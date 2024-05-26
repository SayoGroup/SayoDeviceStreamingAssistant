﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using HidSharp;
using OpenCvSharp;
using OpenCvSharp.Aruco;

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
        
        private static void SetHidMessageHeader(IList<byte> buffer, byte id, byte echo, ushort flag, byte cmd, byte index, ushort len) {
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
    }
    
    public partial class SayoHidDevice : IDisposable {
        public string SerialNumber { get; private set; }
        //usage, device
        private readonly ConcurrentDictionary<uint, HidDevice> devices = new ConcurrentDictionary<uint, HidDevice>();
        private readonly ConcurrentDictionary<uint, HidStream> streams = new ConcurrentDictionary<uint, HidStream>();
        private readonly ConcurrentDictionary<uint, byte[]> buffers = new ConcurrentDictionary<uint, byte[]>();
        public event Action<bool> OnDeviceConnectionChanged;

        public bool Connected => devices.Count > 0;
        public bool SupportStreaming {
            get {
                var screenInfo = GetScreenInfo();
                return screenInfo != null && screenInfo.Width != 0 && screenInfo.Height != 0;
            }
        }
        
        public void Dispose() {
            //stream?.Close();
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
        public ScreenInfoPacket GetScreenInfo() {
            if (!devices.ContainsKey(0xFF020002)) return null;
            try {
                var buffer = buffers[0xFF020002];
                var stream = streams[0xFF020002];
                SetHidMessageHeader(
                    buffer: buffer,
                    id: 0x22,
                    echo: SayoHidPacketBase.ApplicationEcho,
                    flag: 0x7296,
                    cmd: ScreenInfoPacket.Cmd,
                    index: 0,
                    len: 0);
                //ScreenInfoPacket screenInfo;
                streams[0xFF020002].Write(buffers[0xFF020002], 0, 8);
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
                return task.Result;
            } catch {
                return null;
            }
        }

        public async void SendImageAsync(Mat image) {
            await Task.Run(() => SendImage(image));
        }
        public async void SendImageAsync(byte[] rgb565) {
            await Task.Run(() => SendImage(rgb565));
        }

        public double ImageSendElapsedMs { get; private set; }
        public double SendImageRate { get; private set; }

        Queue<DateTime> fpsCounter = new Queue<DateTime>();
        public void SendImage(Mat image) {
            try {
                var buffer = buffers[0xFF020002];
                var stream = streams[0xFF020002];
                var sw = Stopwatch.StartNew();
                sw.Start();
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
                    SetHidMessageHeader(
                        buffer: buffer,
                        id: 0x22,
                        echo: SayoHidPacketBase.ApplicationEcho,
                        flag: 0x7296,
                        cmd: 0x25,
                        index: 0,
                        len: (ushort)(pixelCount + 4));
                    stream.Write(buffer, 0, pixelCount + 12);
                    j += pixelCount;
                }
                ImageSendElapsedMs = sw.Elapsed.TotalMilliseconds;
                sw.Stop();
                fpsCounter.Enqueue(DateTime.Now);
                while (fpsCounter.Count > 30) 
                    fpsCounter.Dequeue();
                SendImageRate = (fpsCounter.Count - 1) / (fpsCounter.Last() - fpsCounter.First()).TotalSeconds;
            } catch {
                //Console.WriteLine(e);
            }
        }

        public void SendImage(byte[] rgb565) {
            try {
                var buffer = buffers[0xFF020002];
                var stream = streams[0xFF020002];
                var sw = Stopwatch.StartNew();
                sw.Start();
                for (int j = 0; j < rgb565.Length;) {
                    var pixelCount = Math.Min(buffer.Length - 12, rgb565.Length - j);
                    Array.Copy(BitConverter.GetBytes(j), 0, buffer, 8, 4);
                    Array.Copy(rgb565, j, buffer, 12, pixelCount);
                    SetHidMessageHeader(
                        buffer: buffer,
                        id: 0x22,
                        echo: SayoHidPacketBase.ApplicationEcho,
                        flag: 0x7296,
                        cmd: 0x25,
                        index: 0,
                        len: (ushort)(pixelCount + 4));
                    stream.Write(buffer, 0, pixelCount + 12);
                    j += pixelCount;
                }
                ImageSendElapsedMs = sw.Elapsed.TotalMilliseconds;
                sw.Stop();
                fpsCounter.Enqueue(DateTime.Now);
                while (fpsCounter.Count > 30)
                    fpsCounter.Dequeue();
                SendImageRate = (fpsCounter.Count - 1) / (fpsCounter.Last() - fpsCounter.First()).TotalSeconds;
            } catch {
                //Console.WriteLine(e);
            }
        }
    }
}