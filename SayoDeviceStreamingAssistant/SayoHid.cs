using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using HidSharp;
using OpenCvSharp;

namespace SayoDeviceStreamingAssistant {
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

    public class SayoHidDevice : IDisposable {
        public readonly HidDevice Device;
        private HidStream stream;
        private readonly byte[] buffer;

        private ScreenInfoPacket screenInfo;
        public ScreenInfoPacket ScreenInfo => screenInfo ?? (screenInfo = GetScreenInfo());
        public bool IsConnected => stream != null;

        public bool SupportsStreaming => ScreenInfo != null && ScreenInfo.Width != 0 && ScreenInfo.Height != 0;

        public event Action<bool> OnDeviceConnectionChanged;

        public void Dispose() {
            stream?.Close();
        }

        public static List<SayoHidDevice> Devices {
            get {
                var devices = DeviceList.Local.GetHidDevices(0x8089);
                var deviceDict = new Dictionary<string, HidDevice>();
                foreach (var hidDevice in devices) {
                    Console.WriteLine($"ProductName: {hidDevice.GetProductName()}");
                    Console.WriteLine($"    FriendlyName: {hidDevice.GetFriendlyName()}");
                    Console.WriteLine($"    Manufacturer: {hidDevice.GetManufacturer()}");
                    Console.WriteLine($"    SerialNumber: {hidDevice.GetSerialNumber()}");
                    Console.WriteLine($"    DevicePath: {hidDevice.DevicePath}");
                    foreach (var deviceItem in hidDevice.GetReportDescriptor().DeviceItems) {
                        foreach (var usages in deviceItem.Usages.GetAllValues()) {
                            Console.WriteLine($"        Usage: {usages:X}");
                        }
                    }

                    var serialNumber = hidDevice.GetSerialNumber();
                    var usage = hidDevice.GetReportDescriptor().DeviceItems.FirstOrDefault()?
                        .Usages.GetAllValues().FirstOrDefault() ?? 0;
                    if (usage == 0xFF020002) {
                        if (!deviceDict.ContainsKey(serialNumber))
                            deviceDict.Add(hidDevice.GetSerialNumber(), hidDevice);
                        else
                            deviceDict[serialNumber] = hidDevice;
                    } else if (!deviceDict.ContainsKey(hidDevice.GetSerialNumber())) {
                        if (!deviceDict.ContainsKey(serialNumber))
                            deviceDict.Add(hidDevice.GetSerialNumber(), hidDevice);
                    }
                }

                return deviceDict.Select(device => new SayoHidDevice(device.Value)).ToList();
            }
        }

        private SayoHidDevice(HidDevice device) {
            Device = device;
            stream = device.Open();

            var usage = Device.GetReportDescriptor().DeviceItems.FirstOrDefault()?
                .Usages.GetAllValues().FirstOrDefault() ?? 0;
            if (usage != 0xFF020002) {
                stream.Close();
                stream = null;
                return;
            }
            buffer = new byte[Device.GetMaxOutputReportLength()];
            DeviceList.Local.Changed += (sender, args) => {
                bool found = false;
                foreach (var hidDevice in DeviceList.Local.GetHidDevices()) {
                    if (hidDevice.DevicePath == Device.DevicePath) {
                        found = true;
                        break;
                    }
                }
                switch (found) {
                    case false when stream != null:
                        stream.Close();
                        stream = null;
                        OnDeviceConnectionChanged?.Invoke(false);
                        return;
                    case true when stream == null:
                        if (!Device.TryOpen(out stream))
                            stream = null;
                        OnDeviceConnectionChanged?.Invoke(true);
                        break;
                }
            };
        }

        private void SetHeader(byte id, byte echo, ushort flag, byte cmd, byte index, ushort len) {
            len += 4;
            buffer[0] = id;
            buffer[1] = echo;
            buffer[2] = (byte)(flag & 0xFF);
            buffer[3] = (byte)(flag >> 8);
            buffer[4] = (byte)(len & 0xFF);
            buffer[5] = (byte)(len >> 8);
            buffer[6] = cmd;
            buffer[7] = index;
        }

        private ScreenInfoPacket GetScreenInfo() {
            SetHeader(
                id: 0x22,
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
                    screenInfo = ScreenInfoPacket.FromBytes(readBuffer);
                    if (screenInfo != null) {
                        return screenInfo;
                    }
                }
                return null;
            });
            task.Wait();
            return task.Result;
        }

        public async void SendImageAsync(Mat image) {
            await Task.Run(() => SendImage(image));
        }
        public async void SendImageAsync(byte[] rgb565) {
            await Task.Run(() => SendImage(rgb565));
        }

        public void SendImage(Mat image) {
            try {
                var len = image.Width * image.Height * 2;
                for (int j = 0; j < len;) {
                    var pixelCount = Math.Min(buffer.Length - 12, len - j);
                    buffer[8] = (byte)(j & 0xFF);
                    buffer[9] = (byte)((j >> 8) & 0xFF);
                    buffer[10] = (byte)((j >> 16) & 0xFF);
                    buffer[11] = (byte)((j >> 24) & 0xFF);
                    //Array.Copy(BitConverter.GetBytes(j), 0, _buffer, 8, 4);
                    Marshal.Copy(image.Data + j, buffer, 12, pixelCount);
                    //Array.Copy(rgb565, j, _buffer, 12, pixelCount);
                    SetHeader(
                        id: 0x22,
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

        public void SendImage(byte[] rgb565) {
            try {
                for (int j = 0; j < rgb565.Length;) {
                    var pixelCount = Math.Min(buffer.Length - 12, rgb565.Length - j);
                    Array.Copy(BitConverter.GetBytes(j), 0, buffer, 8, 4);
                    Array.Copy(rgb565, j, buffer, 12, pixelCount);
                    SetHeader(
                        id: 0x22,
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