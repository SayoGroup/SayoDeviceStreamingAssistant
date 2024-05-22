using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Animation;

namespace SayoDeviceStreamingAssistant {
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow {
        private readonly StreamingPage streamingPage = new StreamingPage();
        private readonly SourcesManagePage sourcesManagePage = new SourcesManagePage();
        private readonly DeviceSelectionPage deviceSelectionPage = new DeviceSelectionPage();

        public MainWindow() {
            InitializeComponent();
            streamingConfigFrame.Navigate(streamingPage);
            sourcesManageFrame.Navigate(sourcesManagePage);
            deviceSelecteFrame.Navigate(deviceSelectionPage);
            this.Closing += (sender, e) => {
                deviceSelectionPage.Dispose();
                //streamingPage.Dispose();
                sourcesManagePage.Dispose();
            };
            visibility.Add(streamingConfigFrame, false);
            visibility.Add(sourcesManageFrame, false);
            visibility.Add(deviceSelecteFrame, true);
        }
        public void ShowStreamingPage(DeviceInfo device) {
            streamingPage.BindDevice(device);
            ToggleFrameVisibility(streamingConfigFrame);
        }
        public void HideStreamingPage() {
            ToggleFrameVisibility(streamingConfigFrame);
        }

        public void ShowSourcesManagePage(FrameSource frameSource) {
            sourcesManagePage.BindSource(frameSource);
            ToggleFrameVisibility(sourcesManageFrame);
        }
        public void HideSourcesManagePage() {
            ToggleFrameVisibility(sourcesManageFrame);
        }

        private readonly Dictionary<UIElement, bool> visibility = new Dictionary<UIElement, bool>();

        private void ToggleFrameVisibility(UIElement ui) {
            var animation = new ThicknessAnimation {
                Duration = TimeSpan.FromSeconds(0.2)
            };
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
