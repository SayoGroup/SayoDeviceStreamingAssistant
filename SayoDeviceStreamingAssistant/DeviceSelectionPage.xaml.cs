using System;
using System.Collections.Generic;

namespace SayoDeviceStreamingAssistant {
    /// <summary>
    /// DeviceSelectionPage.xaml 的交互逻辑
    /// </summary>
    public partial class DeviceSelectionPage : IDisposable {
        private readonly List<DeviceInfo> deviceInfos = new List<DeviceInfo>();
        public DeviceSelectionPage() {
            InitializeComponent();
            UpdateDeviceList();
            HidSharp.DeviceList.Local.Changed += (sender, e) => {
                Dispatcher.Invoke(UpdateDeviceList);
            };
        }

        private void UpdateDeviceList() {
            var devices = SayoHidDevice.Devices;
            foreach (var device in devices) {
                var deviceInfo = deviceInfos.Find(info => info.Device.Device.GetSerialNumber() == device.Device.GetSerialNumber());
                if (deviceInfo != null) continue;
                deviceInfo = new DeviceInfo(device);
                DeviceList.Children.Add(deviceInfo);
                deviceInfos.Add(deviceInfo);
            }
        }
        public void Dispose() {
            foreach (var device in deviceInfos) {
                device.Dispose();
            }
        }

    }
}
