using Microsoft.Win32;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using static System.Net.Mime.MediaTypeNames;
using Window = System.Windows.Window;

namespace SayoDeviceStreamingAssistant {
    /// <summary>
    /// SourcesManagePage.xaml 的交互逻辑
    /// </summary>
    public partial class SourcesManagePage : Page {
        public static List<string> sourceTypes = new List<string> {
            "Monitor",
            "Window",
            "Media",
        };
        public static ObservableCollection<FrameSource> FrameSources = new ObservableCollection<FrameSource>();
        public static ObservableCollection<WindowInfo> Windows = new ObservableCollection<WindowInfo>();
        public static ObservableCollection<MonitorInfo> Monitors = new ObservableCollection<MonitorInfo>();
        private static Timer contentUpdateTimer;

        private WriteableBitmap previewBitmap;
        private Mat previewMat = new Mat(80,160,MatType.CV_8UC2);
        private bool newFrame = false;
        private DispatcherTimer previewTimer = new DispatcherTimer();


        private FrameSource _selectedSource;
        private FrameSource SelectedSource {
            get => _selectedSource;
            set {
                previewMat.SetTo(new Scalar(0, 0, 0));
                if (_selectedSource != null)
                    _selectedSource.OnFrameReady -= OnFrameReady;
                _selectedSource = value;
                if (_selectedSource == null) return;
                SourceName.Text = _selectedSource.Name;
                SourceType.SelectedIndex = sourceTypes.IndexOf(_selectedSource.Type);
                SetContentUIByType(_selectedSource.Type);
                if (_selectedSource != null)
                    _selectedSource.OnFrameReady += OnFrameReady;
            }
        }
        public SourcesManagePage() {
            InitializeComponent();
            SourceType.ItemsSource = sourceTypes;
            SourcesList.ItemsSource = FrameSources;
            SourceConfigPanel.Visibility = Visibility.Collapsed;

            if (contentUpdateTimer != null) return;
            contentUpdateTimer = new Timer((state) => {
                var monitors = MonitorEnumerationHelper.GetMonitors();
                foreach (var monitor in monitors) {
                    if (Monitors.ToList().Find((m) => m.DeviceName == monitor.DeviceName) == null) {
                        Dispatcher.Invoke(() => Monitors.Add(monitor));
                    }
                }
                foreach (var monitor in Monitors.ToArray()) {
                    if (monitors.ToList().Find((m) => m.DeviceName == monitor.DeviceName) == null) {
                        Dispatcher.Invoke(() => Monitors.Remove(monitor));
                    }
                }
                var windows = WindowEnumerationHelper.GetWindows();

                foreach (var wnd in windows) {
                    if (Windows.ToList().Find((p) => p.proc.Id == wnd.proc.Id) == null) {
                        Dispatcher.Invoke(() => Windows.Add(wnd));
                    }
                }
                foreach (var wnd in Windows.ToArray()) {
                    if (windows.Find((p) => p.proc.Id == wnd.proc.Id) == null) {
                        Dispatcher.Invoke(() => {
                            var source = _selectedSource?.Source;
                            Windows.Remove(wnd);
                            if (source != null)
                                SourceContentCombo.Text = source;
                        });
                    }
                }
            }, null, 0, 1000);
            previewTimer.Tick += (sender, e) => {
                UpdatePreview();
            };
            previewTimer.Interval = TimeSpan.FromMilliseconds(1e3 / 60);
            previewTimer.Start();
            previewBitmap = new WriteableBitmap(previewMat.Width, previewMat.Height, 96, 96, PixelFormats.Bgr565, null);
            Preview.Source = previewBitmap;
        }

        public void BindSource(FrameSource source) {
            var index = FrameSources.IndexOf(source);
            SourcesList.SelectedIndex = index;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) {
            SourcesList.SelectedIndex = -1;
            //SelectedSource = null;
            var mainWindow = (MainWindow)Window.GetWindow(this);
            mainWindow.HideSourcesManagePage();
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
            if(SelectedSource.Type != type)
                SelectedSource.Source = null;
            SelectedSource.Type = type;
            SetContentUIByType(type);
        }

        private void SetContentUIByType(string type) {
            SourceContentCombo.Visibility = Visibility.Collapsed;
            SourceContentText.Visibility = Visibility.Collapsed;
            SelecteFileButton.Visibility = Visibility.Collapsed;
            labelContent.Visibility = Visibility.Collapsed;

            if (!sourceTypes.Contains(type)) return;

            labelContent.Visibility = Visibility.Visible;
            if (type == "Media") {
                SourceContentText.Visibility = Visibility.Visible;
                SelecteFileButton.Visibility = Visibility.Visible;
                labelContent.Content = "Video path";
                SourceContentText.Text = _selectedSource.Source;
            } else {
                SourceContentCombo.Visibility = Visibility.Visible;
                labelContent.Content = "Content";
                if (type == "Monitor") {
                    SourceContentCombo.ItemsSource = Monitors;
                    SourceContentCombo.Text = _selectedSource?.Source;
                } else if (type == "Window") {
                    SourceContentCombo.ItemsSource = Windows;
                    SourceContentCombo.Text = _selectedSource?.Source;
                }
            }
        }

        private void SourceContentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if(SourceContentCombo.SelectedItem is MonitorInfo mon)
                _selectedSource.Source = mon.Name;
            else if(SourceContentCombo.SelectedItem is WindowInfo win)
                _selectedSource.Source = win.Name;
        }
        private void SourceContentCombo_TextInput(object sender, TextCompositionEventArgs e) {
            var text = SourceContentCombo.Text + e.Text;
            if (text != SelectedSource.Source)
                SelectedSource.Source = text;
        }

        private void SourceContentText_TextChanged(object sender, TextChangedEventArgs e) {
            var text = SourceContentText.Text;
            if (SelectedSource != null &&text != SelectedSource.Source)
                SelectedSource.Source = text;
        }

        private void SelecteFileButton_Click(object sender, RoutedEventArgs e) {
            var mainWindow = (MainWindow)Window.GetWindow(this);
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = "选择视频文件";

            // 设置文件类型过滤器
            openFileDialog.Filter = "视频文件 (*.avi; *.mp4; *.mkv; *.mov; *.wmv; *.flv; *.rmvb)|*.avi;*.mp4;*.mkv;*.mov;*.wmv;*.flv;*.rmvb|所有文件 (*.*)|*.*";
            //openFileDialog.Multiselect = true;
            bool? result = openFileDialog.ShowDialog();
            if (result == true) {
                var selectedFilePath = openFileDialog.FileName;
                SourceContentText.Text = selectedFilePath;
            }
        }

        private void OnFrameReady(Mat frame) {
            frame.DrawTo(previewMat, new OpenCvSharp.Rect(0, 0, previewMat.Width, previewMat.Height));
            newFrame = true;
        }
        private void UpdatePreview() {
            if (previewMat != null && newFrame != false) {
                var len = previewMat.Height * previewMat.Width * 2;
                previewBitmap.Lock();
                WinAPI.CopyMemory(previewBitmap.BackBuffer, previewMat.Data, (uint)len);
                previewBitmap.AddDirtyRect(new Int32Rect(0, 0, previewMat.Width, previewMat.Height));
                previewBitmap.Unlock();
                newFrame = false;
            }
        }

    }
}
