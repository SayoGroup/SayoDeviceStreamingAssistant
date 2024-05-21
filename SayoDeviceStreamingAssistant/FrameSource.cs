using Composition.WindowsRuntimeHelpers;
using MongoDB.Bson;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SayoDeviceStreamingAssistant {
    public class FrameSource: IDisposable {
        public string Name;
        public readonly string Type;
        public readonly string Source;

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
        public double Fps { get; private set; }
        public ulong FrameCount { get; private set; }

        protected Mat RawFrame = new Mat();
        protected readonly MicroTimer _microTimer = new MicroTimer();

        private CaptureFramework capture = null;
        private VideoCapture video = null;
        private Func<Mat,bool> ReadRawFrame;

        public FrameSource(string name, string type, string source, double expectedFps, Rect2f? rect = null) {
            Name = name;
            Type = type;
            Source = source;
            Fps = expectedFps;
            _microTimer.Interval = (long)Math.Round(1000.0 / Fps);
            _microTimer.MicroTimerElapsed += (o, e) => {
                var sw = Stopwatch.StartNew();
                ReadFrame();
                FrameTime = sw.Elapsed.TotalMilliseconds;
                sw.Stop();
                _onFrameReady?.Invoke(RawFrame);
            };
            initTimer = new Timer((state) => Init(), null, 0, 1000);
        }

        public bool initialized => initTimer == null;
        private Timer initTimer;
        private void Init() {
            switch (Type) {
                case "Monitor":
                    var monitors = MonitorEnumerationHelper.GetMonitors();
                    var monitor = monitors.Where(m => m.DeviceName == Source).FirstOrDefault();
                    if (monitor == null) 
                        return;
                    
                    var captureItem = CaptureHelper.CreateItemForMonitor(monitor.Hmon);
                    capture = new CaptureFramework(captureItem);
                    if (Enabled) capture.Init();
                    ReadRawFrame = capture.ReadFrame;
                    break;
                case "Window":
                    var processName = Source.Split(':')[0];
                    var windowTitle = Source.Split(':')[1];
                    var process = Process.GetProcessesByName(processName);
                    if (process.Length == 0) 
                        return;
                    foreach (var p in process) {
                        if (p.MainWindowTitle == windowTitle) {
                            capture = new CaptureFramework(CaptureHelper.CreateItemForWindow(p.MainWindowHandle));
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
                            capture = new CaptureFramework(CaptureHelper.CreateItemForWindow(p.MainWindowHandle));
                            break;
                        }
                    }
                    if (capture != null) {
                        if (Enabled) capture.Init();
                        ReadRawFrame = capture.ReadFrame;
                        break;
                    }
                    return;
                case "Media":
                    if (File.Exists(Source) == false) 
                        return;
                    video = new VideoCapture(Source);
                    video.Open(Source);
                    Fps = Fps < video.Fps ? Fps : video.Fps;
                    ReadRawFrame = video.Read;
                    break;
            }
            initTimer.Dispose();
            initTimer = null;
            if (Enabled) _microTimer.Stop();
            _microTimer.Interval = (long)Math.Round(1000.0 / Fps);
            if (Enabled) _microTimer.Start();
        }

        public void Dispose() {
            initTimer?.Dispose();
            _microTimer?.StopAndWait();
            capture?.Dispose();
            video?.Dispose();
            RawFrame?.Dispose();
        }

        protected void ReadFrame() {
            if (!initialized) return;
            ReadRawFrame(RawFrame);
            //RawFrame.DrawTo(mat, FrameRect);
            if(++FrameCount % 60 == 0)
                GC.Collect();
        }
        public Mat PeekFrame() {
            return RawFrame;
        }

        public Size GetContentRawSize() {
            return capture?.GetSourceSize() ?? new Size(video.FrameWidth, video.FrameHeight);
        }
    }

    




}
