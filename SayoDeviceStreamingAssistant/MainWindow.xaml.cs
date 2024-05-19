using Composition.WindowsRuntimeHelpers;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.UI.Xaml.Controls;
using Window = System.Windows.Window;
using Size = OpenCvSharp.Size;
using System.Windows.Media.Animation;

namespace SayoDeviceStreamingAssistant {
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window {
        private DeviceSelectionPage deviceSelectionPage = new DeviceSelectionPage();
        private StreamingPage streamingPage = new StreamingPage();

        public class Media {
            public string MainWindowTitle { get; set; }
        }

        public ObservableCollection<Media> medias = new ObservableCollection<Media>();

        public MainWindow() {
            InitializeComponent();
            deviceSelect.Navigate(deviceSelectionPage);
            streaming.Navigate(streamingPage);
            this.Closing += (sender, e) => {
                deviceSelectionPage.Dispose();
            };
        }

        public void AddMedia(string title) {
            medias.Add(new Media { MainWindowTitle = title });
        }
        public void SetStreamingPage(DeviceInfo device) {
            streamingPage.SetDevice(device);
            ToggleFrameVisibility();
        }

        private bool isFrameVisible = false;
        public void ToggleFrameVisibility() {
            ThicknessAnimation animation = new ThicknessAnimation();
            animation.Duration = TimeSpan.FromSeconds(0.2);

            if (isFrameVisible) {
                animation.To = new Thickness(-streaming.ActualWidth * 2, 0, 0, 0);
                isFrameVisible = false;
            } else {
                animation.To = new Thickness(0, 0, 0, 0);
                isFrameVisible = true;
            }

            streaming.BeginAnimation(MarginProperty, animation);
        }

    }
}
