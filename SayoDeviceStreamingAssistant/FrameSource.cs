using Composition.WindowsRuntimeHelpers;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using MongoDB.Bson;

namespace SayoDeviceStreamingAssistant {
    public class FrameSource : IDisposable, INotifyPropertyChanged {
        public readonly Guid Guid;
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
        private int type = -1;
        public int Type {
            get => type;
            set {
                if (value == type) return;
                type = value;
                OnPropertyChanged(nameof(Type));
                if (!Initialized) return;
                Dispose();
                initTimer = new Timer((state) => Init(), null, 0, 1000);
            }
        }
        private string source = "";
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
        //private event OnFrameReadyDelegate OnFrameReady;
        //callback, expected fps, beginSendFrameCount, sendFrameCount
        private readonly Dictionary<OnFrameReadyDelegate, (double, uint, uint)> onFrameListeners 
            = new Dictionary<OnFrameReadyDelegate, (double, uint, uint)>();

        public void AddFrameListener(OnFrameReadyDelegate listener, double expectedFps) {
            if (onFrameListeners.Count == 0)
                Enabled = true;
            //OnFrameReady += listener;
            onFrameListeners[listener] = (expectedFps, FrameCount, 0);
            SetFps();
        }
        public void RemoveFrameListener(OnFrameReadyDelegate listener) {
            //OnFrameReady -= listener;
            onFrameListeners.Remove(listener);
            if (onFrameListeners.Count == 0)
                Enabled = false;
            SetFps();
        }

        private bool Enabled {
            get => onFrameListeners.Count != 0;
            set {
                readFrameTimer.Enabled = value;
                if (capture == null) return;
                if (value) capture.Init();
                else capture.Dispose();
            }
        }
        public double FrameTime { get; private set; }
        public double Fps { get; private set; } = 60;

        private void SetFps() {
            Fps = video?.Fps ?? 
                  (onFrameListeners.Any() ? onFrameListeners.Values.Select((i)=>i.Item1).Max() : 60);
            readFrameTimer.Interval = (long)Math.Round(1e6 / Fps);
        }

        public uint FrameCount { get; private set; }

        private readonly Mat rawFrame = new Mat();
        private readonly MicroTimer readFrameTimer = new MicroTimer();

        private CaptureFramework.CaptureFramework capture;
        private VideoCapture video;
        private Func<Mat, bool> readRawFrame;

        public FrameSource(string name, Guid? guid = null) {
            this.Guid = guid ?? Guid.NewGuid();
            Name = name;
            initTimer = new Timer((state) => Init(), null, 0, 1000);
            readFrameTimer.Interval = (long)Math.Round(1e6 / 60);
            readFrameTimer.MicroTimerElapsed += (o, e) => {
                var sw = Stopwatch.StartNew();
                if (ReadFrame()) {
                    //OnFrameReady?.Invoke(rawFrame);
                    foreach (var listener in onFrameListeners.ToArray()) {
                        var onFrame = listener.Key;
                        var (expectedFps, beginFrameCount, sendFrameCount) = listener.Value;
                        var frameElapsedCount = FrameCount - beginFrameCount;
                        var fpsRatio = expectedFps / Fps;
                        if ((double)sendFrameCount / frameElapsedCount > fpsRatio) continue;
                        onFrame(rawFrame);
                        onFrameListeners[listener.Key] = (expectedFps, beginFrameCount, sendFrameCount + 1);
                    }
                    ++FrameCount;
                }
                FrameTime = sw.Elapsed.TotalMilliseconds;
                sw.Stop();
            };
        }

        public BsonDocument ToBsonDocument() {
            var json = new {
                Guid = Guid.ToString(),
                Name,
                Type,
                Source
            };
            return json.ToBsonDocument();
        }
        public static FrameSource FromBsonDocument(BsonDocument bson) {
            var guid = Guid.Parse(bson["Guid"].AsString);
            var name = bson["Name"].AsString;
            var type = bson["Type"].AsInt32;
            var source = bson["Source"].AsString;
            return new FrameSource(name, guid) {
                Type = type,
                Source = source
            };
        }
        
        public bool Initialized => initTimer == null;
        private Timer initTimer;
        private bool initializing;
        private readonly Stopwatch sinceInitialized = new Stopwatch();
        private bool Init() {
            if (initializing) return false;
            initializing = true;
            if (readRawFrame != null) return initializing = false;
            if (Type < 0 || Type > 2) return initializing = false;
            if (string.IsNullOrEmpty(Source)) return initializing = false;
            switch (Type) {
                case 0: //"Monitor"
                    var monitors = MonitorEnumerationHelper.GetMonitors();
                    var monitor = monitors.FirstOrDefault(m => m.DeviceName == Source);
                    if (monitor == null)
                        break;

                    var captureItem = CaptureHelper.CreateItemForMonitor(monitor.Hmon);
                    if (captureItem == null)
                        break;
                    capture = new CaptureFramework.CaptureFramework(captureItem);
                    capture.ItemeDestroyed += Capture_ItemeDestroyed;
                    if (Enabled) capture.Init();
                    readRawFrame = capture.ReadFrame;
                    
                    break;
                case 1://"Window"
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
                        capture.ItemeDestroyed += Capture_ItemeDestroyed;
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
                        capture.ItemeDestroyed += Capture_ItemeDestroyed;
                        break;
                    }

                    if (capture == null) return initializing = false;
                    if (Enabled) capture.Init();
                    readRawFrame = capture.ReadFrame;
                    break;
                case 2: //"Media"
                    if (File.Exists(Source) == false) break;
                    video = new VideoCapture(Source);
                    video.Open(Source);
                    readRawFrame = video.Read;
                    break;
            }
            if (readRawFrame != null) {
                initTimer.Dispose();
                initTimer = null;
                if (Enabled) readFrameTimer.StopAndWait();
                SetFps();
                if (Enabled) readFrameTimer.Start();
            }
            initializing = false;
            sinceInitialized.Restart();
            return true;
        }

        private void Capture_ItemeDestroyed() {
            readRawFrame = null;
            readFrameTimer.Enabled = false;
            readFrameTimer?.StopAndWait();
            capture?.Dispose();
            capture = null;
            initTimer = new Timer((state) => Init(), null, 0, 1000);
        }

        public void Dispose() {
            for (; reading;) Thread.Sleep(1);
            readRawFrame = null;
            initTimer?.Dispose();
            readFrameTimer.Enabled = false;
            readFrameTimer?.StopAndWait();
            capture?.Dispose();
            capture = null;
            video?.Dispose();
            video = null;
        }

        private bool reading;
        private bool ReadFrame() {
            if (reading || !Initialized || readRawFrame == null) 
                return false;
            reading = true;
            
            if (video != null && video.PosFrames >= video.FrameCount) 
                video.PosFrames = 0;
            
            if (!readRawFrame(rawFrame)) {
                reading = false;
                return false;
            }
            
            // Cv2.ImShow("frame", rawFrame);
            // Cv2.WaitKey(1);

            //RawFrame.DrawTo(mat, FrameRect);
            if (FrameCount % 60 == 0)
                GC.Collect();
            reading = false;
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
