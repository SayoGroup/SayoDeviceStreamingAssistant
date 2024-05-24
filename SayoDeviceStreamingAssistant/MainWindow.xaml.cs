using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SayoDeviceStreamingAssistant {
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow {
        private readonly StreamingPage streamingPage = new StreamingPage();
        private readonly SourcesManagePage sourcesManagePage = new SourcesManagePage();
        private readonly DeviceSelectionPage deviceSelectionPage = new DeviceSelectionPage();
        private Page currentPage;

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
            currentPage = deviceSelectionPage;
        }
        public void ShowStreamingPage(DeviceInfo device) {
            currentPage = streamingPage;
            streamingConfigFrame.IsHitTestVisible = true;
            streamingPage.ShowPage(device);
            SetFrameVisibility(streamingConfigFrame, true);
            SetBackButtonVisibility(true);
        }

        public void ShowSourcesManagePage(FrameSource frameSource) {
            currentPage = sourcesManagePage;
            sourcesManageFrame.IsHitTestVisible = true;
            sourcesManagePage.ShowPage(frameSource);
            SetFrameVisibility(sourcesManageFrame, true);
            SetBackButtonVisibility(true);
            //streamingPage.HidePage();
        }

        private void SetFrameVisibility(UIElement ui,bool show) {
            var animation = new DoubleAnimation {
                Duration = TimeSpan.FromSeconds(0.2)
            };
            animation.To = show ? 1 : 0;
            ui.BeginAnimation(OpacityProperty, animation);
        }
        private void SetBackButtonVisibility(bool show) {
            var animation = new DoubleAnimation {
                Duration = TimeSpan.FromSeconds(0.2)
            };
            animation.To = show ? 1 : 0;
            BackButton.BeginAnimation(OpacityProperty, animation);
            BackButton.IsHitTestVisible = show;
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            this.DragMove();
        }

        private void TitleBar_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) {

        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) {
            this.Close();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) {
            if (currentPage == streamingPage) {
                streamingConfigFrame.IsHitTestVisible = false;
                SetFrameVisibility(streamingConfigFrame, false);
                streamingPage.HidePage();
                currentPage = deviceSelectionPage;
                SetBackButtonVisibility(false);
            } else if (currentPage == sourcesManagePage) {
                sourcesManageFrame.IsHitTestVisible = false;
                SetFrameVisibility(sourcesManageFrame, false);
                sourcesManagePage.HidePage();
                currentPage = streamingPage;
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
            this.WindowState = WindowState.Minimized;
        }

        private void Window_Activated(object sender, EventArgs e) {
            //Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => WindowStyle = WindowStyle.None));
            //Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => AllowsTransparency = true));
        }
    }
}
