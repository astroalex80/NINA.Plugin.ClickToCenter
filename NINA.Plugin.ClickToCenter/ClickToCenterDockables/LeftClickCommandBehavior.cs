using NINA.WPF.Base.View;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using static NINA.Plugin.ClickToCenter.ClickToCenterDockables.ClickToCenterDockable;

namespace NINA.Plugin.ClickToCenter.ClickToCenterDockables {
    public static class LeftClickCommandBehavior {

        private const string CrosshairTag = "ClickToCenter_Crosshair";

        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.RegisterAttached(
                "Command",
                typeof(ICommand),
                typeof(LeftClickCommandBehavior),
                new PropertyMetadata(null, OnCommandChanged));

        public static void SetCommand(DependencyObject obj, ICommand value) {
            obj.SetValue(CommandProperty, value);
        }

        public static ICommand GetCommand(DependencyObject obj) {
            return (ICommand)obj.GetValue(CommandProperty);
        }

        private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is UIElement ui) {
                if (e.OldValue != null) {
                   // ui.PreviewMouseLeftButtonDown -= Ui_PreviewMouseLeftButtonDown;
                    ui.PreviewMouseRightButtonDown -= Ui_PreviewMouseLeftButtonDown;
                }
                if (e.NewValue != null) {
                    //ui.PreviewMouseLeftButtonDown += Ui_PreviewMouseLeftButtonDown;
                    ui.PreviewMouseRightButtonDown += Ui_PreviewMouseLeftButtonDown;
                }
            }
        }


        private static readonly DependencyProperty CrosshairHandlerProperty =
            DependencyProperty.RegisterAttached(
                "CrosshairHandler",
                typeof(EventHandler),
                typeof(LeftClickCommandBehavior),
                new PropertyMetadata(null));

        private static void EnsureSubscribed(ImageView imageView, Canvas canvas) {
            if (canvas.GetValue(CrosshairHandlerProperty) is EventHandler) return;
            if (imageView.DataContext is not ClickToCenterDockable vm) return;

            EventHandler handler = (_, __) => {
                if (!canvas.Dispatcher.CheckAccess()) {
                    canvas.Dispatcher.Invoke(() => RemoveCrosshair(canvas));
                } else {
                    RemoveCrosshair(canvas);
                }
            };

            vm.CrosshairClearRequested += handler;
            canvas.SetValue(CrosshairHandlerProperty, handler);

            canvas.Unloaded += (_, __) => {
                if (canvas.GetValue(CrosshairHandlerProperty) is EventHandler h) {
                    vm.CrosshairClearRequested -= h;
                    canvas.ClearValue(CrosshairHandlerProperty);
                }
            };
        }

        private static void Ui_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            if (sender is not ImageView imageView) {
                return;
            }

            var command = GetCommand(imageView);
            if (command == null) {
                return;
            }
            //var image = FindDescendant<System.Windows.Controls.Image>(imageView, "PART_Image");
            //var canvas = FindDescendant<Canvas>(imageView, "PART_Canvas");
            //if (image == null || canvas == null) return;

            //// 1) Position im Image-Element (Viewport-Koordinate)
            //var pos = e.GetPosition(image);
            //var posInCanvas = e.GetPosition(canvas);

            //if (pos.X < 0 || pos.Y < 0 ||
            //    pos.X >= canvas.ActualWidth ||
            //    pos.Y >= canvas.ActualHeight) {
            //    return;
            //}

            //var pointInfo = new ClickPointInfo(pos);

            //if (command.CanExecute(pointInfo)) {
            //    command.Execute(pointInfo);
            //}

            //RemoveCrosshair(canvas);
            //DrawCrosshair(posInCanvas, canvas);

            var canvas = FindDescendant<Canvas>(imageView, "PART_Canvas");
            if (canvas == null) {
                return;
            }

            EnsureSubscribed(imageView, canvas);

            var pos = e.GetPosition(canvas);

            if (pos.X < 0 || pos.Y < 0 ||
                pos.X >= canvas.ActualWidth ||
                pos.Y >= canvas.ActualHeight) {
                return;
            }

            var pointInfo = new ClickPointInfo(pos);

            if (command.CanExecute(pointInfo)) {
                command.Execute(pointInfo);
            }

            RemoveCrosshair(canvas);
            DrawCrosshair(pos, canvas);
        }

        private static void DrawCrosshair(Point pos, Canvas canvas) {

            if (canvas.TryFindResource("NINA.Plugin.ClickToCenter_CrosshairPath") is not Path template) {
                return;
            }

            var path = template;

            path.Data.Freeze();
            path.Tag = CrosshairTag;
            path.IsHitTestVisible = false;
            path.Width = canvas.ActualWidth / 4; // 1/4 of the image size for better visibility on large images
            path.Height = canvas.ActualHeight / 4;
            var posX = pos.X - path.Width / 2.0;
            var posY = pos.Y - path.Height / 2.0;
            path.RenderTransform = new TranslateTransform(posX, posY);

            canvas.Children.Add(path);

        }

        public static void RemoveCrosshair(Canvas canvas) {
            
            var oldCrosshairs = canvas.Children
                .OfType<Path>()
                .Where(p => Equals(p.Tag, CrosshairTag))
                .ToList();

            foreach (var marker in oldCrosshairs) {
                canvas.Children.Remove(marker);
            }
        }

        public static T? FindDescendant<T>(DependencyObject root, string? name = null)
            where T : FrameworkElement {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++) {
                var child = VisualTreeHelper.GetChild(root, i);

                if (child is T t && (name == null || t.Name == name)) {
                    return t;
                }

                var result = FindDescendant<T>(child, name);
                if (result != null) {
                    return result;
                }
            }

            return null;
        }

    }
}
