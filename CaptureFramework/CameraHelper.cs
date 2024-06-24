using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using DirectShowLib;

namespace CaptureFramework {
    public class CameraInfo {
        public string Name { get; set; }
        public string DeviceName { get; set; }
        public int Index { get; set; }
    }

    public static class CameraHelper {
        public static List<CameraInfo> EnumCameras() {
            var videoInputDevices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            return videoInputDevices
                .Select((t, i) => new CameraInfo { Name = t.Name, DeviceName = t.DevicePath, Index = i }).ToList();
        }
        
        public static void GetCameraDetails(string deviceName, out int index, out int width, out int height, out double fps) {
            index = -1;
            width = 0;
            height = 0;
            fps = 0;
            DsDevice[] videoInputDevices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            int i = 0;
            foreach (DsDevice device in videoInputDevices) {
                if (device.Name != deviceName) {
                    ++i;   
                    continue;
                }
                
                IFilterGraph2 graphBuilder = (IFilterGraph2)new FilterGraph();
                IBaseFilter sourceFilter = null;
                try {
                    int hr = graphBuilder.AddSourceFilterForMoniker(device.Mon, null, device.Name, out sourceFilter);
                    DsError.ThrowExceptionForHR(hr);
                    IAMStreamConfig streamConfig = GetStreamConfigInterface(graphBuilder, sourceFilter);
                    if (streamConfig != null) {
                        GetVideoCapabilities(streamConfig, out width, out height, out long timePerFrame);
                        index = i;
                        fps = timePerFrame > 0 ? 10000000.0 / timePerFrame : 0;
                    }
                }
                catch (Exception ex) {
                    Console.WriteLine($"Error: {ex.Message}");
                }
                finally {
                    // 释放资源
                    if (sourceFilter != null) {
                        Marshal.ReleaseComObject(sourceFilter);
                    }

                    if (graphBuilder != null) {
                        Marshal.ReleaseComObject(graphBuilder);
                    }
                }

                if (index != -1) {
                    break;
                }
                ++i;
            }
        }

        static IAMStreamConfig GetStreamConfigInterface(IFilterGraph2 graphBuilder, IBaseFilter sourceFilter) {
            // 获取过滤器上的输出引脚
            IEnumPins pinEnum;
            int hr = sourceFilter.EnumPins(out pinEnum);
            DsError.ThrowExceptionForHR(hr);

            IPin[] pins = new IPin[1];
            IAMStreamConfig streamConfig = null;

            try {
                while (pinEnum.Next(1, pins, IntPtr.Zero) == 0) {
                    // 检查每个引脚是否支持 IAMStreamConfig
                    PinDirection direction;
                    hr = pins[0].QueryDirection(out direction);
                    DsError.ThrowExceptionForHR(hr);

                    if (direction == PinDirection.Output) {
                        streamConfig = pins[0] as IAMStreamConfig;
                        if (streamConfig != null) {
                            break;
                        }
                    }

                    Marshal.ReleaseComObject(pins[0]);
                }
            }
            finally {
                if (pinEnum != null) {
                    Marshal.ReleaseComObject(pinEnum);
                }
            }

            return streamConfig;
        }

        static void GetVideoCapabilities(IAMStreamConfig streamConfig, out int width, out int height, out long timePerFrame) {
            width = 0;
            height = 0;
            timePerFrame = 0;
            int piCount = 0, piSize = 0;
            int hr = streamConfig.GetNumberOfCapabilities(out piCount, out piSize);
            DsError.ThrowExceptionForHR(hr);

            for (int i = 0; i < piCount; i++) {
                // 分配结构来存储能力信息
                IntPtr taskAlloc = Marshal.AllocCoTaskMem(piSize);
                AMMediaType mediaType = null;

                try {
                    hr = streamConfig.GetStreamCaps(i, out mediaType, taskAlloc);
                    DsError.ThrowExceptionForHR(hr);

                    // 获取视频信息头
                    var videoInfoHeader =
                        (VideoInfoHeader)Marshal.PtrToStructure(mediaType.formatPtr, typeof(VideoInfoHeader));

                    // 输出分辨率和刷新率信息
                    Console.WriteLine(
                        $"Resolution: {videoInfoHeader.BmiHeader.Width}x{videoInfoHeader.BmiHeader.Height}");
                    Console.WriteLine(
                        $"Frame Rate: {(videoInfoHeader.AvgTimePerFrame > 0 ? 10000000 / videoInfoHeader.AvgTimePerFrame : 0)} fps");
                    
                    width = videoInfoHeader.BmiHeader.Width;
                    height = videoInfoHeader.BmiHeader.Height;
                    timePerFrame = videoInfoHeader.AvgTimePerFrame;

                    DsUtils.FreeAMMediaType(mediaType);
                }
                finally {
                    Marshal.FreeCoTaskMem(taskAlloc);
                }
            }
        }
    }
}