using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using CaptureFramework;
using MongoDB.Bson;
using OpenCvSharp;

namespace SayoDeviceStreamingAssistant.Sources {
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
                if (!initialized) return;
                Dispose();
                StartInitTimer();
            }
        }

        private string source = "";

        public string Source {
            get => source;
            set {
                if (value == null) value = "";
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
                //Console.WriteLine("set timer enabled: " + value);
                if (!value) readFrameTimer.Stop();
                else readFrameTimer.Enabled = true;
                // if (capture == null) return;
                // if (value) capture.Init();
                // else capture.Dispose();
            }
        }

        public double FrameTime { get; private set; }
        public double Fps { get; private set; } = 60;

        private void SetFps() {
            Fps = video?.Fps ??
                  (onFrameListeners.Any() ? onFrameListeners.Values.Select((i) => i.Item1).Max() : 60);
            if (Fps < 1) Fps = 60;
            readFrameTimer.Interval = (long)Math.Round(1e6 / Fps);
        }

        public uint FrameCount { get; private set; }

        private Mat rawFrame = new Mat(new Size(10, 10), MatType.CV_8UC4); //new Mat(10,10, Depth.U8, 4);
        private readonly MicroTimer readFrameTimer = new MicroTimer();

        //private CaptureFramework.CaptureFramework capture;
        private VideoCapture video;
        private Func<Func<Mat, bool>, bool> readRawFrame;

        public FrameSource(string name, Guid? guid = null) {
            this.Guid = guid ?? Guid.NewGuid();
            Name = name;
            StartInitTimer();
            readFrameTimer.Interval = (long)Math.Round(1e6 / 60);
            readFrameTimer.MicroTimerElapsed += (o, e) => {
                var sw = Stopwatch.StartNew();
                bool success = ReadFrame();
                if (success)
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
            if (!initialized) return;
            Dispose();
            StartInitTimer();
        }

        private void StartInitTimer() {
            Console.WriteLine("StartInitTimer");
            if (initTimer != null) {
                initTimer.Dispose();
                initTimer = null;
            }

            initTimer = new Timer((state) => { initialized = Init(); }, null, 0, 1000);
            Console.WriteLine("StartInitTimer end");
        }

        private bool initialized = false;
        private Timer initTimer;
        private bool initializing;
        private readonly Stopwatch sinceInitialized = new Stopwatch();

        private bool Init() {
            if (initializing) return false;
            initializing = true;
            if (readRawFrame != null) return initializing = false;
            if (Type < 2 || Type > 3) return initializing = false;
            if (string.IsNullOrEmpty(Source)) return initializing = false;

            Console.WriteLine($"Init FrameSource {Name} {Source} {Type}");

            switch (Type) {
                // case 0: //"Monitor"
                //     var monitors = MonitorEnumerationHelper.GetMonitors();
                //     var monitor = monitors.FirstOrDefault(m => m.DeviceName == Source);
                //     if (monitor == null)
                //         break;
                //     capture = new CaptureFramework.CaptureFramework(monitor.Hmon, CaptureFramework.CaptureFramework.SourceType.Monitor);
                //     capture.ItemDestroyed += Capture_ItemDestroyed;
                //     if (Enabled) capture.Init();
                //     readRawFrame = capture.ReadFrame;
                //     
                //     break;
                // case 1://"Window"
                //     //just grab window from SourcesManager -> -- best way --
                //     //not the best way, because when a window just closed, it will not be removed instantly from SourcesManager
                //     //var wndInfo = SourcesManagePage.GetWindowInfo(Source);
                //     //if (wndInfo != null) {
                //     //    capture = new CaptureFramework.CaptureFramework(wndInfo.hWnd, CaptureFramework.CaptureFramework.SourceType.Window);
                //     //    capture.ItemeDestroyed += Capture_ItemDestroyed;
                //     //    if (Enabled) capture.Init();
                //     //    readRawFrame = capture.ReadFrame;
                //     //    break;
                //     //}
                //     
                //     //ops, try to find window by process name and title,
                //     //theatrically this success only if SourcesManager has not been initialized
                //     foreach (var wnd in WindowEnumerationHelper.GetWindows()) {
                //         if (wnd.Name != Source) continue;
                //         capture = new CaptureFramework.CaptureFramework(wnd.hWnd, CaptureFramework.CaptureFramework.SourceType.Window);
                //         capture.ItemDestroyed += Capture_ItemDestroyed;
                //         if (Enabled) capture.Init();
                //         readRawFrame = capture.ReadFrame;
                //         break;
                //     }
                //     if (readRawFrame != null) break;
                //     
                //     //ops, try to only match process name
                //     var processName = Source.Split(':')[0];
                //     var process = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
                //     if (process.Length == 0)
                //         break;
                //     foreach (var p in process) {
                //         if (!WindowEnumerationHelper.IsWindowValidForCapture(p.MainWindowHandle)) continue;
                //         capture = new CaptureFramework.CaptureFramework(p.MainWindowHandle, CaptureFramework.CaptureFramework.SourceType.Window);
                //         capture.ItemDestroyed += Capture_ItemDestroyed;
                //         if (Enabled) capture.Init();
                //         readRawFrame = capture.ReadFrame;
                //     }
                //     break;
                case 2: //"Media"
                    if (File.Exists(Source) == false) break;
                    video = VideoCapture.FromFile(Source);
                    if (video == null) {
                        //System.Windows.MessageBox.Show("Failed to open video file.\nDoesn't support HVC1 yet.");
                        Source = "";
                        break;
                    }

                    //video.Open(Source);
                    readRawFrame = (onFrameReady) => {
                        try {
                            var res = video.Grab();
                            if (!res) return false;
                            onFrameReady(video.RetrieveMat());
                            return true;
                        }
                        catch (Exception e) {
                            Console.WriteLine(e);
                            return false;
                        }
                    };
                    break;
                case 3: //"Camera"
                    CameraHelper.GetCameraDetails(Source, out var index, out var width, out var height, out var fps);
                    if (index == -1) {
                        //System.Windows.MessageBox.Show("Failed to open camera.");
                        Source = "";
                        break;
                    }

                    video = VideoCapture.FromCamera(index);
                    if (video == null) {
                        //System.Windows.MessageBox.Show("Failed to open camera.");
                        Source = "";
                        break;
                    }

                    video.Set(VideoCaptureProperties.FrameHeight, height);
                    video.Set(VideoCaptureProperties.FrameWidth, width);
                    video.Set(VideoCaptureProperties.Fps, fps);
                    Console.WriteLine(video.FrameWidth + "x" + video.FrameHeight + " " + video.Fps + "fps");
                    readRawFrame = (onFrameReady) => {
                        //Console.WriteLine("Read camera frame");
                        try {
                            var res = video.Grab();
                            if (!res) return false;
                            onFrameReady(video.RetrieveMat());
                            return true;
                        }
                        catch (Exception e) {
                            Console.WriteLine(e);
                            return false;
                        }
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
            // capture?.Dispose();
            // capture = null;
            StartInitTimer();
        }

        public void Dispose() {
            Console.WriteLine("Dispose FrameSource");
            initTimer?.Dispose();
            initTimer = null;
            initialized = false;
            for (; reading;) {
                Console.WriteLine("wait for reading... dispose");
                Thread.Sleep(1);
            }

            readRawFrame = null;
            readFrameTimer.Enabled = false;
            readFrameTimer?.Stop();
            // capture?.Dispose();
            // capture = null;
            video?.Dispose();
            video = null;
        }

        private bool reading;

        private bool ReadFrame() {
            if (reading || !initialized || readRawFrame == null)
                return false;
            reading = true;


            if (video != null && video.CaptureType == CaptureType.File && video.PosFrames >=
                video.FrameCount)
                video.PosFrames = 0;

            var res = readRawFrame((mat) => {
                rawFrame = mat;
                foreach (var listener in onFrameListeners.ToArray()) {
                    var onFrame = listener.Key;
                    var (expectedFps, beginFrameCount, sendFrameCount) = listener.Value;
                    var frameElapsedCount = FrameCount - beginFrameCount;
                    var fpsRatio = expectedFps / Fps;
                    if ((double)sendFrameCount / frameElapsedCount > fpsRatio) continue;
                    onFrame(rawFrame);
                    if (onFrameListeners.ContainsKey(listener.Key))
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
            //return capture?.GetSourceSize() ?? GetVideoSize();
            return GetVideoSize();
        }

        private Size? GetVideoSize() {
            if (video == null)
                return null;
            return new Size((int)video.FrameWidth, (int)video.FrameHeight);
        }
    }
}