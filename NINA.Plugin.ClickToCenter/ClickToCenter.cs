using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Image.ImageData;
using NINA.Plugin;
using NINA.Plugin.ClickToCenter.Properties;
using NINA.Plugin.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Settings = NINA.Plugin.ClickToCenter.Properties.Settings;

namespace NINA.Plugin.ClickToCenter {
   
    [Export(typeof(IPluginManifest))]
    public class ClickToCenter : PluginBase {
        
        [ImportingConstructor]
        public ClickToCenter() {

        }

        public override Task Teardown() {
            return base.Teardown();
        }
    }
}
