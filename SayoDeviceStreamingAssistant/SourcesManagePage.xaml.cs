using Microsoft.Win32;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Window = System.Windows.Window;

namespace SayoDeviceStreamingAssistant {
    /// <summary>
    /// SourcesManagePage.xaml 的交互逻辑
    /// </summary>
    public partial class SourcesManagePage : IDisposable {
        private static readonly List<string> SourceTypes = new List<string> {
            "Monitor",
            "Window",
            "Media",
        };
        public static readonly ObservableCollection<FrameSource> FrameSources = new ObservableCollection<FrameSource>();
        private static readonly ObservableCollection<WindowInfo> Windows = new ObservableCollection<WindowInfo>();
        private static readonly ObservableCollection<MonitorInfo> Monitors = new ObservableCollection<MonitorInfo>();
        private static Timer _contentUpdateTimer;

        private WriteableBitmap previewBitmap;
        private Mat previewMat = new Mat(80, 160, MatType.CV_8UC2);
        private bool newFrame;
        private DispatcherTimer previewTimer = new DispatcherTimer();


        private FrameSource selectedSource;
        private FrameSource SelectedSource {
            get => selectedSource;
            set {
                previewMat.SetTo(new Scalar(0, 0, 0));
                if (selectedSource != null)
                    selectedSource.OnFrameReady -= OnFrameReady;
                selectedSource = value;
                if (selectedSource == null) return;
                SourceName.Text = selectedSource.Name;
                SourceType.SelectedIndex = SourceTypes.IndexOf(selectedSource.Type);
                SetContentUiByType(selectedSource.Type);
                if (selectedSource != null)
                    selectedSource.OnFrameReady += OnFrameReady;
            }
        }
        public SourcesManagePage() {
            InitializeComponent();
            SourceType.ItemsSource = SourceTypes;
            SourcesList.ItemsSource = FrameSources;
            SourceConfigPanel.Visibility = Visibility.Collapsed;

            if (_contentUpdateTimer == null) {
                _contentUpdateTimer = new Timer((state) => {
                    if (Dispatcher.HasShutdownStarted) return;
                    var monitors = MonitorEnumerationHelper.GetMonitors();
                    var monitorInfos = monitors as MonitorInfo[] ?? monitors.ToArray();
                    foreach (var monitor in monitorInfos) {
                        if (Monitors.ToList().Find((m) => m.DeviceName == monitor.DeviceName) == null) {
                            Dispatcher.Invoke(() => Monitors.Add(monitor));
                        }
                    }
                    foreach (var monitor in Monitors.ToArray()) {
                        if (monitorInfos.ToList().Find((m) => m.DeviceName == monitor.DeviceName) == null) {
                            Dispatcher.Invoke(() => Monitors.Remove(monitor));
                        }
                    }
                
                    var windows = WindowEnumerationHelper.GetWindows();
                    foreach (var wnd in windows.Where(wnd => Windows.ToList().Find((p) => p.proc.Id == wnd.proc.Id) == null)) {
                        Dispatcher.Invoke(() => Windows.Add(wnd));
                    }
                    foreach (var wnd in Windows.ToArray()) {
                        if (windows.Find((p) => p.proc.Id == wnd.proc.Id) == null) {
                            Dispatcher.Invoke(() => {
                                var source = selectedSource?.Source;
                                Windows.Remove(wnd);
                                if (source != null)
                                    SourceContentCombo.Text = source;
                            });
                        }
                    }
                }, null, 0, 1000);
            }
            previewTimer.Tick += (sender, e) => {
                UpdatePreview();
            };
            previewTimer.Interval = TimeSpan.FromMilliseconds(1e3 / 60);
            previewTimer.Start();
            previewBitmap = new WriteableBitmap(previewMat.Width, previewMat.Height, 96, 96, PixelFormats.Bgr565, null);
            Preview.Source = previewBitmap;
        }

        public void Dispose() {
            foreach (var source in FrameSources) {
                source.Dispose();
            }
            _contentUpdateTimer.Dispose();
            _contentUpdateTimer = null;
            previewTimer.Stop();
            previewTimer = null;
            previewBitmap = null;
            previewMat.Dispose();
            previewMat = null;
        }

        public void BindSource(FrameSource source) {
            var index = FrameSources.IndexOf(source);
            SourcesList.SelectedIndex = index;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) {
            SourcesList.SelectedIndex = -1;
            //SelectedSource = null;
            var mainWindow = (MainWindow)Window.GetWindow(this);
            mainWindow?.HideSourcesManagePage();
        }

        private void AddNewButton_Click(object sender, RoutedEventArgs e) {
            var newSource = new FrameSource($"New source");
            FrameSources.Add(newSource);
            SourcesList.SelectedIndex = FrameSources.IndexOf(newSource);
        }
        private void DeleteButton_Click(object sender, RoutedEventArgs e) {
            var source = (FrameSource)SourcesList.SelectedItem;
            if (source == null) return;
            var index = FrameSources.IndexOf(source);
            FrameSources.Remove(source);
            if (FrameSources.Count > 0)
                SourcesList.SelectedIndex = Math.Min(index, FrameSources.Count - 1);
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
            var type = SourceType.SelectedItem as string;
            if (SelectedSource == null || type == null) return;
            if (SelectedSource.Type != type)
                SelectedSource.Source = null;
            SelectedSource.Type = type;
            SetContentUiByType(type);
        }

        private void SetContentUiByType(string type) {
            SourceContentCombo.Visibility = Visibility.Collapsed;
            SourceContentText.Visibility = Visibility.Collapsed;
            SelecteFileButton.Visibility = Visibility.Collapsed;
            labelContent.Visibility = Visibility.Collapsed;

            if (!SourceTypes.Contains(type)) return;

            labelContent.Visibility = Visibility.Visible;
            if (type == "Media") {
                SourceContentText.Visibility = Visibility.Visible;
                SelecteFileButton.Visibility = Visibility.Visible;
                labelContent.Content = "Video path";
                SourceContentText.Text = selectedSource.Source;
            } else {
                SourceContentCombo.Visibility = Visibility.Visible;
                labelContent.Content = "Content";
                if (type == "Monitor") {
                    SourceContentCombo.ItemsSource = Monitors;
                    SourceContentCombo.Text = selectedSource?.Source;
                } else if (type == "Window") {
                    SourceContentCombo.ItemsSource = Windows;
                    SourceContentCombo.Text = selectedSource?.Source;
                }
            }
        }

        private void SourceContentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (SourceContentCombo.SelectedItem is MonitorInfo mon)
                selectedSource.Source = mon.Name;
            else if (SourceContentCombo.SelectedItem is WindowInfo win)
                selectedSource.Source = win.Name;
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
                Title = "选择视频文件",
                // 设置文件类型过滤器
                Filter = "视频文件 (*.avi; *.mp4; *.mkv; *.mov; *.wmv; *.flv; *.rmvb)|*.avi;*.mp4;*.mkv;*.mov;*.wmv;*.flv;*.rmvb|所有文件 (*.*)|*.*"
            };

            //openFileDialog.Multiselect = true;
            var result = openFileDialog.ShowDialog();
            if (!result != true) return;
            var selectedFilePath = openFileDialog.FileName;
            SourceContentText.Text = selectedFilePath;
        }

        private void OnFrameReady(Mat frame) {
            frame.DrawTo(previewMat, new OpenCvSharp.Rect(0, 0, previewMat.Width, previewMat.Height));
            newFrame = true;
        }
        private void UpdatePreview() {
            if (previewMat == null || !newFrame) return;
            var len = previewMat.Height * previewMat.Width * 2;
            previewBitmap.Lock();
            WinApi.CopyMemory(previewBitmap.BackBuffer, previewMat.Data, (uint)len);
            previewBitmap.AddDirtyRect(new Int32Rect(0, 0, previewMat.Width, previewMat.Height));
            previewBitmap.Unlock();
            newFrame = false;
        }

    }
}
