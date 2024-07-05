using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaptureFramework;
using Microsoft.Win32;
using MongoDB.Bson;
using OpenCvSharp;
using SayoDeviceStreamingAssistant.Sources;
using FrameSource = SayoDeviceStreamingAssistant.Sources.FrameSource;
using Size = OpenCvSharp.Size;

namespace SayoDeviceStreamingAssistant.Pages {
    /// <summary>
    /// SourcesManagePage.xaml 的交互逻辑
    /// </summary>
    public partial class SourcesManagePage : IDisposable {
        private const string SourcesJsonFile = "./content/sources.json";

        private static readonly List<string> SourceTypes = new List<string> {
            //Properties.Resources.SourcesManagePage_SetContentUiByType_Monitor,
            //Properties.Resources.SourcesManagePage_SetContentUiByType_Window,
            Properties.Resources.SourcesManagePage_SourceTypes_Media,
            "Camera"
        };

        public static readonly ObservableCollection<FrameSource> FrameSources = new ObservableCollection<FrameSource>();
        //private static readonly ObservableCollection<WindowInfo> Windows = new ObservableCollection<WindowInfo>();
        //private static readonly ObservableCollection<MonitorInfo> Monitors = new ObservableCollection<MonitorInfo>();
        private static readonly ObservableCollection<CameraInfo> Cameras = new ObservableCollection<CameraInfo>();
        private static Timer _contentUpdateTimer;

        private WriteableBitmap previewBitmap;
        private Mat previewMat = new Mat(new Size(160, 80), MatType.CV_8UC2); //new Mat(80, 160, Depth.U8, 2);
        private bool newFrame;
        private DispatcherTimer previewTimer = new DispatcherTimer();

        public FrameSource selectedSource { get; private set; }

        private FrameSource SelectedSource {
            get {
                ClearPreview();
                return selectedSource;
            }
            set {
                if (selectedSource != null)
                    selectedSource.RemoveFrameListener(OnFrameReady);
                selectedSource = value;
                if (selectedSource == null) return;
                SourceName.Text = selectedSource.Name;
                SourceType.SelectedIndex = selectedSource.Type - 2;
                SetContentUiByType(selectedSource.Type);
                if (selectedSource != null)
                    selectedSource.AddFrameListener(OnFrameReady, 60);
                ClearPreview();
            }
        }

        // public static WindowInfo GetWindowInfo(string source) {
        //     return Windows.ToList().Find((w) => w.Name == source);
        // }

        private string ToJson() {
            var sources = new BsonArray();
            foreach (var source in FrameSources) {
                sources.Add(source.ToBsonDocument());
            }

            return new BsonDocument {
                { "Sources", sources }
            }.ToJson();
        }

        private static void FromJson(string json) {
            var doc = BsonDocument.Parse(json);
            var sources = doc["Sources"].AsBsonArray;
            foreach (var source in sources) {
                if (source["Type"].AsInt32 != 2 && source["Type"].AsInt32 != 3) continue;
                var frameSource = FrameSource.FromBsonDocument(source as BsonDocument);
                FrameSources.Add(frameSource);
            }
        }

        public SourcesManagePage() {
            InitializeComponent();
            if (File.Exists(SourcesJsonFile))
                FromJson(File.ReadAllText(SourcesJsonFile));

            SourceType.ItemsSource = SourceTypes;
            SourcesList.ItemsSource = FrameSources;
            SourceConfigPanel.Visibility = Visibility.Collapsed;

            previewTimer.Tick += (sender, e) => { UpdatePreview(); };
            previewTimer.Interval = TimeSpan.FromMilliseconds(1e3 / 60);
            previewTimer.Start();
            previewBitmap = new WriteableBitmap(previewMat.Cols, previewMat.Rows, 96, 96, PixelFormats.Bgr565, null);
            Preview.Source = previewBitmap;
        }

        private void UpdateContent(object sender) {
            if (Dispatcher.HasShutdownStarted) return;
            // var monitors = MonitorEnumerationHelper.GetMonitors();
            // var monitorInfos = monitors as MonitorInfo[] ?? monitors.ToArray();
            // foreach (var monitor in monitorInfos) {
            //     if (Monitors.ToList().Find((m) => m.DeviceName == monitor.DeviceName) == null) {
            //         Dispatcher.Invoke(() => Monitors.Add(monitor));
            //     }
            // }
            //
            // foreach (var monitor in Monitors.ToArray()) {
            //     if (monitorInfos.ToList().Find((m) => m.DeviceName == monitor.DeviceName) == null) {
            //         Dispatcher.Invoke(() => Monitors.Remove(monitor));
            //     }
            // }
            //
            // var windows = WindowEnumerationHelper.GetWindows();
            // // Console.WriteLine("---------------------------------");
            // // foreach (var window in windows) {
            // //     Console.WriteLine(window.Name);
            // // }
            //
            //
            // foreach (var wnd in windows) {
            //     var old = Windows.ToList().Find((p) => p.hWnd == wnd.hWnd);
            //     if (old != null) {
            //         old.Title = wnd.Title;
            //         old.proc = wnd.proc;
            //     }
            //     else {
            //         Dispatcher.Invoke(() => Windows.Add(wnd));
            //     }
            // }
            //
            // foreach (var wnd in Windows.ToArray()) {
            //     if (windows.Find((p) => p.hWnd == wnd.hWnd) == null) {
            //         Dispatcher.Invoke(() => {
            //             var source = selectedSource?.Source;
            //             Windows.Remove(wnd);
            //             if (source != null)
            //                 SourceContentCombo.Text = source;
            //         });
            //     }
            // }
            
            //get all cameras
            var cameras = CameraHelper.EnumCameras();
            foreach (var camera in cameras) {
                if (Cameras.ToList().Find((c) => c.DeviceName == camera.DeviceName) == null) {
                    Dispatcher.Invoke(() => Cameras.Add(camera));
                }
            }

            foreach (var camera in Cameras.ToArray()) {
                if (cameras.ToList().Find((c) => c.DeviceName == camera.DeviceName) == null) {
                    Dispatcher.Invoke(() => Cameras.Remove(camera));
                }
            }
            
            
            
        }

        public void Dispose() {
            if (!Directory.Exists("./content"))
                Directory.CreateDirectory("./content");
            File.WriteAllText(SourcesJsonFile, ToJson());
            foreach (var source in FrameSources) {
                source.Dispose();
            }

            _contentUpdateTimer?.Dispose();
            _contentUpdateTimer = null;
            previewTimer.Stop();
            previewTimer = null;
            previewBitmap = null;
            previewMat.Dispose();
            previewMat = null;
        }

        public void ShowPage(FrameSource source) {
            var index = FrameSources.IndexOf(source);
            SourcesList.SelectedIndex = index;
            _contentUpdateTimer = new Timer(UpdateContent, null, 0, 1000);
        }

        public void HidePage() {
            SourcesList.SelectedIndex = -1;
            _contentUpdateTimer?.Dispose();
        }

        private void AddNewButton_Click(object sender, RoutedEventArgs e) {
            var newSource = new FrameSource(string.Format(
                Properties.Resources.SourcesManagePage_AddNewButton_Click_Source__0_, FrameSources.Count));
            newSource.Type = 2;
            FrameSources.Add(newSource);
            SourcesList.SelectedIndex = FrameSources.IndexOf(newSource);
            SourceType.SelectedIndex = 2;
            SourceContentCombo.SelectedIndex = 0;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e) {
            var source = (FrameSource)SourcesList.SelectedItem;
            if (source == null) return;
            var index = FrameSources.IndexOf(source);
            FrameSources.Remove(source);
            if (FrameSources.Count > 0)
                SourcesList.SelectedIndex = Math.Min(index, FrameSources.Count - 1);
            source.Dispose();
        }

        private void SourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            SelectedSource = null;
            var source = (FrameSource)SourcesList.SelectedItem;
            if (source == null) {
                SourceConfigPanel.Visibility = Visibility.Collapsed;
                return;
            }
            SourceConfigPanel.Visibility = Visibility.Visible;
            SelectedSource = source;
        }

        private void SourceName_TextChanged(object sender, TextChangedEventArgs e) {
            if (SelectedSource == null) return;
            SelectedSource.Name = SourceName.Text;
        }

        private void SourceType_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            var type = SourceType.SelectedIndex + 2;
            if (SelectedSource == null) return;
            if (SelectedSource.Type != type)
                SelectedSource.Source = null;
            SelectedSource.Type = type;
            SetContentUiByType(type);
        }

        private void SetContentUiByType(int type) {
            SourceContentCombo.Visibility = Visibility.Collapsed;
            SourceContentText.Visibility = Visibility.Collapsed;
            SelecteFileButton.Visibility = Visibility.Collapsed;
            labelContent.Visibility = Visibility.Collapsed;

            if (type < 2 || type > 3) return;

            labelContent.Visibility = Visibility.Visible;
            switch (type) {
                // case 0: //"Monitor"
                //     SourceContentCombo.Visibility = Visibility.Visible;
                //     labelContent.Content = Properties.Resources.SourcesManagePage_SetContentUiByType_Monitor;
                //     SourceContentCombo.ItemsSource = Monitors;
                //     SourceContentCombo.Text = selectedSource?.Source;
                //     break;
                // case 1: //"Window"
                //     SourceContentCombo.Visibility = Visibility.Visible;
                //     labelContent.Content = Properties.Resources.SourcesManagePage_SetContentUiByType_Window;
                //     SourceContentCombo.ItemsSource = Windows;
                //     SourceContentCombo.Text = selectedSource?.Source;
                //     break;
                case 2: //"Media"
                    SourceContentText.Visibility = Visibility.Visible;
                    SelecteFileButton.Visibility = Visibility.Visible;
                    labelContent.Content = Properties.Resources.SourcesManagePage_SetContentUiByType_Video_path;
                    SourceContentText.Text = selectedSource.Source;
                    break;
                case 3: //"Camera"
                    SourceContentCombo.Visibility = Visibility.Visible;
                    labelContent.Content = "Camera";
                    SourceContentCombo.ItemsSource = Cameras;
                    SourceContentCombo.Text = selectedSource?.Source;
                    break;
            }
        }

        private void SourceContentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            // if (SourceContentCombo.SelectedItem is MonitorInfo mon)
            //     selectedSource.Source = mon.Name;
            // else if (SourceContentCombo.SelectedItem is WindowInfo win)
            //     selectedSource.Source = win.Name;
            // else
            if (SourceContentCombo.SelectedItem is CameraInfo cam)
                selectedSource.Source = cam.Name;
        }

        private void SourceContentCombo_TextInput(object sender, TextCompositionEventArgs e) {
            var text = SourceContentCombo.Text + e.Text;
            if (text != SelectedSource.Source)
                SelectedSource.Source = text;
        }

        private void SourceContentText_TextChanged(object sender, TextChangedEventArgs e) {
            var text = SourceContentText.Text;
            if (SelectedSource != null && text != SelectedSource.Source)
                SelectedSource.Source = text;
        }

        private void SelectFileButton_Click(object sender, RoutedEventArgs e) {
            var openFileDialog = new OpenFileDialog {
                Title = Properties.Resources.SourcesManagePage_SelectFileButton_Click_Select_video_file,
                // 设置文件类型过滤器
                Filter =
                    "Video (*.avi; *.mp4; *.mkv; *.mov; *.wmv; *.flv; *.rmvb)|*.avi;*.mp4;*.mkv;*.mov;*.wmv;*.flv;*.rmvb|All (*.*)|*.*"
            };

            //openFileDialog.Multiselect = true;
            var result = openFileDialog.ShowDialog();
            if (result != true) return;
            var selectedFilePath = openFileDialog.FileName;
            SourceContentText.Text = selectedFilePath;
        }

        private void ClearPreview() {
            previewMat.SetTo(Scalar.Black);
            newFrame = true;
        }

        private void OnFrameReady(Mat frame) {
            // Cv2.ImShow("frame", frame);
            // Cv2.WaitKey(1);
            frame.DrawToBgr565(previewMat, MatExtension.GetDefaultRect(frame.Size(), previewMat.Size()));
            newFrame = true;
        }

        DateTime lastUpdate = DateTime.Now;

        private void UpdatePreview() {
            if (previewMat == null || previewBitmap == null) return;
            if (!newFrame) {
                if ((DateTime.Now - lastUpdate).TotalSeconds > 0.5)
                    ClearPreview();
                return;
            }
            // Cv2.ImShow("preview", previewMat);
            // Cv2.WaitKey(1);
            var len = previewMat.Rows * previewMat.Cols * 2;
            previewBitmap.Lock();
            WinApi.CopyMemory(previewBitmap.BackBuffer, previewMat.Data, (uint)len);
            previewBitmap.AddDirtyRect(new Int32Rect(0, 0, previewMat.Cols, previewMat.Rows));
            previewBitmap.Unlock();
            newFrame = false;
            lastUpdate = DateTime.Now;
        }
    }
}