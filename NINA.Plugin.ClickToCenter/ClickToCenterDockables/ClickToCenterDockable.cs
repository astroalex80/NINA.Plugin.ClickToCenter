using CommunityToolkit.Mvvm.Input;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Image.Interfaces;
using NINA.PlateSolving;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem.Platesolving;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Plugin.ClickToCenter.ClickToCenterDockables {

    [Export(typeof(IDockableVM))]
    public class ClickToCenterDockable : DockableVM {

        // Dependencies (Injected)
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly ICameraMediator cameraMediator;
        private readonly IApplicationStatusMediator applicationStatusMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IDomeMediator domeMediator;
        private readonly IDomeFollower domeFollower;
        private readonly IGuiderMediator guiderMediator;

        // State
        private CancellationTokenSource? _centerCts;

        private IRenderedImage _lastRenderedImage;
        private BitmapSource? _lastImage;
        private BitmapSource _lastImageThumbnail;

        private bool _hasClickPoint;
        private double _clickX = -1;
        private double _clickY = -1;

        private bool _isBusy;

        // Internal/services state
        internal IWindowServiceFactory windowServiceFactory;
        internal IWindowService service;
        internal PlateSolvingStatusVM plateSolveStatusVM;
        //internal IProgress<ApplicationStatus> progress;

        [ImportingConstructor]
        public ClickToCenterDockable(
            IProfileService profileService,
            IImagingMediator imagingMediator,
            ITelescopeMediator telescopeMediator,
            ICameraMediator cameraMediator,
            IApplicationStatusMediator applicationStatusMediator,
            IFilterWheelMediator filterWheelMediator,
            IDomeMediator domeMediator,
            IDomeFollower domeFollower,
            IGuiderMediator guiderMediator) : base(profileService) {

            this.profileService = profileService;
            this.imagingMediator = imagingMediator;
            this.telescopeMediator = telescopeMediator;
            this.cameraMediator = cameraMediator;
            this.applicationStatusMediator = applicationStatusMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.domeMediator = domeMediator;
            this.domeFollower = domeFollower;
            this.guiderMediator = guiderMediator;

            // This will reference the resource dictionary to import the SVG graphic and assign it as the icon for the header bar
            var dict = new ResourceDictionary();
            dict.Source = new Uri("NINA.Plugin.ClickToCenter;component/ClickToCenterDockables/ClickToCenterDockableTemplates.xaml", UriKind.RelativeOrAbsolute);
            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["NINA.Plugin.ClickToCenter_CrosshairSVG"];
            ImageGeometry.Freeze();

            Title = "Click to Center";
            SetClickPointCommand = new RelayCommand<object?>(OnSetClickPoint);
            CenterCommand = new AsyncCommand<bool>(_ => CenterAsync(), _ => CanCenter());
            CancelCenterCommand = new RelayCommand(
                execute: CancelCenter,
                canExecute: () => _centerCts is not null && !_centerCts.IsCancellationRequested);

            imagingMediator.ImagePrepared += ImagingMediator_ImagePrepared;

        }

        //public IProgress<ApplicationStatus> Progress {
        //    get {
        //        if (progress == null) {
        //            progress = new Progress<ApplicationStatus>();
        //        }
        //        return progress;
        //    }
        //}

        public RelayCommand<object> SetClickPointCommand { get; }
        public AsyncCommand<bool> CenterCommand { get; }
        public RelayCommand CancelCenterCommand { get; }

        internal IRenderedImage LastRenderedImage {
            get => _lastRenderedImage;
            private set {
                _lastRenderedImage = value;
                RaisePropertyChanged();
            }
        }
        public override bool IsTool { get; } = true;

        public BitmapSource? LastImage {
            get => _lastImage;
            private set {
                if (ReferenceEquals(_lastImage, value)) return;
                _lastImage = value;
                HasClickPoint = false;
                ClickX = -1;
                ClickY = -1;
                RaiseAllPropertiesChanged();
            }
        }

        private int LastImageWidth => LastImage?.PixelWidth ?? 0;
        private int LastImageHeight => LastImage?.PixelHeight ?? 0;

        internal BitmapSource LastImageThumbnail {
            get => _lastImageThumbnail;
            private set {
                _lastImageThumbnail = value;
                RaisePropertyChanged();
            }
        }

        public bool HasClickPoint {
            get => _hasClickPoint;
            private set {
                if (_hasClickPoint == value) return;
                _hasClickPoint = value;
                RaisePropertyChanged();
            }
        }

        public double ClickX {
            get => _clickX;
            private set {
                if (_clickX.Equals(value)) return;
                _clickX = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CrosshairLeft));
            }
        }

        public double ClickY {
            get => _clickY;
            private set {
                if (_clickY.Equals(value)) return;
                _clickY = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CrosshairTop));
            }
        }

        public double CrosshairSize {
            get {
                int w = LastImageWidth;
                int h = LastImageHeight;
                if (w <= 0 || h <= 0) return 0;
                return Math.Min(w, h) / 5.0; // 20% of the smallest dimension
            }
        }

        public double CrosshairLeft => ClickX - CrosshairSize / 2.0;
        public double CrosshairTop => ClickY - CrosshairSize / 2.0;
        internal bool IsBusy {
            get => _isBusy;
            private set {
                if (_isBusy != value) {
                    _isBusy = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool CanCenter() {
            return !IsBusy && LastRenderedImage != null &&
                   cameraMediator?.GetInfo()?.Connected == true &&
                   telescopeMediator?.GetInfo()?.Connected == true &&
                   ClickX >= 0 && ClickY >= 0;
        }
      
        private void ImagingMediator_ImagePrepared(object sender, ImagePreparedEventArgs e) {

            if (e?.RenderedImage?.Image is null) return;

            LastRenderedImage = e.RenderedImage;
            BitmapSource img = e.RenderedImage.Image;
            bool autoStretch = e.Parameters?.AutoStretch ?? true;

            double factor = ActiveProfile.ImageSettings.AutoStretchFactor;
            double bClipping = ActiveProfile.ImageSettings.BlackClipping;
            bool unlinked = ActiveProfile.ImageSettings.UnlinkedStretch;

            if (autoStretch) {
                _ = Task.Run(async () => {
                    IRenderedImage stretched = await e.RenderedImage.Stretch(factor: factor, blackClipping: bClipping, unlinked: unlinked);
                    BitmapSource thumbnail = await stretched.GetThumbnail();
                    Application.Current.Dispatcher.Invoke(() => {
                        LastImage = stretched.Image;
                        LastImageThumbnail = thumbnail;
                    });
                });

            } else {
                _ = Task.Run(async () => {
                        BitmapSource rendered = e.RenderedImage.Image;
                        BitmapSource thumbnail = await e.RenderedImage.GetThumbnail();
                    Application.Current.Dispatcher.Invoke(() => {
                        LastImage = rendered;
                        LastImageThumbnail = thumbnail;
                    });
                });
            }

        }

        private async Task<bool> CenterAsync() {

            _centerCts?.Dispose();
            _centerCts = new CancellationTokenSource();
            CancelCenterCommand.NotifyCanExecuteChanged();

            if (!CanCenter()) {
                return false;
            }

            IsBusy = true;

            try {

                var renderedImage = LastRenderedImage;
                if (renderedImage == null) {
                    return false;
                }

                int width = LastImageWidth;
                int height = LastImageHeight;

                double clickX = ClickX;
                double clickY = ClickY;

                if (clickX < 0 || clickX >= width || clickY < 0 || clickY >= height) {
                    Notification.ShowWarning("No target position set. Click on the image to define a target.");
                    return false;
                }
                
                await PlateSolveCurrentImage(_centerCts.Token);

                if (plateSolveStatusVM.PlateSolveResult is null || plateSolveStatusVM.PlateSolveResult.Success == false) {
                    Notification.ShowError("Plate-solving failed. Click to Center stopped.");
                    return false;
                }

                PlateSolveResult solveResult = plateSolveStatusVM.PlateSolveResult;

                double xOffset = (clickX - width / 2.0);
                double yOffset = (clickY - height / 2.0);

                double deltaXDeg = AstroUtil.ArcsecToDegree(xOffset * solveResult.Pixscale);
                double deltaYDeg = AstroUtil.ArcsecToDegree(yOffset * solveResult.Pixscale);

                Coordinates targetCoords = solveResult.Coordinates.Shift(deltaXDeg, deltaYDeg, /* solveResult.PositionAngle*/ solveResult.Orientation ,
                    Coordinates.ProjectionType.Gnomonic);
                
                Logger.Info($"Pixel offset from image center X: {Math.Round(clickX,3)}; Y: {Math.Round(clickY, 3)} - target coordinates: RA: {targetCoords.RAString}; DEC: {targetCoords.DecString}; Epoch: {targetCoords.Epoch}");
                
                bool centered = await Center(targetCoords, _centerCts.Token);
                
                return centered;

            } catch (OperationCanceledException) {
                Notification.ShowInformation("Click to Center cancelled.", TimeSpan.FromSeconds(5));
                Logger.Info("Click to Center cancelled.");
                return false;
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
                return false;
            } finally {
                HasClickPoint = false;
                ClickX = -1; // reset click points
                ClickY = -1;
                _centerCts?.Dispose();
                _centerCts = null;
                CancelCenterCommand.NotifyCanExecuteChanged();
                IsBusy = false;
            }

        }

        private IWindowServiceFactory WindowServiceFactory {
            get {
                if (windowServiceFactory == null) {
                    windowServiceFactory = new WindowServiceFactory();
                }
                return windowServiceFactory;
            }
            set => windowServiceFactory = value;
        }

        private async Task<bool> PlateSolveCurrentImage(CancellationToken cancellationToken) {

            var renderedImage = LastRenderedImage;

            if (renderedImage == null) return false;
            try {
                if (plateSolveStatusVM == null) {
                    plateSolveStatusVM = new PlateSolvingStatusVM();
                    service = WindowServiceFactory.Create();
                } else {
                    await service.Close();
                }

                var cameraInfo = cameraMediator.GetInfo();
                var binning = cameraInfo?.BinX ?? 1;

                var plateSolver = PlateSolverFactory.GetPlateSolver(profileService.ActiveProfile.PlateSolveSettings);
                var blindSolver = PlateSolverFactory.GetBlindSolver(profileService.ActiveProfile.PlateSolveSettings);
                var parameter = new PlateSolveParameter() {
                    Binning = binning,
                    Coordinates = telescopeMediator.GetCurrentPosition(),
                    DownSampleFactor = profileService.ActiveProfile.PlateSolveSettings.DownSampleFactor,
                    FocalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength,
                    MaxObjects = profileService.ActiveProfile.PlateSolveSettings.MaxObjects,
                    PixelSize = profileService.ActiveProfile.CameraSettings.PixelSize,
                    Regions = profileService.ActiveProfile.PlateSolveSettings.Regions,
                    SearchRadius = profileService.ActiveProfile.PlateSolveSettings.SearchRadius,
                    BlindFailoverEnabled = profileService.ActiveProfile.PlateSolveSettings.BlindFailoverEnabled
                };

                var imageSolver = new ImageSolver(plateSolver, blindSolver);

                service.Show(plateSolveStatusVM, this.Title + " - " + plateSolveStatusVM.Title, ResizeMode.CanResize,
                    WindowStyle.ToolWindow);
                plateSolveStatusVM.PlateSolveResult = null;
                plateSolveStatusVM.Thumbnail = LastImageThumbnail;

                IProgress<ApplicationStatus> progress = new Progress<ApplicationStatus>();

                var result = await imageSolver.Solve(renderedImage.RawImageData, parameter,
                    plateSolveStatusVM.CreateLinkedProgress(progress), cancellationToken);

                plateSolveStatusVM.PlateSolveResult = result;
            } catch (OperationCanceledException) {
                Logger.Info("Plate solving cancelled.");
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            } finally {
                service?.DelayedClose(TimeSpan.FromSeconds(2));
            }
            return true;

        }

        private void CancelCenter() {
            try {
                _centerCts?.Cancel();
            } catch (ObjectDisposedException) {
                ;
            }

            CancelCenterCommand.NotifyCanExecuteChanged();
        }

        private void OnSetClickPoint(object? p) {

            var width = LastImage?.Width ?? 0;
            var height = LastImage?.Height ?? 0;

            if (p == null || width == 0 || height == 0) return;

            var point = (Point)p;

            if (point.X < 0 || point.X > width || point.Y < 0 || point.Y > height) {
                return;
            }

            ClickX = point.X;
            ClickY = point.Y;
            HasClickPoint = true;
        }

        private async Task<bool> Center(Coordinates coordinates, CancellationToken token) {
            var center = new Center(profileService, telescopeMediator, imagingMediator, filterWheelMediator, guiderMediator, domeMediator, domeFollower, new PlateSolverFactoryProxy(), new WindowServiceFactory());

            center.Coordinates = new InputCoordinates(coordinates);
            var isValid = center.Validate();

            if (!isValid) {
                Notification.ShowError(string.Join(Environment.NewLine, center.Issues));
                return false;
            }

            IProgress<ApplicationStatus> progress = new Progress<ApplicationStatus>();

            await center.Run(progress, token);
            return true;
        }
        public void Dispose() {
            imagingMediator.ImagePrepared -= ImagingMediator_ImagePrepared;
        }
    }
}
