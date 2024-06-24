using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FontAwesome.WPF;
using OpenCvSharp;
using SayoDeviceStreamingAssistant.Sources;
using FrameSource = SayoDeviceStreamingAssistant.Sources.FrameSource;
using RectInt =OpenCvSharp.Rect;
using RectDouble = OpenCvSharp.Rect2d;
using SizeInt = OpenCvSharp.Size;
using SizeDouble = OpenCvSharp.Size2d;
using Window = System.Windows.Window;

namespace SayoDeviceStreamingAssistant.Pages {
    /// <summary>
    /// StreamingPage.xaml 的交互逻辑
    /// </summary>
    public partial class StreamingPage {
        private DeviceInfo bindDeviceInfo;
        private WriteableBitmap previewBitmap;
        private Mat previewMat;
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
            previewMat = new Mat(new SizeInt((int)screenSize.Width, (int)screenSize.Height), MatType.CV_8UC2);//new Mat(screenSize.Height, screenSize.Width, Depth.U8, 2);
            previewBitmap = new WriteableBitmap(screenSize.Width, screenSize.Height, 96, 96, PixelFormats.Bgr565, null);
            Preview.Source = previewBitmap;
            SourceCombo.SelectedIndex = SourcesManagePage.FrameSources.IndexOf(bindDeviceInfo.FrameSource);
            SetStreamButton();
            bindDeviceInfo.OnFrameReady += OnBindDeviceFrameReady;
            if (bindDeviceInfo.FrameSource == null)
            {
                Preview.Visibility = Visibility.Hidden;
                return;
            }
            Preview.Visibility = Visibility.Visible;
            previewTimer.Interval = TimeSpan.FromMilliseconds(1e3 / bindDeviceInfo.FrameSource.Fps);
            previewTimer.Start();
        }
        public void HidePage() {
            previewTimer.Stop();
            bindDeviceInfo.OnFrameReady -= OnBindDeviceFrameReady;
            bindDeviceInfo = null;
        }
        private void OnBindDeviceFrameReady(Mat frame) {
            //frame.DrawToBgr565(previewMat, MatExtension.GetDefaultRect(frame.Size, previewMat.Size));
            frame.CopyTo(previewMat);
            //CV.Copy(frame,previewMat);
            newFrame = true;
        }
        private void UpdatePreview() {
            if (previewMat != null && newFrame) {
                var len = previewMat.Rows * previewMat.Cols * 2;
                previewBitmap.Lock();
                WinApi.CopyMemory(previewBitmap.BackBuffer, previewMat.Data, (uint)len);
                previewBitmap.AddDirtyRect(new Int32Rect(0, 0, previewMat.Cols, previewMat.Rows));
                previewBitmap.Unlock();
                newFrame = false;
            }

            var frameSource = bindDeviceInfo?.FrameSource;
            if (frameSource == null)
                return;
            var fps = frameSource.Fps.ToString("F2");
            var frameTime = frameSource.FrameTime.ToString("F2");
            FPSLabel.Content = $"{bindDeviceInfo.SendImageRate:F2}/{fps} FPS";
            FrameTimeLabel.Content = $"Capture: {frameTime}ms";
            SendImageElapsedLabel.Content = $"Send: {bindDeviceInfo.SendImageElapsed:F2}ms";
            var currentOvertimeFlag = SendImageElapsedLabel.Foreground == Brushes.Orange;
            var sendingOvertime = bindDeviceInfo.SendImageElapsed > 1e3 * (currentOvertimeFlag ? 0.9 : 1) /
                bindDeviceInfo.Device.GetScreenInfo().RefreshRate;
            if (sendingOvertime == currentOvertimeFlag) return;
            SendImageElapsedLabel.Foreground = sendingOvertime ? Brushes.Orange : Brushes.DarkGray;
            SendImageElapsedLabel.ToolTip = sendingOvertime ? 
                Properties.Resources.SendImageElapsedLabel_ToolTip + "\n" + 
                Properties.Resources.StreamingPage_UpdatePreview_Can_t_keep_up__Try_to_switch_USB_port_or_higher_pulling_rate_ 
                : Properties.Resources.SendImageElapsedLabel_ToolTip;
        }
        private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            var newSource = SourceCombo.SelectedItem as FrameSource;
            if (newSource == bindDeviceInfo.FrameSource)
                return;
            bindDeviceInfo.FrameSource = newSource;
            previewTimer.Stop();
            if (bindDeviceInfo.FrameSource == null) {
                Preview.Visibility = Visibility.Hidden;   
                return;
            }
            Preview.Visibility = Visibility.Visible;
            previewTimer.Interval = TimeSpan.FromMilliseconds(1e3 / bindDeviceInfo.FrameSource.Fps);
            previewMat.SetTo(Scalar.Black);
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
            StreamButton.Content = bindDeviceInfo.Streaming ? new ImageAwesome { Icon = FontAwesomeIcon.Stop } :
                new ImageAwesome { Icon = FontAwesomeIcon.Play };
            ((ImageAwesome)StreamButton.Content).Foreground = bindDeviceInfo.Streaming ? Brushes.Red : Brushes.Green;
        }

        private void Preview_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) {
            var mousePos = new Point2d(e.GetPosition(Preview).X / 2.0, e.GetPosition(Preview).Y / 2.0);
            var deltaScale = e.Delta > 0 ? 1.1 : 0.9;
            if (!bindDeviceInfo.CanScaleDownSource && e.Delta < 0)
                return;
            if (!bindDeviceInfo.CanScaleUpSource && e.Delta > 0)
                return;

            if (bindDeviceInfo.FrameRect == null)
                return;
            var rect = bindDeviceInfo.FrameRect.Value;

            var cursorVec = new Point2d(mousePos.X - rect.X, mousePos.Y - rect.Y);
            rect.Width *= deltaScale;
            rect.Height *= deltaScale;
            rect.X -= cursorVec.X * (deltaScale - 1.0);
            rect.Y -= cursorVec.Y * (deltaScale - 1.0);
            if (mouseDownFrameRect != null) {
                mouseDownFrameRect = rect;
                mouseDownPose = e.GetPosition(Preview);   
            }
            bindDeviceInfo.FrameRect = rect;
            
            //bindDeviceInfo.PeekFrame();
        }

        private System.Windows.Point? mouseDownPose;
        private RectDouble? mouseDownFrameRect;
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
            var dx = pos.X - mouseDownPose.Value.X;
            var dy = pos.Y - mouseDownPose.Value.Y;
            rect.X += dx / 2.0;
            rect.Y += dy / 2.0;
            bindDeviceInfo.FrameRect = rect;
            //mouseDownPose = pos;
            //bindDeviceInfo.PeekFrame();
        }

        private void ResetPreviewRect_Click(object sender, RoutedEventArgs e) {
            var rect = bindDeviceInfo.GetDefaultRect();
            if (rect != null)
                bindDeviceInfo.FrameRect = rect.Value;
            //bindDeviceInfo.PeekFrame();
            bindDeviceInfo.FrameSource?.ReInit();
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
