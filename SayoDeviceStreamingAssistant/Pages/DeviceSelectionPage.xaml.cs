using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using OpenCvSharp;
using SayoDeviceStreamingAssistant.Sources;
using Rect = Windows.Foundation.Rect;

namespace SayoDeviceStreamingAssistant.Pages {
    internal class DeviceConfig {
        public Guid Source;
        //source id, rect
        public Dictionary<Guid, Windows.Foundation.Rect> Rects;
        
        public BsonDocument ToBsonDocument() {
            var bson = new BsonDocument {
                {"Source", Source.ToString()},
                {"Rects", new BsonDocument(
                    Rects.ToDictionary(kv => kv.Key.ToString(), 
                        kv => new BsonDocument {
                            {"X", kv.Value.X},
                            {"Y", kv.Value.Y},
                            {"Width", kv.Value.Width},
                            {"Height", kv.Value.Height}
                        }))}
            };
            return bson;
        }
        public static DeviceConfig FromBsonDocument(BsonDocument doc) {
            return new DeviceConfig {
                Source = Guid.Parse(doc["Source"].AsString),
                Rects = doc["Rects"].AsBsonDocument.ToDictionary(
                    kv => Guid.Parse(kv.Name), 
                    kv => BsonSerializer.Deserialize<Rect>(kv.Value.AsBsonDocument))
            };
        }
    }
    
    /// <summary>
    /// DeviceSelectionPage.xaml 的交互逻辑
    /// </summary>
    public partial class DeviceSelectionPage : IDisposable {
        private const string DeviceRectsFile = "./content/devices.json";
        private readonly List<DeviceInfo> deviceInfos = new List<DeviceInfo>();
        private readonly Dictionary<string, DeviceConfig> devicesSettings = new Dictionary<string, DeviceConfig>();
        
        public DeviceSelectionPage() {
            InitializeComponent();

            if (File.Exists(DeviceRectsFile)) {
                var bson = BsonDocument.Parse(File.ReadAllText(DeviceRectsFile));
                foreach (var kv in bson.ToList()) {
                    var serialNumber = kv.Name;
                    devicesSettings.Add(serialNumber, DeviceConfig.FromBsonDocument(kv.Value.AsBsonDocument));
                }
            }
            
            UpdateDeviceList();
            HidSharp.DeviceList.Local.Changed += (sender, e) => {
                Dispatcher.Invoke(UpdateDeviceList);
            };
        }
        public void UpdateAllDeviceInfos() {
            foreach (var deviceInfo in deviceInfos) {
                deviceInfo.UpdateStatus();
            }
        }

        private void UpdateDeviceList() {
            var devices = SayoHidDevice.Devices;
            foreach (var kv in devices) {
                var serialNumber = kv.Key;
                var device = kv.Value;
                var deviceInfo = deviceInfos.Find(info => info.Device.SerialNumber == serialNumber);
                if (deviceInfo != null) 
                    continue;
                
                deviceInfo = new DeviceInfo(device);

                if (devicesSettings.TryGetValue(serialNumber, out var cfg)) {
                    deviceInfo.SetSourceRects(cfg.Rects);
                    deviceInfo.FrameSource = SourcesManagePage.FrameSources.ToList()
                        .Find(s => s.Guid == cfg.Source);   
                }
                DeviceList.Children.Add(deviceInfo);
                deviceInfos.Add(deviceInfo);
            }
        }
        public void Dispose() {
            foreach (var deviceInfo in deviceInfos) { //retrieve device settings
                var serialNumber = deviceInfo.Device.SerialNumber;
                var rects = deviceInfo.GetSourceRects();
                var source = deviceInfo.FrameSource;
                devicesSettings[serialNumber] = new DeviceConfig {
                    Source = source?.Guid ?? Guid.Empty,
                    Rects = rects
                };
            }
            var bson = new BsonDocument(devicesSettings.ToDictionary(kv => kv.Key,
                kv => kv.Value.ToBsonDocument()));
            if(!Directory.Exists("./content"))
                Directory.CreateDirectory("./content");
            File.WriteAllText(DeviceRectsFile,bson.ToJson());
            foreach (var device in deviceInfos) {
                device.Dispose();
            }
        }

    }
}
