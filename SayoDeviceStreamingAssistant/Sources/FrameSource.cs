using Composition.WindowsRuntimeHelpers;
using OpenCV.Net;
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
                ReInit();
            }
        }

        public delegate void OnFrameReadyDelegate(Mat frame);
        //private event OnFrameReadyDelegate OnFrameReady;
        //callback, expected fps, beginSendFrameCount, sendFrameCount
        private readonly Dictionary<OnFrameReadyDelegate, (double, uint, uint)> onFrameListeners 
            = new Dictionary<OnFrameReadyDelegate, (double, uint, uint)>();

        public void AddFrameListener(OnFrameReadyDelegate listener, double expectedFps) {
            onFrameListeners[listener] = (expectedFps, FrameCount, 0);
            if (readFrameTimer.Enabled == false)
                Enabled = true;
            SetFps();
        }
        public void RemoveFrameListener(OnFrameReadyDelegate listener) {
            if (onFrameListeners.Count == 1 && onFrameListeners.ContainsKey(listener))
                Enabled = false;
            onFrameListeners.Remove(listener);
            SetFps();
        }

        private bool Enabled {
            get => onFrameListeners.Count != 0;
            set {
                Console.WriteLine("set timer enabled: " + value);
                if (!value) readFrameTimer.Stop();
                else readFrameTimer.Enabled = true;
                if (capture == null) return;
                if (value) capture.Init();
                else capture.Dispose();

            }
        }
        public double FrameTime { get; private set; }
        public double Fps { get; private set; } = 60;

        private void SetFps() {
            Fps = video?.GetProperty(CaptureProperty.Fps) ?? 
                  (onFrameListeners.Any() ? onFrameListeners.Values.Select((i)=>i.Item1).Max() : 60);
            readFrameTimer.Interval = (long)Math.Round(1e6 / Fps);
        }

        public uint FrameCount { get; private set; }

        private Mat rawFrame = new Mat(10,10, Depth.U8, 4);
        private readonly MicroTimer readFrameTimer = new MicroTimer();

        private CaptureFramework.CaptureFramework capture;
        private Capture video;
        private Func<Func<Mat,bool>,bool> readRawFrame;

        public FrameSource(string name, Guid? guid = null) {
            this.Guid = guid ?? Guid.NewGuid();
            Name = name;
            initTimer = new Timer((state) => Init(), null, 0, 1000);
            readFrameTimer.Interval = (long)Math.Round(1e6 / 60);
            readFrameTimer.MicroTimerElapsed += (o, e) => {
                var sw = Stopwatch.StartNew();
                ReadFrame();
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

        public void ReInit() {
            if (!Initialized) return;
            Dispose();
            initTimer = new Timer((state) => Init(), null, 0, 1000);
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
                    capture = new CaptureFramework.CaptureFramework(monitor.Hmon, CaptureFramework.CaptureFramework.SourceType.Monitor);
                    capture.ItemeDestroyed += Capture_ItemDestroyed;
                    if (Enabled) capture.Init();
                    readRawFrame = capture.ReadFrame;
                    
                    break;
                case 1://"Window"
                    //just grab window from SourcesManager -> -- best way --
                    //not best way, because when a window just closed, it will not be removed instantly from SourcesManager
                    //var wndInfo = SourcesManagePage.GetWindowInfo(Source);
                    //if (wndInfo != null) {
                    //    capture = new CaptureFramework.CaptureFramework(wndInfo.hWnd, CaptureFramework.CaptureFramework.SourceType.Window);
                    //    capture.ItemeDestroyed += Capture_ItemDestroyed;
                    //    if (Enabled) capture.Init();
                    //    readRawFrame = capture.ReadFrame;
                    //    break;
                    //}
                    
                    //ops, try to find window by process name and title,
                    //theatrically this success only if SourcesManager has not been initialized
                    foreach (var wnd in WindowEnumerationHelper.GetWindows()) {
                        if (wnd.Name != Source) continue;
                        capture = new CaptureFramework.CaptureFramework(wnd.hWnd, CaptureFramework.CaptureFramework.SourceType.Window);
                        capture.ItemeDestroyed += Capture_ItemDestroyed;
                        if (Enabled) capture.Init();
                        readRawFrame = capture.ReadFrame;
                        break;
                    }
                    if (readRawFrame != null) break;
                    
                    //ops, try to only match process name
                    var processName = Source.Split(':')[0];
                    var process = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
                    if (process.Length == 0)
                        break;
                    foreach (var p in process) {
                        if (!WindowEnumerationHelper.IsWindowValidForCapture(p.MainWindowHandle)) continue;
                        capture = new CaptureFramework.CaptureFramework(p.MainWindowHandle, CaptureFramework.CaptureFramework.SourceType.Window);
                        capture.ItemeDestroyed += Capture_ItemDestroyed;
                        if (Enabled) capture.Init();
                        readRawFrame = capture.ReadFrame;
                    }
                    break;
                case 2: //"Media"
                    if (File.Exists(Source) == false) break;
                    video = Capture.CreateFileCapture(Source);
                    //video.Open(Source);
                    readRawFrame = (onFrameReady) => {
                        var res = video.GrabFrame();
                        if (!res) return false;
                        onFrameReady(video.RetrieveFrame().GetMat());
                        return true;
                    };
                    break;
            }
            if (readRawFrame != null) {
                initTimer.Dispose();
                initTimer = null;
                SetFps();
                if (Enabled)
                    readFrameTimer.Enabled = true;
            }
            initializing = false;
            sinceInitialized.Restart();
            return true;
        }

        private void Capture_ItemDestroyed() {
            readRawFrame = null;
            readFrameTimer.Enabled = false;
            capture?.Dispose();
            capture = null;
            initTimer = new Timer((state) => Init(), null, 0, 1000);
        }

        public void Dispose() {
            initTimer?.Dispose();
            initTimer = null;
            for (; reading;) Thread.Sleep(1);
            readRawFrame = null;
            readFrameTimer.Enabled = false;
            readFrameTimer?.Stop();
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

            if (video != null && video.GetProperty(CaptureProperty.PosFrames) >=
                video.GetProperty(CaptureProperty.FrameCount))
                video.SetProperty(CaptureProperty.PosFrames, 0);
            
            var res = readRawFrame((mat) => {
                rawFrame = mat;
                foreach (var listener in onFrameListeners.ToArray()) {
                    var onFrame = listener.Key;
                    var (expectedFps, beginFrameCount, sendFrameCount) = listener.Value;
                    var frameElapsedCount = FrameCount - beginFrameCount;
                    var fpsRatio = expectedFps / Fps;
                    if ((double)sendFrameCount / frameElapsedCount > fpsRatio) continue;
                    onFrame(rawFrame);
                    if(onFrameListeners.ContainsKey(listener.Key))
                        onFrameListeners[listener.Key] = (expectedFps, beginFrameCount, sendFrameCount + 1);
                }
                ++FrameCount;
                return true;
            });
            if (res == false) {
                reading = false;
                return false;
            }
            if (FrameCount % 30 == 0)
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
            return new Size((int)video.GetProperty(CaptureProperty.FrameWidth), (int)video.GetProperty(CaptureProperty.FrameHeight));
        }
    }
}
