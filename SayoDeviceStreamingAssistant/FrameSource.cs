using Composition.WindowsRuntimeHelpers;
using MongoDB.Bson;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SayoDeviceStreamingAssistant {
    public class FrameSource: IDisposable, INotifyPropertyChanged {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) {
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
                if (Initialized) {
                    Dispose();
                    initTimer = new Timer((state) => Init(), null, 0, 1000);
                }
            }
        }

        public delegate void OnFrameReadyDelegate(Mat frame);
        private event OnFrameReadyDelegate _onFrameReady;
        public event OnFrameReadyDelegate OnFrameReady {
            add {
                if (_onFrameReady == null)
                    Enabled = true;
                _onFrameReady += value;
            }
            remove {
                _onFrameReady -= value;
                if (_onFrameReady == null)
                    Enabled = false;
            }
        }
        public bool Enabled {
            get => _onFrameReady != null;
            protected set {
                _microTimer.Enabled = value;
                if (capture == null) return;
                if (value) capture.Init();
                else capture.Dispose();
            }
        }
        public double FrameTime { get; private set; }
        private double _fps = 60;
        public double Fps {
            get {
                return _fps;
            }
            set {
                if (video != null)
                    _fps = value < video.Fps ? value : video.Fps;
                _microTimer.Interval = (long)Math.Round(1e6 / value);
            }
        }
        public ulong FrameCount { get; private set; }

        protected Mat RawFrame = new Mat();
        protected readonly MicroTimer _microTimer = new MicroTimer();

        private CaptureFramework capture = null;
        private VideoCapture video = null;
        private Func<Mat,bool> ReadRawFrame;

        public FrameSource(string name) {
            Name = name;
            initTimer = new Timer((state) => Init(), null, 0, 1000);
            _microTimer.Interval = (long)Math.Round(1e6 / 60);
            _microTimer.MicroTimerElapsed += (o, e) => {
                var sw = Stopwatch.StartNew();
                if(ReadFrame())
                    _onFrameReady?.Invoke(RawFrame);
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
        private bool initializing = false;
        private Stopwatch sinceInitialized = new Stopwatch();
        private bool Init() {
            if (initializing) return false;
            initializing = true;
            if (ReadRawFrame != null) return initializing = false;
            if (string.IsNullOrEmpty(Type)) return initializing = false;
            if (string.IsNullOrEmpty(Source)) return initializing = false;
            switch (Type) {
                case "Monitor":
                    var monitors = MonitorEnumerationHelper.GetMonitors();
                    var monitor = monitors.Where(m => m.DeviceName == Source).FirstOrDefault();
                    if (monitor == null) 
                        break;
                    
                    var captureItem = CaptureHelper.CreateItemForMonitor(monitor.Hmon);
                    if (captureItem == null)
                        break;
                    capture = new CaptureFramework(captureItem);
                    if (Enabled) capture.Init();
                    ReadRawFrame = capture.ReadFrame;
                    break;
                case "Window":
                    var processName = Source.Split(':')[0];
                    var windowTitle = Source.Split(':')[1];
                    var process = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
                    if (process.Length == 0) 
                        break;
                    foreach (var p in process) {
                        if (p.MainWindowTitle == windowTitle) {
                            var item = CaptureHelper.CreateItemForWindow(p.MainWindowHandle);
                            if (item == null)
                                continue;
                            capture = new CaptureFramework(item);
                            break;
                        }
                    }
                    if (capture != null) {
                        if (Enabled) capture.Init();
                        ReadRawFrame = capture.ReadFrame;
                        break;
                    }
                    foreach (var p in process) {
                        if (p.MainWindowHandle != IntPtr.Zero) {
                            var item = CaptureHelper.CreateItemForWindow(p.MainWindowHandle);
                            if (item == null)
                                continue;
                            capture = new CaptureFramework(item);
                            break;
                        }
                    }
                    if (capture != null) {
                        if (Enabled) capture.Init();
                        ReadRawFrame = capture.ReadFrame;
                        break;
                    }
                    return initializing = false;
                case "Media":
                    if (File.Exists(Source) == false)
                        break;
                    video = new VideoCapture(Source);
                    video.Open(Source);
                    Fps = Fps < video.Fps ? Fps : video.Fps;
                    ReadRawFrame = video.Read;
                    break;
                default:
                    break;
            }
            if(ReadRawFrame != null) {
                initTimer.Dispose();
                initTimer = null;
                if (Enabled) _microTimer.StopAndWait();
                _microTimer.Interval = (long)Math.Round(1e6 / Fps);
                if (Enabled) _microTimer.Start();
            }
            initializing = false;
            sinceInitialized.Restart();
            return true;
        }

        public void Dispose() {
            ReadRawFrame = null;
            initTimer?.Dispose();
            _microTimer?.StopAndWait();
            capture?.Dispose();
            video?.Dispose();
        }

        protected bool ReadFrame() {
            if (!Initialized || ReadRawFrame == null) return false;

            if(video != null) {
                var t = sinceInitialized.Elapsed.TotalMilliseconds;
                var frameIndex = (int)(t * video.Fps / 1000.0) % video.FrameCount;
                if (frameIndex != video.PosFrames - 1) {
                    video.PosFrames = frameIndex;
                    ReadRawFrame(RawFrame);
                }
            } else {
                if (!ReadRawFrame(RawFrame))
                    return false;
            }

            //RawFrame.DrawTo(mat, FrameRect);
            if(++FrameCount % 60 == 0)
                GC.Collect();
            return true;
        }
        public Mat PeekFrame() {
            return RawFrame;
        }

        public Size? GetContentRawSize() {
            return capture?.GetSourceSize() ?? 
                GetVideoSize() ?? null;
        }
        private Size? GetVideoSize() {
            if(video == null)
                return null;
            return new Size(video.FrameWidth, video.FrameHeight);
        }
    }

    




}
