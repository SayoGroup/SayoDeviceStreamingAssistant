using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenCvSharp;
using Mat = OpenCvSharp.Mat;
using RectInt =OpenCvSharp.Rect;
using RectDouble = Windows.Foundation.Rect;
using SizeInt = OpenCvSharp.Size;
using SizeDouble = Windows.Foundation.Size;

namespace SayoDeviceStreamingAssistant.Sources {
    internal static class MatExtension {
        public static RectDouble GetDefaultRect(SizeInt srcSize, SizeInt dstSize) {
            RectDouble rect;
            var srcRatio = (double)srcSize.Width / srcSize.Height;
            var dstRatio = (double)dstSize.Width / dstSize.Height;
            if (srcRatio > dstRatio) {
                var space = dstSize.Height - dstSize.Width / srcRatio;
                rect = new RectDouble(0, space / dstRatio, dstSize.Width,
                    dstSize.Width / srcRatio);
            } else {
                var space = dstSize.Width - dstSize.Height * srcRatio;
                rect = new RectDouble(space / dstRatio, 0,
                    dstSize.Height * srcRatio, dstSize.Height);
            }
            return rect;
        }
        
        public static RectInt GetRoiRectAsDst(this Mat src, RectDouble dRect) {
            var rect = dRect.ToCvRect();
            RectInt roiDst;
            roiDst.X = rect.X < 0 ? 0 : rect.X;
            roiDst.Y = rect.Y < 0 ? 0 : rect.Y;
            roiDst.Width = rect.X + rect.Width > src.Cols ? src.Cols - roiDst.X : rect.X + rect.Width - roiDst.X;
            roiDst.Height = rect.Y + rect.Height > src.Rows ? src.Rows - roiDst.Y : rect.Y + rect.Height - roiDst.Y;
            return roiDst;
        }
        public static RectInt GetRoiRectAsSrc(this Mat src, RectDouble dRect, RectInt roiDst) {
            var rect = dRect.ToCvRect();
            Vector2 scale;
            scale.X = (float)rect.Width / src.Cols;
            scale.Y = (float)rect.Height / src.Rows;

            RectInt roiSrc;
            roiSrc.X = (int)((roiDst.X - rect.X) / scale.X);
            roiSrc.Y = (int)((roiDst.Y - rect.Y) / scale.Y);
            roiSrc.Width = (int)(roiDst.Width / scale.X);
            roiSrc.Height = (int)(roiDst.Height / scale.Y);
            return roiSrc;
        }
        
        private static readonly Dictionary<SizeInt,Mat> Bgr565MatCache = new Dictionary<SizeInt, Mat>();
        public static void DrawToBgr565(this Mat src, Mat dst, RectDouble dstRect) {
            if (src == null || dst == null || src.Cols == 0 || src.Rows == 0)
                return;
            var rect = dstRect.ToCvRect();
            if (rect.X >= dst.Cols || rect.Y >= dst.Rows || rect.X + rect.Width <= 0 || rect.Y + rect.Height <= 0)
                return;
            
            var roiDst = dst.GetRoiRectAsDst(dstRect);
            var roiSrc = src.GetRoiRectAsSrc(dstRect, roiDst);

            //var roiMat = new Mat(src, roiSrc).Resize(new SizeInt(roiDst.Width, roiDst.Height));
            
            var roiMat = Resize(new Mat(src,roiSrc), new SizeInt(roiDst.Width, roiDst.Height));
            
            //Resize(new Mat(src,roiSrc), new SizeInt(roiDst.Width, roiDst.Height));
            //Cv2.ImShow("roi", roiMat);
            if (!Bgr565MatCache.ContainsKey(roiMat.Size())) {
                if(Bgr565MatCache.Count > 8)
                    Bgr565MatCache.Clear();
                Bgr565MatCache[roiMat.Size()] = new Mat(roiMat.Size(), MatType.CV_8UC2);
                 //new Mat(roiMat.Size, Depth.U8, 2);
            }
            //var ccRoi = Bgr565MatCache[roiMat.Size()];
            var ccRoi = roiMat.CvtColor(roiMat.Channels() == 4 ? ColorConversionCodes.BGRA2BGR565 : ColorConversionCodes.BGR2BGR565);
            //CV.CvtColor(roiMat, ccRoi, roiMat.Channels == 4 ? ColorConversion.Bgra2Bgr565 : ColorConversion.Bgr2Bgr565);
            ccRoi.CopyTo(new Mat(dst, roiDst));
            //CV.Copy(ccRoi, dst.GetSubRect(roiDst));
        }

        private static Mat _mat480P;
        private static readonly object ResizeLock = new object();
        private static Mat Resize(Mat mat, Size size) {
            var srcPixelCount = mat.Cols * mat.Rows;
            var dstPixelCount = size.Width * size.Height;
            var scale = Math.Sqrt((double)dstPixelCount / srcPixelCount);
            var deltaPixelCount = srcPixelCount - dstPixelCount;
            var res = new Mat(size, mat.Type());//new Mat(size, mat.Depth, mat.Channels);
            if (scale < 1 && deltaPixelCount > 1e6) {
                lock (ResizeLock) {
                    if (_mat480P == null || _mat480P.Depth() != mat.Depth() || _mat480P.Channels() != mat.Channels())
                        _mat480P = new Mat(new Size(1280, 720), mat.Type());//new Mat(640, 480, mat.Depth, mat.Channels);
                    _mat480P = mat.Resize(_mat480P.Size());
                    //CV.Resize(mat,_mat480P);
                    res = _mat480P.Resize(res.Size(), 0, 0, InterpolationFlags.Area);
                    //CV.Resize(_mat480P, res, SubPixelInterpolation.Area);
                }
                return res;
            }
            //CV.Resize(mat, res, SubPixelInterpolation.Area);
            res = mat.Resize(res.Size(), 0, 0, InterpolationFlags.Area);
            return res;
        }
    }
    
    static class BasicConverter {
        public static Windows.Foundation.Rect ToWinRect(this OpenCvSharp.Rect cvRect) {
            return new Windows.Foundation.Rect(cvRect.X, cvRect.Y, cvRect.Width, cvRect.Height);
        }
        public static OpenCvSharp.Rect ToCvRect(this Windows.Foundation.Rect winRect) {
            return new OpenCvSharp.Rect((int)winRect.X, (int)winRect.Y, (int)winRect.Width, (int)winRect.Height);
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