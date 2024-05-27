using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Devices.Sensors;
using OpenCV.Net;

namespace SayoDeviceStreamingAssistant {
    internal static class MatExtension {
        public static Rect GetDefaultRect(Size srcSize, Size dstSize) {
            Rect rect;
            var ratio = (double)srcSize.Width / srcSize.Height;
            if (ratio > 2) {
                var space = dstSize.Height - dstSize.Width / ratio;
                rect = new Rect(0, (int)Math.Round(space / 2), dstSize.Width,
                    (int)Math.Round(dstSize.Width / ratio));
            } else {
                var space = dstSize.Width - dstSize.Height * ratio;
                rect = new Rect((int)Math.Round(space / 2), 0,
                    (int)Math.Round(dstSize.Height * ratio), dstSize.Height);
            }
            return rect;
        }
        public static void DrawToBGR565(this Mat src, Mat dst, Rect rect) {
            if (src == null || dst == null || src.Cols == 0 || src.Rows == 0)
                return;
            if (rect.X >= dst.Cols || rect.Y >= dst.Rows || rect.X + rect.Width <= 0 || rect.Y + rect.Height <= 0)
                return;
            Rect roiRect;
            roiRect.X = rect.X < 0 ? 0 : rect.X;
            roiRect.Y = rect.Y < 0 ? 0 : rect.Y;
            roiRect.Width = rect.X + rect.Width > dst.Cols ? dst.Cols - roiRect.X : rect.X + rect.Width - roiRect.X;
            roiRect.Height = rect.Y + rect.Height > dst.Rows ? dst.Rows - roiRect.Y : rect.Y + rect.Height - roiRect.Y;

            Vector2 scale;
            scale.X = (float)rect.Width / src.Cols;
            scale.Y = (float)rect.Height / src.Rows;

            Rect roi;
            roi.X = (int)((roiRect.X - rect.X) / scale.X);
            roi.Y = (int)((roiRect.Y - rect.Y) / scale.Y);
            roi.Width = (int)(roiRect.Width / scale.X);
            roi.Height = (int)(roiRect.Height / scale.Y);

            var roiMat = Resize(src.GetSubRect(roi), new Size(roiRect.Width, roiRect.Height));
            //Cv2.ImShow("roi", roiMat);
            var ccRoi = new Mat(roiMat.Size, Depth.U8, 2);
            CV.CvtColor(roiMat, ccRoi, roiMat.Channels == 4 ? ColorConversion.Bgra2Bgr565 : ColorConversion.Bgr2Bgr565);
            CV.Copy(ccRoi, dst.GetSubRect(roiRect));
        }

        private static Mat Resize(Mat mat, Size size) {
            var srcPixelCount = mat.Cols * mat.Rows;
            var dstPixelCount = size.Width * size.Height;
            var scale = Math.Sqrt((double)dstPixelCount / srcPixelCount);
            var deltaPixelCount = srcPixelCount - dstPixelCount;
            var res = new Mat(size, mat.Depth, mat.Channels);
            if (scale < 1 && deltaPixelCount > 1e6) {
                var mat640P = new Mat(640, 1280, mat.Depth, mat.Channels);
                CV.Resize(mat,mat640P);
                CV.Resize(mat640P, res, SubPixelInterpolation.Area);
                return res;
            }
            CV.Resize(mat, res, SubPixelInterpolation.Area);
            return res;
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