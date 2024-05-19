using FontAwesome.WPF;
using Microsoft.Win32;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    public partial class StreamingPage : Page {
        private DeviceInfo deviceInfo;
        readonly List<string> types = new List<string> { "Monitor", "Window", "Media" };
        private WriteableBitmap previewBitmap;
        private Mat previewMat;
        private bool newFrame = false; 
        public StreamingPage() {
            InitializeComponent();
            TypeCombo.ItemsSource = types;
            TypeCombo.SelectedIndex = 0;

            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(1e3 / 60);
            timer.Tick += (sender, e) => {
                UpdatePreview();
            };
            timer.Start();
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

            var screen = deviceInfo?.Device?.ScreenStream?.FrameSource;
            if (screen == null)
                return;
            var fps = screen.Fps.ToString("F2");
            var frameTime = screen.FrameTime.ToString("F2");
            FPSLabel.Content = $"{fps} FPS";
            FrameTimeLabel.Content = $"Process: {frameTime}ms";

        }

        public void SetDevice(DeviceInfo deviceInfo) {
            var screenInfo = deviceInfo.Device.ScreenStream.ScreenInfo;
            previewBitmap = new WriteableBitmap(screenInfo.Width, screenInfo.Height, 96, 96, PixelFormats.Bgr565, null);
            previewMat = new Mat(screenInfo.Height, screenInfo.Width, MatType.CV_16UC2);
            Preview.Source = previewBitmap;
            this.deviceInfo = deviceInfo;
            var device = this.deviceInfo.Device;
            var type = device.ScreenStream.SourceType;
            TypeCombo.SelectedIndex = types.IndexOf(type);
            switch (type) {
                case "Monitor":
                    var monitors = MonitorEnumerationHelper.GetMonitors();
                    var monitor = device.ScreenStream.SourceMonitor;
                    ItemCombo.ItemsSource = monitors;
                    ItemCombo.SelectedIndex = monitors.ToList().FindIndex(m => m.Hmon == monitor.Hmon);
                    break;
                case "Window":
                    var processes = from p in Process.GetProcesses()
                                    where !string.IsNullOrWhiteSpace(p.MainWindowTitle) && WindowEnumerationHelper.IsWindowValidForCapture(p.MainWindowHandle)
                                    select p;
                    var process = device.ScreenStream.SourceProcess;
                    //var processes = WindowEnumerationHelper.GetWindows();
                    ItemCombo.ItemsSource = processes;
                    ItemCombo.SelectedIndex = processes.ToList().FindIndex(m => m.Id == process.Id);
                    break;
                case "Media":
                    var medias = ((MainWindow)Window.GetWindow(this)).medias;
                    var media = device.ScreenStream.SourceMedia;
                    ItemCombo.ItemsSource = medias;
                    ItemCombo.SelectedIndex = medias.ToList().FindIndex(m => m.MainWindowTitle == media);
                    break;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) {
            try { 
                deviceInfo.Device.ScreenStream.FrameSource.OnFrameReady -= OnDeviceFrameReady;
            }
            catch {
                // ignored
            }

            var mainWindow = (MainWindow)Window.GetWindow(this);
            mainWindow?.ToggleFrameVisibility();
            ItemCombo.SelectedItem = null;
            TypeCombo.SelectedItem = null;
        }

        private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (TypeCombo.SelectedItem == null)
                return;
            var type = TypeCombo.SelectedItem.ToString();

            switch (type) {
                case "Monitor":
                    var monitors = MonitorEnumerationHelper.GetMonitors();
                    ItemCombo.ItemsSource = monitors;
                    break;
                case "Window":
                    var processes = from p in Process.GetProcesses()
                                               where p.MainWindowHandle != null && WindowEnumerationHelper.IsWindowValidForCapture(p.MainWindowHandle)
                                               select p;
                    ItemCombo.ItemsSource = processes;
                    break;
                case "Media":
                    var medias = ((MainWindow)Window.GetWindow(this)).medias;
                    ItemCombo.ItemsSource = medias;
                    break;
            }
        }

        private void ItemCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (ItemCombo.SelectedItem == null)
                return;
            var type = TypeCombo.SelectedItem.ToString();
            var device = deviceInfo.Device;
            switch (type) {
                case "Monitor":
                    var monitor = ItemCombo.SelectedItem as MonitorInfo;
                    if (monitor.Hmon != device.ScreenStream.SourceMonitor?.Hmon)
                        device.ScreenStream.SetFrameSource(monitor);
                    break;
                case "Window":
                    var process = ItemCombo.SelectedItem as Process;
                    if (process.Id != device.ScreenStream.SourceProcess?.Id)
                        device.ScreenStream.SetFrameSource(process);
                    break;
                case "Media":
                    var media = ItemCombo.SelectedItem as MainWindow.Media;
                    if (media.MainWindowTitle != device.ScreenStream.SourceMedia)
                        device.ScreenStream.SetFrameSource(media.MainWindowTitle);
                    break;
            }
            var screen = device?.ScreenStream?.FrameSource;
            if (screen == null)
                return;
            screen.OnFrameReady += OnDeviceFrameReady;
            OnDeviceFrameReady(screen.PeekFrame());
        }

        private void StreamButton_Click(object sender, RoutedEventArgs e) {
            if (deviceInfo == null)
                return;
            var device = deviceInfo.Device;
            if (device.ScreenStream.FrameSource == null)
                return;
            var status = device.ScreenStream.Enabled;
            device.ScreenStream.Enabled = !status;
            deviceInfo.StreamingStatus = !status ? device.ScreenStream.GetSourceName() : "Paused";
            StreamButton.Content = status ? new ImageAwesome { Icon = FontAwesomeIcon.Play } : 
                new ImageAwesome { Icon = FontAwesomeIcon.Pause };
            (StreamButton.Content as ImageAwesome).Foreground = status ? Brushes.Green : Brushes.Red;
        }

        private void AddMediaButton_Click(object sender, RoutedEventArgs e) {
            var mainWindow = (MainWindow)Window.GetWindow(this);
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = "选择视频文件";

            // 设置文件类型过滤器
            openFileDialog.Filter = "视频文件 (*.avi; *.mp4; *.mkv; *.mov; *.wmv; *.flv; *.rmvb)|*.avi;*.mp4;*.mkv;*.mov;*.wmv;*.flv;*.rmvb|所有文件 (*.*)|*.*";
            openFileDialog.Multiselect = true;
            bool? result = openFileDialog.ShowDialog();
            if (result == true) {
                var selectedFilePath = openFileDialog.FileNames;
                foreach (var path in selectedFilePath) {
                    mainWindow.AddMedia(path);
                }
            }
        }

        private void Preview_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) {
            var screen = deviceInfo?.Device?.ScreenStream?.FrameSource;
            if (screen == null)
                return;
            var mousePos = new Point(e.GetPosition(Preview).X / 2, e.GetPosition(Preview).Y / 2);
            var deltaScale = e.Delta > 0 ? 1.1 : 0.9;

            var rect = screen.FrameRect;
            var cursorVec = new Point(mousePos.X - rect.X, mousePos.Y - rect.Y);
            rect.Width = (int)(rect.Width * deltaScale);
            rect.Height = (int)(rect.Height * deltaScale);
            rect.Left = (int)(rect.X - cursorVec.X * (deltaScale - 1) );
            rect.Top = (int)(rect.Y - cursorVec.Y * (deltaScale - 1));
            screen.FrameRect = rect;
        }

        System.Windows.Point? mouseDownPose = null;
        OpenCvSharp.Rect? mouseDownFrameRect = null;
        private void Preview_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            mouseDownPose = e.GetPosition(Preview);
            var screen = deviceInfo?.Device?.ScreenStream?.FrameSource;
            if (screen == null)
                return;
            mouseDownFrameRect = screen.FrameRect;
        }

        private void Preview_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            mouseDownPose = null;
            mouseDownFrameRect = null;
        }


        private void Preview_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) {
            if (mouseDownPose == null || mouseDownFrameRect == null)
                return;
            var screen = deviceInfo?.Device?.ScreenStream?.FrameSource;
            if (screen == null)
                return;
            var rect = mouseDownFrameRect.Value;
            var pos = e.GetPosition(Preview);
            var delta = new Point(pos.X - mouseDownPose.Value.X, pos.Y - mouseDownPose.Value.Y);
            rect.Left += delta.X / 2;
            rect.Top += delta.Y / 2;
            screen.FrameRect = rect;
        }

        private void ResetPreviewRect_Click(object sender, RoutedEventArgs e) {
            var screen = deviceInfo?.Device?.ScreenStream?.FrameSource;
            if (screen == null)
                return;
            screen.FrameRect = screen.GetDefaultRect();
        }

        private void Preview_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
            mouseDownPose = null;
            mouseDownFrameRect = null;
        }
    }
}
