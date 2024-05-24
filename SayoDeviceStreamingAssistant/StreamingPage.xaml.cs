using FontAwesome.WPF;
using OpenCvSharp;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Point = OpenCvSharp.Point;
using Window = System.Windows.Window;

namespace SayoDeviceStreamingAssistant {
    /// <summary>
    /// StreamingPage.xaml 的交互逻辑
    /// </summary>
    public partial class StreamingPage {
        private DeviceInfo bindDeviceInfo;
        private WriteableBitmap previewBitmap;
        private readonly Mat previewMat = new Mat();
        private bool newFrame;
        private readonly DispatcherTimer previewTimer = new DispatcherTimer();
        public StreamingPage() {
            InitializeComponent();
            SourceCombo.ItemsSource = SourcesManagePage.FrameSources;
            previewTimer.Tick += (sender, e) => {
                UpdatePreview();
            };
        }

        public void ShowPage(DeviceInfo deviceInfo) {
            this.bindDeviceInfo = deviceInfo;
            var screenSize = bindDeviceInfo.ScreenMat.Size();
            previewBitmap = new WriteableBitmap(screenSize.Width, screenSize.Height, 96, 96, PixelFormats.Bgr565, null);
            Preview.Source = previewBitmap;
            SourceCombo.SelectedIndex = SourcesManagePage.FrameSources.IndexOf(bindDeviceInfo.FrameSource);
            SetStreamButton();
            bindDeviceInfo.OnFrameReady += OnBindDeviceFrameReady;
            if (bindDeviceInfo.FrameSource?.Fps == null)
                return;
            previewTimer.Interval = TimeSpan.FromMilliseconds(1e3 / bindDeviceInfo.FrameSource.Fps);
            previewTimer.Start();
        }
        public void HidePage() {
            previewTimer.Stop();
            bindDeviceInfo.OnFrameReady -= OnBindDeviceFrameReady;
            bindDeviceInfo = null;
        }
        private void OnBindDeviceFrameReady(Mat frame) {
            frame.CopyTo(previewMat);
            newFrame = true;
        }
        private void UpdatePreview() {
            if (previewMat != null && newFrame) {
                var len = previewMat.Height * previewMat.Width * 2;
                previewBitmap.Lock();
                WinApi.CopyMemory(previewBitmap.BackBuffer, previewMat.Data, (uint)len);
                previewBitmap.AddDirtyRect(new Int32Rect(0, 0, previewMat.Width, previewMat.Height));
                previewBitmap.Unlock();
                newFrame = false;
            }

            var frameSource = bindDeviceInfo?.FrameSource;
            if (frameSource == null)
                return;
            var fps = frameSource.Fps.ToString("F2");
            var frameTime = frameSource.FrameTime.ToString("F2");
            FPSLabel.Content = $"{bindDeviceInfo.SendImageRate.ToString("F2")}/{fps} FPS";
            FrameTimeLabel.Content = $"Process: {frameTime}ms";
            SendImageElapsedLabel.Content = $"Send: {bindDeviceInfo.SendImageElapsed.ToString("F2")}ms";
        }
        private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            var newSource = SourceCombo.SelectedItem as FrameSource;
            if (newSource == bindDeviceInfo.FrameSource)
                return;
            bindDeviceInfo.FrameSource = newSource;
            previewTimer.Stop();
            if (bindDeviceInfo.FrameSource?.Fps == null)
                return;
            previewTimer.Interval = TimeSpan.FromMilliseconds(1e3 / bindDeviceInfo.FrameSource.Fps);
            previewMat.SetTo(new Scalar(0, 0, 0));
            newFrame = true;
            previewTimer.Start();
            
        }

        private void StreamButton_Click(object sender, RoutedEventArgs e) {
            if (bindDeviceInfo == null)
                return;
            bindDeviceInfo.Streaming = !bindDeviceInfo.Streaming;
            SetStreamButton();
        }
        private void SetStreamButton() {
            StreamButton.ToolTip = bindDeviceInfo.Streaming ? Properties.Resources.StreamingPage_SetStreamButton_Pause_streaming 
                : Properties.Resources.StreamingPage_SetStreamButton_Begin_streaming;
            StreamButton.Content = bindDeviceInfo.Streaming ? new ImageAwesome { Icon = FontAwesomeIcon.Pause } :
                new ImageAwesome { Icon = FontAwesomeIcon.Play };
            ((ImageAwesome)StreamButton.Content).Foreground = bindDeviceInfo.Streaming ? Brushes.Red : Brushes.Green;
        }

        private void Preview_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) {
            var mousePos = new Point(e.GetPosition(Preview).X / 2, e.GetPosition(Preview).Y / 2);
            var deltaScale = e.Delta > 0 ? 1.1 : 0.9;

            if (bindDeviceInfo.FrameRect == null)
                return;
            var rect = bindDeviceInfo.FrameRect.Value;

            var cursorVec = new Point(mousePos.X - rect.X, mousePos.Y - rect.Y);
            rect.Width = (int)(rect.Width * deltaScale);
            rect.Height = (int)(rect.Height * deltaScale);
            rect.Left = (int)(rect.X - cursorVec.X * (deltaScale - 1));
            rect.Top = (int)(rect.Y - cursorVec.Y * (deltaScale - 1));
            bindDeviceInfo.FrameRect = rect;
            bindDeviceInfo.PeekFrame();
        }

        private System.Windows.Point? mouseDownPose;
        private OpenCvSharp.Rect? mouseDownFrameRect;
        private void Preview_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            mouseDownPose = e.GetPosition(Preview);
            mouseDownFrameRect = bindDeviceInfo.FrameRect;
        }

        private void Preview_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            mouseDownPose = null;
            mouseDownFrameRect = null;
        }


        private void Preview_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) {
            if (mouseDownPose == null || mouseDownFrameRect == null)
                return;
            var rect = mouseDownFrameRect.Value;
            var pos = e.GetPosition(Preview);
            var delta = new Point(pos.X - mouseDownPose.Value.X, pos.Y - mouseDownPose.Value.Y);
            rect.Left += delta.X / 2;
            rect.Top += delta.Y / 2;
            bindDeviceInfo.FrameRect = rect;
            bindDeviceInfo.PeekFrame();
        }

        private void ResetPreviewRect_Click(object sender, RoutedEventArgs e) {
            var rect = bindDeviceInfo.GetDefaultRect();
            if (rect == null)
                return;
            bindDeviceInfo.FrameRect = rect.Value;
            bindDeviceInfo.PeekFrame();
        }

        private void Preview_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
            mouseDownPose = null;
            mouseDownFrameRect = null;
        }

        private void ConfigSourcesButton_Click(object sender, RoutedEventArgs e) {
            var mainWindow = (MainWindow)Window.GetWindow(this);
            mainWindow?.ShowSourcesManagePage(SourceCombo.SelectedItem as FrameSource);
        }
    }
}
