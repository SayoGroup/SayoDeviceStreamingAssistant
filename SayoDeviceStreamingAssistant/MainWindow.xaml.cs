using Composition.WindowsRuntimeHelpers;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.UI.Xaml.Controls;
using Window = System.Windows.Window;
using Size = OpenCvSharp.Size;
using System.Windows.Media.Animation;

namespace SayoDeviceStreamingAssistant {
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window {
        private DeviceSelectionPage _deviceSelectionPage = new DeviceSelectionPage();
        private StreamingPage _streamingPage = new StreamingPage();
        private SourcesManagePage _sourcesManagePage = new SourcesManagePage();

        public MainWindow() {
            InitializeComponent();
            deviceSelectePage.Navigate(_deviceSelectionPage);
            streamingConfigPage.Navigate(_streamingPage);
            sourcesManagePage.Navigate(_sourcesManagePage);
            this.Closing += (sender, e) => {
                _deviceSelectionPage.Dispose();
                //_streamingPage.Dispose();
                //_sourcesManagePage.Dispose();
            };
            visibility.Add(deviceSelectePage, true);
            visibility.Add(streamingConfigPage, false);
            visibility.Add(sourcesManagePage, false);
        }
        public void ShowStreamingPage(DeviceInfo device) {
            _streamingPage.BindDevice(device);
            ToggleFrameVisibility(streamingConfigPage);
        }
        public void HideStreamingPage() {
            ToggleFrameVisibility(streamingConfigPage);
        }

        public void ShowSourcesManagePage(FrameSource frameSource) {
            _sourcesManagePage.BindSource(frameSource);
            ToggleFrameVisibility(sourcesManagePage);
        }
        public void HideSourcesManagePage() {
            ToggleFrameVisibility(sourcesManagePage);
        }

        private Dictionary<UIElement, bool> visibility = new Dictionary<UIElement, bool>();
        public void ToggleFrameVisibility(UIElement ui) {
            ThicknessAnimation animation = new ThicknessAnimation();
            animation.Duration = TimeSpan.FromSeconds(0.2);
            if (visibility[ui]) {
                animation.To = new Thickness(-ActualWidth * 2, 0, 0, 0);
                visibility[ui] = false;
            } else {
                animation.To = new Thickness(0, 0, 0, 0);
                visibility[ui] = true;
            }
            ui.BeginAnimation(MarginProperty, animation);
        }
        

    }
}
