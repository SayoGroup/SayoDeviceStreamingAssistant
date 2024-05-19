using HidSharp;
using System;
using System.Collections.Generic;
using System.Linq;
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

namespace SayoDeviceStreamingAssistant {
    /// <summary>
    /// DeviceSelectionPage.xaml 的交互逻辑
    /// </summary>
    public partial class DeviceSelectionPage : Page, IDisposable {
        private List<DeviceInfo> deviceInfos = new List<DeviceInfo>();
        public DeviceSelectionPage() {
            InitializeComponent();
            UpdateDeviceList();
            HidSharp.DeviceList.Local.Changed += (sender, e) => {
                Dispatcher.Invoke(UpdateDeviceList);
            };
        }

        public void UpdateDeviceList() {
            var devices = SayoHidDevice.Devices;
            foreach (var device in devices) {
                var deviceInfo = deviceInfos.Find(info => info.Device.Device.GetSerialNumber() == device.Device.GetSerialNumber());
                if (deviceInfo == null) {
                    deviceInfo = new DeviceInfo();
                    deviceInfo.Device = device;
                    deviceInfo.DeviceName = device.Device.GetProductName();
                    deviceInfo.StreamingStatus = device.SupportsStreaming ? "Ready" : "Doesn't support streaming";
                    deviceInfo.DeviceSelect.Click += (sender, e) => {
                        var mainWindow = (MainWindow)Window.GetWindow(this);
                        mainWindow.SetStreamingPage(deviceInfo);
                    };
                    DeviceList.Children.Add(deviceInfo);
                    deviceInfos.Add(deviceInfo);
                }
            }
        }
        public void Dispose() {
            foreach (var device in deviceInfos) {
                device.Dispose();
            }
        }

    }
}
