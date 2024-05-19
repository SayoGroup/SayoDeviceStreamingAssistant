using System;
using System.Collections.Generic;
using Windows.Graphics.Capture;
using OpenCvSharp;
using Composition.WindowsRuntimeHelpers;
using System.Diagnostics;
using System.Security.AccessControl;
using MongoDB.Bson;
using System.IO;

public class StreamingController: IDisposable {
    public string Name;
    public FrameSource FrameSource;
    public ScreenInfoPacket ScreenInfo;
    public SayoHidDevice HidDevice;

    public string SourceType;
    public MonitorInfo SourceMonitor;
    public Process SourceProcess;
    public string SourceMedia;

    public int FrameCount = 0;
    
    public BsonDocument ToBsonDocument() {
        return new BsonDocument {
            { "Name", Name },
            { "SourceType", SourceType },
            { "SourceName", GetSourceName(true) },
            { "FrameCount", FrameCount },
            { "Transform",  FrameSource.FrameRect.ToBsonDocument() }
        };
    }

    public string GetSourceName(bool fullName = false) {
        switch (SourceType) {
            case "Monitor":
                return SourceMonitor.DeviceName;
            case "Window":
                return fullName ? Path.GetFileName(SourceProcess.MainModule.FileName) + ":" + SourceProcess.MainWindowTitle 
                    : SourceProcess.MainWindowTitle;
            case "Media":
                return fullName ? SourceMedia : Path.GetFileName(SourceMedia);
            default:
                return "Unknown";
        }
    }

    public void Dispose() {
        RemoveFrameSource();
    }
    public StreamingController(SayoHidDevice hidDevice) {
        HidDevice = hidDevice;
        ScreenInfo = HidDevice.GetScreenInfo();
    }

    public void RemoveFrameSource() {
        switch (SourceType) {
            case "Monitor":
                (FrameSource as CaptureFramework)?.Dispose();
                FrameSource = null;
                break;
            case "Window":
                (FrameSource as CaptureFramework)?.Dispose();
                FrameSource = null;
                break;
            case "Media":
                (FrameSource as VideoFramework)?.Dispose();
                FrameSource = null;
                break;
        }
    }

    private void SetFrameSource(GraphicsCaptureItem item) {
        FrameSource = new CaptureFramework(item, new Size(ScreenInfo.Width, ScreenInfo.Height), ScreenInfo.RefreshRate);
        FrameSource.OnFrameReady += OnFrameReady;

    }

    public void SetFrameSource(MonitorInfo monitor) {
        RemoveFrameSource();
        SourceType = "Monitor";
        SourceMonitor = monitor;
        var item = CaptureHelper.CreateItemForMonitor(monitor.Hmon);
        SetFrameSource(item);
    }
    public void SetFrameSource(Process process) {
        
        RemoveFrameSource();
        SourceType = "Window";
        SourceProcess = process;
        var item = CaptureHelper.CreateItemForWindow(process.MainWindowHandle);
        if (item == null) return;
        SetFrameSource(item);
    }

    public void SetFrameSource(string videoPath) {
        RemoveFrameSource();
        SourceType = "Media";
        SourceMedia = videoPath;
        FrameSource = new VideoFramework(videoPath, new Size(ScreenInfo.Width, ScreenInfo.Height), ScreenInfo.RefreshRate);
        FrameSource.OnFrameReady += OnFrameReady;
    }

    public bool Enabled {
        get => FrameSource?.Enabled ?? false;
        set => FrameSource.Enabled = value;
    }

    private void OnFrameReady(Mat frame) {
        HidDevice.SendImage(frame);

        ++FrameCount;
        if (FrameCount % 60 == 0)
            GC.Collect();
    }

}
