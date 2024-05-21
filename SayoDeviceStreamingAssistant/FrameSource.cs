using Composition.WindowsRuntimeHelpers;
using OpenCvSharp;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace SayoDeviceStreamingAssistant {
    public class FrameSource : IDisposable, INotifyPropertyChanged {
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string name;
        public string Name {
            get => name;
            set {
                if (value == name) return;
                name = value;
                OnPropertyChanged(nameof(Name));
            }
        }
        private string type;
        public string Type {
            get => type;
            set {
                if (value == type) return;
                type = value;
                OnPropertyChanged(nameof(Type));
                if (Initialized) {
                    Dispose();
                    initTimer = new Timer((state) => Init(), null, 0, 1000);
                }
            }
        }
        private string source;
        public string Source {
            get => source;
            set {
                if (value == source) return;
                source = value;
                OnPropertyChanged(nameof(Source));
                if (!Initialized) return;
                Dispose();
                initTimer = new Timer((state) => Init(), null, 0, 1000);
            }
        }

        public delegate void OnFrameReadyDelegate(Mat frame);
        private event OnFrameReadyDelegate onFrameReady;
        public event OnFrameReadyDelegate OnFrameReady {
            add {
                if (onFrameReady == null)
                    Enabled = true;
                onFrameReady += value;
            }
            remove {
                onFrameReady -= value;
                if (onFrameReady == null)
                    Enabled = false;
            }
        }

        private bool Enabled {
            get => onFrameReady != null;
            set {
                readFrameTimer.Enabled = value;
                if (capture == null) return;
                if (value) capture.Init();
                else capture.Dispose();
            }
        }
        public double FrameTime { get; private set; }
        private double fps = 60;
        public double Fps {
            get => fps;
            set {
                if (video != null)
                    fps = value < video.Fps ? value : video.Fps;
                readFrameTimer.Interval = (long)Math.Round(1e6 / value);
            }
        }
        public ulong FrameCount { get; private set; }

        private readonly Mat rawFrame = new Mat();
        private readonly MicroTimer readFrameTimer = new MicroTimer();

        private CaptureFramework.CaptureFramework capture;
        private VideoCapture video;
        private Func<Mat, bool> readRawFrame;

        public FrameSource(string name) {
            Name = name;
            initTimer = new Timer((state) => Init(), null, 0, 1000);
            readFrameTimer.Interval = (long)Math.Round(1e6 / 60);
            readFrameTimer.MicroTimerElapsed += (o, e) => {
                var sw = Stopwatch.StartNew();
                if (ReadFrame())
                    onFrameReady?.Invoke(rawFrame);
                FrameTime = sw.Elapsed.TotalMilliseconds;
                sw.Stop();
            };
        }
        //public void SetSource(string type, string source, double expectedFps, Rect2f? rect = null) {
        //    Type = type;
        //    Source = source;
        //    Fps = expectedFps;
        //    _microTimer.Interval = (long)Math.Round(1000.0 / Fps);
        //    _microTimer.MicroTimerElapsed += (o, e) => {
        //        var sw = Stopwatch.StartNew();
        //        ReadFrame();
        //        FrameTime = sw.Elapsed.TotalMilliseconds;
        //        sw.Stop();
        //        _onFrameReady?.Invoke(RawFrame);
        //    };
        //    initTimer = new Timer((state) => Init(), null, 0, 1000);
        //}
        public bool Initialized => initTimer == null;
        private Timer initTimer;
        private bool initializing;
        private readonly Stopwatch sinceInitialized = new Stopwatch();
        private bool Init() {
            if (initializing) return false;
            initializing = true;
            if (readRawFrame != null) return initializing = false;
            if (string.IsNullOrEmpty(Type)) return initializing = false;
            if (string.IsNullOrEmpty(Source)) return initializing = false;
            switch (Type) {
                case "Monitor":
                    var monitors = MonitorEnumerationHelper.GetMonitors();
                    var monitor = monitors.FirstOrDefault(m => m.DeviceName == Source);
                    if (monitor == null)
                        break;

                    var captureItem = CaptureHelper.CreateItemForMonitor(monitor.Hmon);
                    if (captureItem == null)
                        break;
                    capture = new CaptureFramework.CaptureFramework(captureItem);
                    if (Enabled) capture.Init();
                    readRawFrame = capture.ReadFrame;
                    break;
                case "Window":
                    var processName = Source.Split(':')[0];
                    var windowTitle = Source.Split(':')[1];
                    var process = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
                    if (process.Length == 0)
                        break;
                    foreach (var p in process) {
                        if (p.MainWindowTitle != windowTitle) continue;
                        var item = CaptureHelper.CreateItemForWindow(p.MainWindowHandle);
                        if (item == null) continue;
                        capture = new CaptureFramework.CaptureFramework(item);
                        break;
                    }
                    if (capture != null) {
                        if (Enabled) capture.Init();
                        readRawFrame = capture.ReadFrame;
                        break;
                    }
                    foreach (var p in process) {
                        if (p.MainWindowHandle == IntPtr.Zero) continue;
                        var item = CaptureHelper.CreateItemForWindow(p.MainWindowHandle);
                        if (item == null) continue;
                        capture = new CaptureFramework.CaptureFramework(item);
                        break;
                    }

                    if (capture == null) return initializing = false;
                    if (Enabled) capture.Init();
                    readRawFrame = capture.ReadFrame;
                    break;
                case "Media":
                    if (File.Exists(Source) == false) break;
                    video = new VideoCapture(Source);
                    video.Open(Source);
                    Fps = Fps < video.Fps ? Fps : video.Fps;
                    readRawFrame = video.Read;
                    break;
            }
            if (readRawFrame != null) {
                initTimer.Dispose();
                initTimer = null;
                if (Enabled) readFrameTimer.StopAndWait();
                readFrameTimer.Interval = (long)Math.Round(1e6 / Fps);
                if (Enabled) readFrameTimer.Start();
            }
            initializing = false;
            sinceInitialized.Restart();
            return true;
        }

        public void Dispose() {
            readRawFrame = null;
            initTimer?.Dispose();
            readFrameTimer.Enabled = false;
            readFrameTimer?.StopAndWait();
            readFrameTimer?.Abort();
            capture?.Dispose();
            video?.Dispose();
        }

        private bool ReadFrame() {
            if (!Initialized || readRawFrame == null) return false;

            if (video != null) {
                var t = sinceInitialized.Elapsed.TotalMilliseconds;
                var frameIndex = (int)(t * video.Fps / 1000.0) % video.FrameCount;
                if (frameIndex != video.PosFrames - 1) {
                    video.PosFrames = frameIndex;
                    readRawFrame(rawFrame);
                }
            } else {
                if (!readRawFrame(rawFrame))
                    return false;
            }

            //RawFrame.DrawTo(mat, FrameRect);
            if (++FrameCount % 60 == 0)
                GC.Collect();
            return true;
        }
        public Mat PeekFrame() {
            return rawFrame;
        }

        public Size? GetContentRawSize() {
            return capture?.GetSourceSize() ?? GetVideoSize();
        }
        private Size? GetVideoSize() {
            if (video == null)
                return null;
            return new Size(video.FrameWidth, video.FrameHeight);
        }
    }






}
