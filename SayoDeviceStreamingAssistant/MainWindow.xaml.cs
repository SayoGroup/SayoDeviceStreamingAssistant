using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Animation;

namespace SayoDeviceStreamingAssistant {
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow {
        private readonly DeviceSelectionPage deviceSelectionPage = new DeviceSelectionPage();
        private readonly StreamingPage streamingPage = new StreamingPage();
        private readonly SourcesManagePage sourcesManagePage = new SourcesManagePage();

        public MainWindow() {
            InitializeComponent();
            deviceSelecteFrame.Navigate(deviceSelectionPage);
            streamingConfigFrame.Navigate(streamingPage);
            sourcesManageFrame.Navigate(sourcesManagePage);
            this.Closing += (sender, e) => {
                deviceSelectionPage.Dispose();
                //_streamingPage.Dispose();
                //_sourcesManagePage.Dispose();
            };
            visibility.Add(deviceSelecteFrame, true);
            visibility.Add(streamingConfigFrame, false);
            visibility.Add(sourcesManageFrame, false);
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
