using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

static class ByteReader {
    public static ushort ReadUInt16(this byte[] bytes, ref int offset) {
        offset += 2;
        return BitConverter.ToUInt16(bytes, offset - 2);
    }
    public static uint ReadUInt32(this byte[] bytes, ref int offset) {
        offset += 4;
        return BitConverter.ToUInt32(bytes, offset - 4);
    }
}

static class WinAPI {
    [DllImport("kernel32.dll", EntryPoint = "CopyMemory", SetLastError = false)]
    public static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);
}