using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SayoDeviceStreamingAssistant.Pages;

namespace SayoDeviceStreamingAssistant {
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow {
        public readonly Settings settingsPage = new Settings();
        private readonly StreamingPage streamingPage = new StreamingPage();
        private readonly SourcesManagePage sourcesManagePage = new SourcesManagePage();
        private readonly DeviceSelectionPage deviceSelectionPage = new DeviceSelectionPage();
        private Page currentPage;

        public MainWindow() {
            InitializeComponent();
            streamingConfigFrame.Navigate(streamingPage);
            sourcesManageFrame.Navigate(sourcesManagePage);
            deviceSelecteFrame.Navigate(deviceSelectionPage);
            settingsFrame.Navigate(settingsPage);
            this.Closing += (sender, e) => {
                deviceSelectionPage.Dispose();
                //streamingPage.Dispose();
                sourcesManagePage.Dispose();
                settingsPage.Dispose();
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
        public void ShowSettingsPage() {
            currentPage = settingsPage;
            settingsFrame.IsHitTestVisible = true;
            SetFrameVisibility(settingsFrame, true);
            SetBackButtonVisibility(true);
        }

        private void SetFrameVisibility(UIElement ui,bool show) {
            var animation = new DoubleAnimation {
                Duration = TimeSpan.FromSeconds(0.2)
            };
            animation.To = show ? 1 : 0;
            ui.BeginAnimation(OpacityProperty, animation);
        }
        private void SetBackButtonVisibility(bool show) {
            var backBtnAnim = new DoubleAnimation {
                Duration = TimeSpan.FromSeconds(0.2)
            };
            var settingBtnAnim = new DoubleAnimation
            {
                Duration = TimeSpan.FromSeconds(0.2)
            };
            backBtnAnim.To = show ? 1 : 0;
            settingBtnAnim.To = show ? 0 : 1;
            BackButton.BeginAnimation(OpacityProperty, backBtnAnim);
            BackButton.IsHitTestVisible = show;
            SettingsButton.BeginAnimation(OpacityProperty, settingBtnAnim);
            SettingsButton.IsHitTestVisible = !show;
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
                if(sourcesManagePage.selectedSource != null)
                    streamingPage.SourceCombo.SelectedItem = sourcesManagePage.selectedSource;
                sourcesManageFrame.IsHitTestVisible = false;
                SetFrameVisibility(sourcesManageFrame, false);
                sourcesManagePage.HidePage();
                currentPage = streamingPage;
            } else if (currentPage == settingsPage) {
                settingsFrame.IsHitTestVisible = false;
                SetFrameVisibility(settingsFrame, false);
                currentPage = deviceSelectionPage;
                SetBackButtonVisibility(false);
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
            this.WindowState = WindowState.Minimized;
        }

        private void Window_Activated(object sender, EventArgs e) {
            //Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => WindowStyle = WindowStyle.None));
            //Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => AllowsTransparency = true));
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsPage();
        }
    }
}
