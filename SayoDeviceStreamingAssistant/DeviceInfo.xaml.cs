using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
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

namespace SayoDeviceStreamingAssistant {
    
    public partial class DeviceInfo : UserControl, IDisposable {
        public string DeviceName {
            get {
                return labelName.Content.ToString();
            }
            set { labelName.Content = value; }
        }
        public string StreamingStatus {
            get {
                return labelSupportStreaming.Content.ToString().Substring(18);
            }
            set { labelSupportStreaming.Content = value; }
        }

        public SayoHidDevice Device { get; set; }

        public bool IsStreaming {
            get {
                return Device?.ScreenStream.Enabled ?? false;
            }
        }

        public DeviceInfo() {
            InitializeComponent();

            var statusTimer = new DispatcherTimer();
            statusTimer.Tick += CheckStatus;
            statusTimer.Interval = TimeSpan.FromMilliseconds(100);
            statusTimer.Start();

        }

        private void CheckStatus(object sender, EventArgs e) {
            if (Device == null || Device.Stream == null) {
                DeviceStatus.Fill = Brushes.Gray;
                return;
            }
            if (IsStreaming) {
                DeviceStatus.Fill = Brushes.Green;
            } else {
                DeviceStatus.Fill = Brushes.Cyan;
            }
        }

        public void Dispose() {
            Device?.Dispose();
        }
    }
}
