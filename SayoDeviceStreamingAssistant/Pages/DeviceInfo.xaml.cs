using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using FontAwesome.WPF;
using OpenCV.Net;
using SayoDeviceStreamingAssistant.Sources;
using static SayoDeviceStreamingAssistant.Sources.FrameSource;
using Rect = OpenCV.Net.Rect;

namespace SayoDeviceStreamingAssistant.Pages {
    public partial class DeviceInfo : IDisposable {
        public string DeviceName {
            get => labelName.Content.ToString();
            private set => labelName.Content = value;
        }
        public string Status {
            get => labelDeviceStatus.Content.ToString();
            set => labelDeviceStatus.Content = value;
        }

        public SayoHidDevice Device { get; set; }
        private FrameSource frameSource;
        public FrameSource FrameSource {
            get => frameSource;
            set {
                if (frameSource != null && onFrameReady != null) {
                    frameSource.RemoveFrameListener(HandleFrame);
                }
                frameSource = value;
                Dispatcher.Invoke(UpdateStatus);
                if (frameSource == null || onFrameReady == null) return;
                frameSource.AddFrameListener(HandleFrame, Device?.GetScreenInfo().RefreshRate ?? 60);
                rectDirty = true;
                //FrameRect = GetDefaultRect();
            }
        }

        private event OnFrameReadyDelegate onFrameReady;
        public event OnFrameReadyDelegate OnFrameReady {
            add {
                if (frameSource != null && onFrameReady == null)
                    frameSource.AddFrameListener(HandleFrame, Device?.GetScreenInfo()?.RefreshRate ?? 60);
                onFrameReady += value;
            }
            remove {
                onFrameReady -= value;
                if (frameSource != null && onFrameReady == null)
                    frameSource.RemoveFrameListener(HandleFrame);
            }
        }
        public bool Streaming {
            get {
                if (frameSource == null || onFrameReady == null)
                    return false;
                return Array.Find(onFrameReady.GetInvocationList(),
                    (i) => i.Equals((OnFrameReadyDelegate)Device.SendImage)) != null;
            }
            set {
                if (frameSource == null || Streaming == value)
                    return;
                if (value) {
                    OnFrameReady += Device.SendImage;
                } else {
                    OnFrameReady -= Device.SendImage;
                }
                Dispatcher.Invoke(UpdateStatus);
            }
        }

        public Mat ScreenMat;
        private readonly Dictionary<Guid,Rect> frameRects = new Dictionary<Guid, Rect>();
        
        
        private bool rectDirty = true;
        public Rect? FrameRect {
            get {
                if(frameSource == null) return null;
                if (!frameRects.ContainsKey(frameSource.Guid)) return null;
                return frameRects[frameSource.Guid];
            }
            set {
                if(value == null) return;
                if (frameSource == null) return;
                frameRects[frameSource.Guid] = value.Value;
                rectDirty = true;
            }
        }

        public Dictionary<Guid, Rect> GetSourceRects() {
            return frameRects;
        }
        public void SetSourceRects(Dictionary<Guid, Rect> rects) {
            frameRects.Clear();
            foreach (var kv in rects) {
                frameRects[kv.Key] = kv.Value;
            }
        }
        
        public DeviceInfo(SayoHidDevice device) {
            InitializeComponent();
            Device = device;

            DeviceName = device.GetProductName();
            UpdateStatus();
            Device.OnDeviceConnectionChanged += (connected) => {
                Dispatcher.Invoke(UpdateStatus);
            };
            SourcesManagePage.FrameSources.CollectionChanged += (sender, e) => {
                if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
                    return;
                foreach (FrameSource source in e.OldItems) {
                    if (source != FrameSource)
                        continue;
                    FrameSource = null;
                    UpdateStatus();
                }
            };
            
        }


        public double SendImageElapsed => Device.ImageSendElapsedMs;
        public double SendImageRate => Device.SendImageRate;

        private void HandleFrame(Mat frame) {
            if (frame == null) return;
            if (FrameRect == null) {
                FrameRect = GetDefaultRect();
                return;
            }
            if (rectDirty) {
                ScreenMat.Set(new Scalar(0, 0, 0));
                rectDirty = false;
            }
            frame.DrawToBgr565(ScreenMat, FrameRect.Value);
            onFrameReady?.Invoke(ScreenMat);
        }
        
        public void PeekFrame() {
            if (frameSource == null) return;
            var frame =frameSource.PeekFrame();
            if (frame == null) return;
            if (FrameRect == null) {
                FrameRect = GetDefaultRect();
                return;
            }
            ScreenMat.Set(new Scalar(0, 0, 0));
            frame.DrawToBgr565(ScreenMat, FrameRect.Value);

            if (onFrameReady == null) return;
            foreach (var cb in onFrameReady.GetInvocationList()) {
                if ((OnFrameReadyDelegate)cb != Device.SendImage)
                    cb.DynamicInvoke(ScreenMat);
            }
        }
        
        public Rect? GetDefaultRect() {
            var srcSize = frameSource?.GetContentRawSize();
            if (srcSize == null || srcSize.Value.Width == 0 || srcSize.Value.Height == 0) return null;
            var dstSize = ScreenMat.Size;
            return MatExtension.GetDefaultRect(srcSize.Value, dstSize);
        }

        public void UpdateStatus() {
            if (!Device.Connected) {
                Status = Properties.Resources.DeviceInfo_UpdateStatus_Disconnected;
                DeviceStatus.Fill = Brushes.Gray;
                //DeviceSelectButton.IsEnabled = false;
                DeviceSelectButton.IsHitTestVisible = false;
                DeviceSelectButton.Background = Brushes.Gray;
                DeviceSelectButton.ToolTip = Properties.Resources.DeviceInfo_UpdateStatus_Device_is_disconnected;
                PlayButton.Visibility = Visibility.Hidden;
                Visibility = Visibility.Visible;
                Visibility = Settings.Instance.ShowUnsupportedDevice || Device.SupportStreaming != false ? 
                    Visibility.Visible : Visibility.Collapsed;
            } else if (Device.SupportStreaming == false) {
                Status = Properties.Resources.DeviceInfo_UpdateStatus_Not_Supported;
                DeviceStatus.Fill = Brushes.Red;
                //DeviceSelectButton.IsEnabled = false;
                DeviceSelectButton.IsHitTestVisible = false;
                DeviceSelectButton.Background = Brushes.Gray;
                DeviceSelectButton.ToolTip = Properties.Resources.DeviceInfo_UpdateStatus_Device_does_not_support_streaming;
                PlayButton.Visibility = Visibility.Hidden;
                Visibility = Settings.Instance.ShowUnsupportedDevice ? Visibility.Visible : Visibility.Collapsed;
            } else if (Device.SupportStreaming == null) {
                Status = Properties.Resources.DeviceInfo_UpdateStatus_Switch_to_8k_pulling_rate_to_enable_streaming;
                DeviceStatus.Fill = Brushes.Orange;
                //DeviceSelectButton.IsEnabled = false;
                DeviceSelectButton.IsHitTestVisible = false;
                DeviceSelectButton.Background = Brushes.Gray;
                DeviceSelectButton.ToolTip = Properties.Resources.DeviceInfo_UpdateStatus_Switch_to_8k_pulling_rate_to_enable_streaming;
                PlayButton.Visibility = Visibility.Hidden;
                Visibility = Visibility.Visible;
            } else if (!Streaming) {
                Status = frameSource == null ? Properties.Resources.DeviceInfo_UpdateStatus_Ready 
                    : string.Format(Properties.Resources.DeviceInfo_UpdateStatus_Paused___0_, frameSource.Name);
                DeviceStatus.Fill = Brushes.Cyan;
                //DeviceSelectButton.IsEnabled = true;
                DeviceSelectButton.IsHitTestVisible = true;
                DeviceSelectButton.Background = Brushes.Transparent;
                PlayButton.Visibility = Visibility.Visible;
                Visibility = Visibility.Visible;
            } else {
                Status = string.Format(Properties.Resources.DeviceInfo_UpdateStatus_Streaming___0_, frameSource.Name);
                DeviceStatus.Fill = Brushes.Green;
                //DeviceSelectButton.IsEnabled = true;
                DeviceSelectButton.IsHitTestVisible = true;
                DeviceSelectButton.Background = Brushes.Transparent;
                PlayButton.Visibility = Visibility.Visible;
                Visibility = Visibility.Visible;
            }
            PlayButton.ToolTip = Streaming ? Properties.Resources.StreamingPage_SetStreamButton_Pause_streaming 
                : Properties.Resources.StreamingPage_SetStreamButton_Begin_streaming;
            PlayButton.Content = Streaming ? new ImageAwesome { Icon = FontAwesomeIcon.Pause } :
                new ImageAwesome { Icon = FontAwesomeIcon.Play };
            ((ImageAwesome)PlayButton.Content).Foreground = Streaming ? Brushes.Red : Brushes.Green;

            if (ScreenMat == null && Device.SupportStreaming == true) {
                var screenInfo = Device.GetScreenInfo();
                if (screenInfo != null) {
                    ScreenMat = new Mat(screenInfo.Height, screenInfo.Width, Depth.U8, 2);
                    var screenInfoStr = $"{screenInfo.Width}x{screenInfo.Height}@{screenInfo.RefreshRate}Hz";
                    labelScreenInfo.Content = screenInfoStr;
                } else
                    labelScreenInfo.Content = "";
            }
        }
        public void Dispose() {
            Device?.Dispose();
        }

        private void DeviceSelectButton_Click(object sender, RoutedEventArgs e) {
            var mainWindow = (MainWindow)System.Windows.Window.GetWindow(this);
            mainWindow?.ShowStreamingPage(this);
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e) {
            Streaming = !Streaming;
        }
    }
}
