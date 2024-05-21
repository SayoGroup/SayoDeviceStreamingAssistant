using OpenCvSharp;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using static SayoDeviceStreamingAssistant.FrameSource;
using Rect = OpenCvSharp.Rect;

namespace SayoDeviceStreamingAssistant {
    
    public partial class DeviceInfo : UserControl, IDisposable {
        public string DeviceName {
            get => labelName.Content.ToString();
            private set => labelName.Content = value;
        }
        public string Status {
            get => labelDeviceStatus.Content.ToString();
            set => labelDeviceStatus.Content = value;
        }

        public SayoHidDevice Device { get; set; }
        private FrameSource _frameSource;
        public FrameSource FrameSource { 
            get => _frameSource;
            set {
                if (_frameSource != null && _onFrameReady != null) {
                    _frameSource.OnFrameReady -= HandleFrame;
                }
                _frameSource = value;
                if (_frameSource != null && _onFrameReady != null) {
                    _frameSource.OnFrameReady += HandleFrame;
                    FrameRect = GetDefaultRect();
                }
            }
        }
        
        private event OnFrameReadyDelegate _onFrameReady;
        public event OnFrameReadyDelegate OnFrameReady {
            add {
                if (_frameSource != null && _onFrameReady == null)
                    _frameSource.OnFrameReady += HandleFrame;
                _onFrameReady += value;
            }
            remove {
                _onFrameReady -= value;
                if (_frameSource != null && _onFrameReady == null)
                    _frameSource.OnFrameReady -= HandleFrame;
            }
        }
        public bool Streaming {
            get {
                if (_frameSource == null || _onFrameReady == null)
                    return false;
                return Array.Find(_onFrameReady.GetInvocationList(), 
                    (i) => i.Equals((OnFrameReadyDelegate)Device.SendImageAsync)) != null;
            }
            set {
                if (_frameSource == null || Streaming == value)
                    return;
                if (value) {
                    OnFrameReady += Device.SendImageAsync;
                } else {
                    OnFrameReady -= Device.SendImageAsync;
                }
            }
        }

        public Mat ScreenMat;
        public Rect? FrameRect;

        public DeviceInfo(SayoHidDevice device) {
            InitializeComponent();
            Device = device;
            DeviceName = device.Device.GetProductName();
            UpdateStatus();
            Device.OnDeviceConnectionChanged += (connected) => {
                Dispatcher.Invoke(() => {
                    UpdateStatus();
                });
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
            var screenInfo = Device.ScreenInfo;
            ScreenMat = new Mat(screenInfo.Height, screenInfo.Width, MatType.CV_8UC2);
        }
        public void HandleFrame(Mat frame) {
            if (frame == null) return;
            if(FrameRect == null) {
                FrameRect = GetDefaultRect();
                return;
            }
            frame.DrawTo(ScreenMat, FrameRect.Value);
            _onFrameReady?.Invoke(ScreenMat);
        }
        public Rect? GetDefaultRect() {
            if (_frameSource == null)
                return null;
            var srcSize = _frameSource.GetContentRawSize();
            if (srcSize == null) return null;
            var dstSize = ScreenMat.Size();
            Rect rect;
            var ratio = (double)srcSize.Value.Width / srcSize.Value.Height;
            if (ratio > 2) {
                var space = dstSize.Height - dstSize.Width / ratio;
                rect = new Rect(0, (int)Math.Round(space / 2), dstSize.Width,
                    (int)Math.Round(dstSize.Width / ratio));
            } else {
                var space = dstSize.Width - dstSize.Height * ratio;
                rect = new Rect((int)Math.Round(space / 2), 0,
                    (int)Math.Round(dstSize.Height * ratio), dstSize.Height);
            }
            return rect;
        }
        public void UpdateStatus() {
            string status;
            if (!Device.IsConnected) {
                status = "Disconnected";
                DeviceStatus.Fill = Brushes.Gray;
                DeviceSelectButton.IsEnabled = false;
                DeviceSelectButton.ToolTip = "Device is disconnected";
            }
            else if (!Device.SupportsStreaming) {
                status = "Not Supported";
                DeviceStatus.Fill = Brushes.Red;
                DeviceSelectButton.IsEnabled = false;
                DeviceSelectButton.ToolTip = "Device does not support streaming";
            }
            else if (!Streaming || _frameSource == null) {
                status = "Ready";
                DeviceStatus.Fill = Brushes.Cyan;
                DeviceSelectButton.IsEnabled = true;
            }
            else {
                status = _frameSource.Name;
                DeviceStatus.Fill = Brushes.Green;
                DeviceSelectButton.IsEnabled = true;
            }
            Status = status;
        }
        public void Dispose() {
            Device?.Dispose();
        }

        private void DeviceSelectButton_Click(object sender, RoutedEventArgs e) {
            var mainWindow = (MainWindow)System.Windows.Window.GetWindow(this);
            mainWindow.ShowStreamingPage(this);
        }
    }
}
