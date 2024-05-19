using OpenCvSharp;
using SharpDX.Win32;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;

namespace SayoDeviceStreamingAssistant {
    public class TransformedMat {
        //percent
        public Rect2d Rect = new Rect2d(0.1, 0.1, 0.9, 0.9);

        public Mat Apply(Mat mat, int resWidth, int resHeight) {
            var roi = new Rect((int)(Rect.X * resWidth),
                (int)(Rect.Y * resHeight),
                (int)(Rect.Width * resWidth),
                (int)(Rect.Height * resHeight));
            var res = new Mat(new Size(resWidth, resHeight), mat.Type());
            mat.DrawTo(res, roi);
            return res;
        }
    }

    public static class CvExtensions {
        public static void DrawTo(this Mat src, Mat dst, Rect rect) {
            if (rect.X >= dst.Width || rect.Y >= dst.Height || rect.Right <= 0 || rect.Bottom <= 0)
                return;

            //Cv2.ImShow("src", src);
            
            //caculate needed area
            var scale = new Vector2((float)rect.Width / src.Width, (float)rect.Height / src.Height);
            // var roiLeft = (int)(rect.X < 0 ? -rect.X / scale.X : 0);
            // var roiTop = (int)(rect.Y < 0 ? -rect.Y / scale.Y : 0);
            // var roiRight = (int)(rect.Right > dst.Width ? (dst.Width - rect.Left) / scale.X : 0);
            // var roiBottom = (int)(rect.Bottom > dst.Height ? (dst.Height - rect.Top) / scale.Y : 0);
            var roi = new Rect((int)(rect.X < 0 ? -rect.X / scale.X : 0),
                (int)(rect.Y < 0 ? -rect.Y / scale.Y : 0),
                (int)(rect.Right > dst.Width ? (dst.Width - rect.Left) / scale.X : 0),
                (int)(rect.Bottom > dst.Height ? (dst.Height - rect.Top) / scale.Y : 0));
            rect.X = rect.X < 0 ? 0 : rect.X;
            rect.Y = rect.Y < 0 ? 0 : rect.Y;
            rect.Width = rect.Right > dst.Width ? dst.Width - rect.Left : rect.Width;
            rect.Height = rect.Bottom > dst.Height ? dst.Height - rect.Top : rect.Height;
            var roiMat = src.AdjustROI(roi.Top,roi.Bottom,roi.Left,roi.Right)
                .Resize(new Size(rect.Width, rect.Height));
            //Cv2.ImShow("roi", roiMat);
            //Cv2.WaitKey(1);
            roiMat.CopyTo(dst.AdjustROI(rect.Top, rect.Bottom, rect.Left, rect.Right));
            //Cv2.ImShow("dst", dst);
            //Cv2.WaitKey();
        }
    }
}