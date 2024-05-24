using System;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace SayoDeviceStreamingAssistant {
    internal static class MatExtension {
        public static void DrawTo(this Mat src, Mat dst, Rect rect, ColorConversionCodes colorCvtCode = ColorConversionCodes.BGRA2BGR565) {
            if (rect.X >= dst.Width || rect.Y >= dst.Height || rect.Right <= 0 || rect.Bottom <= 0)
                return;
            Rect roiRect;
            roiRect.X = rect.X < 0 ? 0 : rect.X;
            roiRect.Y = rect.Y < 0 ? 0 : rect.Y;
            roiRect.Width = rect.Right > dst.Width ? dst.Width - roiRect.X : rect.Right - roiRect.X;
            roiRect.Height = rect.Bottom > dst.Height ? dst.Height - roiRect.Y : rect.Bottom - roiRect.Y;

            Vector2 scale;
            scale.X = (float)rect.Width / src.Width;
            scale.Y = (float)rect.Height / src.Height;

            Rect roi;
            roi.X = (int)((roiRect.X - rect.X) / scale.X);
            roi.Y = (int)((roiRect.Y - rect.Y) / scale.Y);
            roi.Width = (int)(roiRect.Width / scale.X);
            roi.Height = (int)(roiRect.Height / scale.Y);

            var roiMat = Resize(src.SubMat(roi), roiRect.Size);
            //Cv2.ImShow("roi", roiMat);
            Cv2.CvtColor(roiMat, roiMat, colorCvtCode);
            roiMat.CopyTo(dst.RowRange(roiRect.Top, roiRect.Bottom).ColRange
                (roiRect.Left, roiRect.Right));
        }
        private static readonly Size Size720P = new Size(1280, 640);
        private static Mat Resize(Mat mat, Size size) {
            var srcPixelCount = mat.Width * mat.Height;
            var dstPixelCount = size.Width * size.Height;
            var scale = Math.Sqrt((double)dstPixelCount / srcPixelCount);
            var deltaPixelCount = srcPixelCount - dstPixelCount;
            if (scale < 1 && deltaPixelCount > 2e6) {
                return mat.Resize(Size720P).Resize(size, 0, 0, InterpolationFlags.Area);
            }

            return mat.Resize(size, 0, 0, InterpolationFlags.Area);
        }
    }

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

    static class WinApi {
        [DllImport("kernel32.dll", EntryPoint = "CopyMemory", SetLastError = false)]
        public static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);
    }
}