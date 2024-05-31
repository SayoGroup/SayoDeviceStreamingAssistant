using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HidSharp;
using OpenCV.Net;

namespace SayoDeviceStreamingAssistant.Sources {
    public class SayoHidPacketBase {
        public const byte ApplicationEcho = 0x04;
        public byte ReportId;
        public ushort Flag;
        public ushort Len;
        //public byte Cmd;
        public byte Index;
    }

    public class ScreenInfoPacket : SayoHidPacketBase {

        public const byte Cmd = 0x2;
        public ushort Width;
        public ushort Height;
        public byte RefreshRate;
        public ushort SysTimeMs;
        public ushort SysTimeSec;
        public ushort Vid;
        public ushort Pid;

        public byte CpuLoad1M;
        public byte CpuLoad5M;

        public uint CpuFreq;
        public uint HclkFreq;
        public uint Pclk1Freq;
        public uint Pclk2Freq;
        public uint Adc0Freq;
        public uint Adc1Freq;

        public static ScreenInfoPacket FromBytes(byte[] data) {
            if (data.Length < 47)
                return null;

            var echo = data[1];
            var cmd = data[6];
            if (cmd != Cmd || echo != ApplicationEcho)
                return null;


            var packet = new ScreenInfoPacket();
            var i = 0;
            packet.ReportId = data[i++];
            _ = data[i++];
            packet.Flag = data.ReadUInt16(ref i);
            packet.Len = data.ReadUInt16(ref i);
            _ = data[i++]; //cmd
            packet.Index = data[i++];
            packet.Width = data.ReadUInt16(ref i);
            packet.Height = data.ReadUInt16(ref i);
            packet.RefreshRate = data[i++];
            packet.SysTimeMs = data.ReadUInt16(ref i);
            packet.SysTimeSec = data.ReadUInt16(ref i);
            packet.Vid = data.ReadUInt16(ref i);
            packet.Pid = data.ReadUInt16(ref i);
            packet.CpuLoad1M = data[i++];
            packet.CpuLoad5M = data[i++];
            packet.CpuFreq = data.ReadUInt32(ref i);
            packet.HclkFreq = data.ReadUInt32(ref i);
            packet.Pclk1Freq = data.ReadUInt32(ref i);
            packet.Pclk2Freq = data.ReadUInt32(ref i);
            packet.Adc0Freq = data.ReadUInt32(ref i);
            packet.Adc1Freq = data.ReadUInt32(ref i);
            return packet;
        }

    }

    public partial class SayoHidDevice : IDisposable { //static
        private static bool _initialized;
        private static bool Init() {
            if (_initialized)
                return true;
            _initialized = true;
            HidSharp.DeviceList.Local.Changed += OnDeviceChanged;
            OnDeviceChanged(null, null);
            return true;
        }
        
        //serial number, device
        private static readonly ConcurrentDictionary<string, SayoHidDevice> DeviceList = new ConcurrentDictionary<string, SayoHidDevice>();
        public static ConcurrentDictionary<string, SayoHidDevice> Devices {
            get {
                Init();
                return DeviceList;
            }
        }
        private static void OnDeviceChanged(object sender, DeviceListChangedEventArgs args) {
            var devices = HidSharp.DeviceList.Local.GetHidDevices(0x8089);
            //serial number, usage, device
            var deviceDict = new Dictionary<string, Dictionary<uint, HidDevice>>();
            foreach (var device in devices) {
                uint? usage = null;
                foreach (var deviceItem in device.GetReportDescriptor().DeviceItems) {
                    foreach (var deviceUsage in deviceItem.Usages.GetAllValues()) {
                        usage = deviceUsage;
                    }
                }
                if (usage == null) continue;
                var serialNumber = device.GetSerialNumber();
                if (!deviceDict.ContainsKey(serialNumber))
                    deviceDict[serialNumber] = new Dictionary<uint, HidDevice>();
                deviceDict[serialNumber][(uint)usage] = device;
                
                // Console.WriteLine($"Product: {device.GetProductName()}");
                // Console.WriteLine($"    Serial: {serialNumber}");
                // Console.WriteLine($"    Usage: {usage:X}");
            }

            foreach (var sayoDevice in Devices) { //remove devices that are not connected
                var serialNumber = sayoDevice.Key;
                foreach (var kv in sayoDevice.Value.devices) {
                    var usage = kv.Key;
                    if (!deviceDict.ContainsKey(serialNumber) || !deviceDict[serialNumber].ContainsKey(usage)) 
                        sayoDevice.Value.RemoveDevice(usage);
                }
            }
            foreach (var kv in deviceDict) { //add new devices
                var serialNumber = kv.Key;
                if (!Devices.ContainsKey(serialNumber)) 
                    Devices[serialNumber] = new SayoHidDevice(serialNumber);
                var sayoDevice = Devices[serialNumber];
                foreach (var kv2 in kv.Value) {
                    var usage = kv2.Key;
                    var device = kv2.Value;
                    sayoDevice.AddDevice(usage, device);
                }
            }
        }
        
        private static void SetHidMessageHeader(IList<byte> buffer, byte echo, ushort flag, byte cmd, byte index, ushort len) {
            len += 4;
            buffer[0] = (byte)(buffer.Count == 64 ? 0x21 : 0x22);
            buffer[1] = echo;
            buffer[2] = (byte)(flag & 0xFF);
            buffer[3] = (byte)(flag >> 8);
            buffer[4] = (byte)(len & 0xFF);
            buffer[5] = (byte)(len >> 8);
            buffer[6] = cmd;
            buffer[7] = index;
        }
    }
    
    public partial class SayoHidDevice {
        public string SerialNumber { get; private set; }
        //usage, device
        private readonly ConcurrentDictionary<uint, HidDevice> devices = new ConcurrentDictionary<uint, HidDevice>();
        private readonly ConcurrentDictionary<uint, HidStream> streams = new ConcurrentDictionary<uint, HidStream>();
        private readonly ConcurrentDictionary<uint, byte[]> buffers = new ConcurrentDictionary<uint, byte[]>();
        private readonly ManualResetEvent canvasDirtyEvent = new ManualResetEvent(false);
        private byte[] canvas;
        public event Action<bool> OnDeviceConnectionChanged;
        bool quit = false;
        public bool Connected => devices.Count > 0;
        public bool? SupportStreaming {
            get {
                var screenInfo = GetScreenInfo();
                var hardwareSupport = screenInfo != null && screenInfo.Width != 0 && screenInfo.Height != 0;
                var softwareSupport = devices.ContainsKey(0xFF020002);
                if(hardwareSupport && softwareSupport) return true;
                if(hardwareSupport) return null;
                return false;
            }
        }
        
        public void Dispose() {
            //stream?.Close();
            quit = true;
        }

        private void AddDevice(uint usage, HidDevice device) {
            devices[usage] = device;
            if (!device.TryOpen(out var stream))
                return;
            streams[usage] = stream;
            var buffer = new byte[device.GetMaxOutputReportLength()];
            buffers[usage] = buffer;
            OnDeviceConnectionChanged?.Invoke(true);
        }
        private void RemoveDevice(uint usage) {
            if (streams.TryGetValue(usage, out var stream)) {
                stream.Close();
                streams.TryRemove(usage,out var _);
            }
            devices.TryRemove(usage, out var _);
            buffers.TryRemove(usage, out var _);
            OnDeviceConnectionChanged?.Invoke(false);
        }
        
        private SayoHidDevice(string serialNumber) {
            SerialNumber = serialNumber;
        }

        public string GetProductName() {
            if(devices.Count == 0) return "Not Connected";
            var device = devices.First().Value;
            if (devices.TryGetValue(0xFF020002, out var streamingDevice)) device = streamingDevice;
            return device.GetProductName();
        }
        
        private Thread imgSendThread;
        private ScreenInfoPacket screenInfoPacket;
        public ScreenInfoPacket GetScreenInfo() {
            if (screenInfoPacket != null)
                return screenInfoPacket;
            if (!devices.ContainsKey(0xFF020002) && !devices.ContainsKey(0xFF010002)) return null;
            var usage = devices.ContainsKey(0xFF020002) ? 0xFF020002 : 0xFF010002;
            try {
                var buffer = buffers[usage];
                var stream = streams[usage];
                SetHidMessageHeader(
                    buffer: buffer,
                    echo: SayoHidPacketBase.ApplicationEcho,
                    flag: 0x7296,
                    cmd: ScreenInfoPacket.Cmd,
                    index: 0,
                    len: 0);
                //ScreenInfoPacket screenInfo;
                stream.Write(buffer, 0, 8);
                var task = Task.Run(() => {
                    var sw = new Stopwatch();
                    sw.Start();
                    while (sw.ElapsedMilliseconds < 1000) {
                        var readBuffer = new byte[1024];
                        var readTask = stream.ReadAsync(readBuffer, 0, 1024);
                        _ = Task.WaitAny(readTask, Task.Delay(1000)) == 1;
                        var screenInfo = ScreenInfoPacket.FromBytes(readBuffer);
                        if (screenInfo != null) {
                            return screenInfo;
                        }
                    }
                    return null;
                });
                task.Wait();
                //Console.WriteLine(task.Result?.RefreshRate.ToString()??"null");
                screenInfoPacket = task.Result;
                //screenInfoPacket.RefreshRate = 180;
                //canvas[usage] = new Mat(screenInfoPacket.Height, screenInfoPacket.Width, Depth.U8, 2);
                canvas = new byte[screenInfoPacket.Height * screenInfoPacket.Width * 2];
                if (imgSendThread == null) {
                    imgSendThread = new Thread(() => {
                        SendImageThreadHandle();
                    }) {
                        IsBackground = true,
                        Priority = ThreadPriority.Lowest,
                    };
                    imgSendThread.Start();
                }
                
                return screenInfoPacket;
            } catch (Exception e){
                //Console.WriteLine(e);
                return null;
            }
        }

        private void SendImageThreadHandle() {
            var sw = new Stopwatch();
            while (!quit) {
                try
                {
                    var usage = devices.ContainsKey(0xFF020002) ? 0xFF020002 : 0xFF010002;
                    canvasDirtyEvent.WaitOne();
                    sw.Restart();
                    SendImage(canvas, usage);
                    ImageSendElapsedMs = sw.Elapsed.TotalMilliseconds;
                    sw.Stop();
                    fpsCounter.Enqueue(DateTime.Now);
                    while (fpsCounter.Count > 30)
                        fpsCounter.Dequeue();
                    SendImageRate = (fpsCounter.Count - 1) / (fpsCounter.Last() - fpsCounter.First()).TotalSeconds;
                }
                catch (Exception e)
                {
                    Thread.Sleep(100);
                    //Console.WriteLine(e);
                }
                canvasDirtyEvent.Reset();
            }
        }
        
        public async void SendImageAsync(Mat image) {
            await Task.Run(() => SendImage(image));
        }
        // public async void SendImageAsync(byte[] rgb565) {
        //     await Task.Run(() => SendImage(rgb565));
        // }

        public double ImageSendElapsedMs { get; private set; }
        public double SendImageRate { get; private set; }
        
        private readonly Queue<DateTime> fpsCounter = new Queue<DateTime>();
        public void SendImage(Mat image) {
            try {
                Marshal.Copy(image.Data, canvas, 0, canvas.Length);
                canvasDirtyEvent.Set();
            } catch {
                //Console.WriteLine(e);
            }
        }

        public void SendImage(byte[] rgb565, uint usage) {
            try {
                var buffer = buffers[usage];
                var stream = streams[usage];
                for (int j = 0; j < rgb565.Length;) {
                    var pixelCount = Math.Min(buffer.Length - 12, rgb565.Length - j);
                    Array.Copy(BitConverter.GetBytes(j), 0, buffer, 8, 4);
                    Array.Copy(rgb565, j, buffer, 12, pixelCount);
                    SetHidMessageHeader(
                        buffer: buffer,
                        echo: SayoHidPacketBase.ApplicationEcho,
                        flag: 0x7296,
                        cmd: 0x25,
                        index: 0,
                        len: (ushort)(pixelCount + 4));
                    stream.Write(buffer, 0, pixelCount + 12);
                    j += pixelCount;
                }
            } catch {
                //Console.WriteLine(e);
            }
        }
    }
}