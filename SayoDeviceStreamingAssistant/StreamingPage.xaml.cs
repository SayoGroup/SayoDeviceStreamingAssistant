using FontAwesome.WPF;
using Microsoft.Win32;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Packaging;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Point = OpenCvSharp.Point;
using Window = System.Windows.Window;

namespace SayoDeviceStreamingAssistant {
    /// <summary>
    /// StreamingPage.xaml 的交互逻辑
    /// </summary>
    public partial class StreamingPage : Page {
        private DeviceInfo _deviceInfo;
        private WriteableBitmap previewBitmap;
        private Mat previewMat = new Mat();
        private bool newFrame = false;
        private DispatcherTimer previewTimer = new DispatcherTimer();
        public StreamingPage() {
            InitializeComponent();
            SourceCombo.ItemsSource = SourcesManagePage.FrameSources;
            previewTimer.Tick += (sender, e) => {
                UpdatePreview();
            };
        }

        public void BindDevice(DeviceInfo deviceInfo) {
            this._deviceInfo = deviceInfo;
            var screenSize = _deviceInfo.ScreenMat.Size();
            previewBitmap = new WriteableBitmap(screenSize.Width, screenSize.Height, 96, 96, PixelFormats.Bgr565, null);
            Preview.Source = previewBitmap;
            SourceCombo.SelectedIndex = SourcesManagePage.FrameSources.IndexOf(_deviceInfo.FrameSource);
            StreamButton.Content = _deviceInfo.Streaming ? new ImageAwesome { Icon = FontAwesomeIcon.Pause } : 
                new ImageAwesome { Icon = FontAwesomeIcon.Play };
            (StreamButton.Content as ImageAwesome).Foreground = _deviceInfo.Streaming ? Brushes.Green : Brushes.Red;
            _deviceInfo.OnFrameReady += OnDeviceFrameReady;
            if (_deviceInfo.FrameSource?.Fps == null)
                return;
            previewTimer.Interval = TimeSpan.FromMilliseconds(1e3 / _deviceInfo.FrameSource.Fps);
            previewTimer.Start();
        }
        public void UnbindDevice() {
            var mainWindow = (MainWindow)Window.GetWindow(this);
            mainWindow.HideStreamingPage();
            previewTimer.Stop();
            _deviceInfo.OnFrameReady -= OnDeviceFrameReady;
            _deviceInfo = null;
        }
        private void OnDeviceFrameReady(Mat frame) {
            frame.CopyTo(previewMat);
            newFrame = true;
        }
        private void UpdatePreview() {
            if (previewMat != null && newFrame != false) {
                var len = previewMat.Height * previewMat.Width * 2;
                previewBitmap.Lock();
                WinAPI.CopyMemory(previewBitmap.BackBuffer, previewMat.Data, (uint)len);
                previewBitmap.AddDirtyRect(new Int32Rect(0, 0, previewMat.Width, previewMat.Height));
                previewBitmap.Unlock();
                newFrame = false;
            }

            var frameSource = _deviceInfo?.FrameSource;
            if (frameSource == null)
                return;
            var fps = frameSource.Fps.ToString("F2");
            var frameTime = frameSource.FrameTime.ToString("F2");
            FPSLabel.Content = $"{fps} FPS";
            FrameTimeLabel.Content = $"Process: {frameTime}ms";
        }
        private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            _deviceInfo.FrameSource = SourceCombo.SelectedItem as FrameSource;
            previewTimer.Stop();
            if (_deviceInfo.FrameSource?.Fps == null)
                return;
            previewTimer.Interval = TimeSpan.FromMilliseconds(1e3 / _deviceInfo.FrameSource.Fps);
            previewTimer.Start();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) {
            try {
                UnbindDevice();
            }
            catch {
                // ignored
            }
        }

        private void StreamButton_Click(object sender, RoutedEventArgs e) {
            if (_deviceInfo == null)
                return;
            _deviceInfo.Streaming = false;
        }

        private void Preview_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) {
            var mousePos = new Point(e.GetPosition(Preview).X / 2, e.GetPosition(Preview).Y / 2);
            var deltaScale = e.Delta > 0 ? 1.1 : 0.9;

            var rect = _deviceInfo.FrameRect;
            var cursorVec = new Point(mousePos.X - rect.X, mousePos.Y - rect.Y);
            rect.Width = (int)(rect.Width * deltaScale);
            rect.Height = (int)(rect.Height * deltaScale);
            rect.Left = (int)(rect.X - cursorVec.X * (deltaScale - 1) );
            rect.Top = (int)(rect.Y - cursorVec.Y * (deltaScale - 1));
            _deviceInfo.FrameRect = rect;
        }

        System.Windows.Point? mouseDownPose = null;
        OpenCvSharp.Rect? mouseDownFrameRect = null;
        private void Preview_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            mouseDownPose = e.GetPosition(Preview);
            mouseDownFrameRect = _deviceInfo.FrameRect;
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
            _deviceInfo.FrameRect = rect;
        }

        private void ResetPreviewRect_Click(object sender, RoutedEventArgs e) {
            var rect = _deviceInfo.GetDefaultRect();
            if (rect == null)
                return;
            _deviceInfo.FrameRect = rect.Value;
        }

        private void Preview_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
            mouseDownPose = null;
            mouseDownFrameRect = null;
        }

        private void ConfigSourcesButton_Click(object sender, RoutedEventArgs e) {
            var mainWindow = (MainWindow)Window.GetWindow(this);
            mainWindow.ShowSourcesManagePage(SourceCombo.SelectedItem as FrameSource);
        }
    }
}
