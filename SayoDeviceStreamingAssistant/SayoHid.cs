
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using HidSharp;
using MongoDB.Bson;
using OpenCvSharp;

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

public class SayoHidDevice: IDisposable {
    public HidDevice Device;
    public HidStream Stream;
    private byte[] _buffer;

    public StreamingController ScreenStream;

    public ScreenInfoPacket ScreenInfo = null;
    public bool SupportsStreaming => ScreenInfo != null && ScreenInfo.Width != 0 && ScreenInfo.Height != 0;

    public void Dispose() {
        ScreenStream?.Dispose();
        Stream?.Close();
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
                }
                else if (!deviceDict.ContainsKey(hidDevice.GetSerialNumber())) {
                    if(!deviceDict.ContainsKey(serialNumber))
                        deviceDict.Add(hidDevice.GetSerialNumber(), hidDevice);
                }
            }

            return deviceDict.Select(device => new SayoHidDevice(device.Value)).ToList();
        }
    }

    public SayoHidDevice(HidDevice device) {
        Device = device;
        Stream = device.Open();

        var usage = Device.GetReportDescriptor().DeviceItems.FirstOrDefault()?
            .Usages.GetAllValues().FirstOrDefault() ?? 0;
        if (usage != 0xFF020002) {
            Stream.Close();
            return;
        }
        _buffer = new byte[Device.GetMaxOutputReportLength()];
        ScreenInfo = GetScreenInfo();
        ScreenStream = new StreamingController(this);
        DeviceList.Local.Changed += (sender, args) => {
            bool found = false;
            foreach (var hidDevice in DeviceList.Local.GetHidDevices()) {
                if (hidDevice.DevicePath == Device.DevicePath) {
                    found = true;
                    break;
                }
            }
            switch (found) {
                case false when Stream != null:
                    Stream.Close();
                    Stream = null;
                    return;
                case true when Stream == null:
                    if(!Device.TryOpen(out Stream))
                        Stream = null;
                    break;
            }
        };
    }

    private void SetHeader(byte id, byte echo, ushort flag, byte cmd, byte index, ushort len) {
        len += 4;
        _buffer[0] = id;
        _buffer[1] = echo;
        _buffer[2] = (byte)(flag & 0xFF);
        _buffer[3] = (byte)(flag >> 8);
        _buffer[4] = (byte)(len & 0xFF);
        _buffer[5] = (byte)(len >> 8);
        _buffer[6] = cmd;
        _buffer[7] = index;
    }

    public ScreenInfoPacket GetScreenInfo() {
        SetHeader(
            id: 0x22,
            echo: SayoHidPacketBase.ApplicationEcho,
            flag: 0x7296,
            cmd: ScreenInfoPacket.Cmd,
            index: 0,
            len: 0);
        ScreenInfoPacket screenInfo;
        Stream.Write(_buffer, 0, 8);
        for (; ; ) {
            var res = Stream.Read();
            if ((screenInfo = ScreenInfoPacket.FromBytes(res)) != null)
                break;
        }
        return screenInfo;
    }

    public void SendImage(Mat image) {
        try {
            var len = image.Width * image.Height * 2;
            for (int j = 0; j < len;) {
                var pixelCount = Math.Min(_buffer.Length - 12, len - j);
                _buffer[8] = (byte)(j & 0xFF);
                _buffer[9] = (byte)((j >> 8) & 0xFF);
                _buffer[10] = (byte)((j >> 16) & 0xFF);
                _buffer[11] = (byte)((j >> 24) & 0xFF);
                //Array.Copy(BitConverter.GetBytes(j), 0, _buffer, 8, 4);
                Marshal.Copy(image.Data + j, _buffer, 12, pixelCount);
                //Array.Copy(rgb565, j, _buffer, 12, pixelCount);
                SetHeader(
                    id: 0x22,
                    echo: SayoHidPacketBase.ApplicationEcho,
                    flag: 0x7296,
                    cmd: 0x25,
                    index: 0,
                    len: (ushort)(pixelCount + 4));
                Stream.Write(_buffer, 0, pixelCount + 12);
                j += pixelCount;
            }
        } catch {
            //Console.WriteLine(e);
        }
    }

    public void SendImage(byte[] rgb565) {
        try {
            for (int j = 0; j < rgb565.Length;) {
                var pixelCount = Math.Min(_buffer.Length - 12, rgb565.Length - j);
                Array.Copy(BitConverter.GetBytes(j), 0, _buffer, 8, 4);
                Array.Copy(rgb565, j, _buffer, 12, pixelCount);
                SetHeader(
                    id: 0x22,
                    echo: SayoHidPacketBase.ApplicationEcho,
                    flag: 0x7296,
                    cmd: 0x25,
                    index: 0,
                    len: (ushort)(pixelCount + 4));
                Stream.Write(_buffer, 0, pixelCount + 12);
                j += pixelCount;
            }
        } catch {
            //Console.WriteLine(e);
        }
    }
}