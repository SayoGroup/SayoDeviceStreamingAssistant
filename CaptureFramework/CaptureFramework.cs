//  ---------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// 
//  The MIT License (MIT)
// 
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
// 
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
// 
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//  THE SOFTWARE.
//  ---------------------------------------------------------------------------------

using Composition.WindowsRuntimeHelpers;
using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.UI.Composition;
using OpenCvSharp;
using SharpDX;
using D3D11 = SharpDX.Direct3D11;
using SharpDX.DXGI;
using Windows.Media;
using System.Collections.Generic;

public class FrameSource {
    public delegate void OnFrameReadyDelegate(Mat frame);
    public event OnFrameReadyDelegate OnFrameReady;

    protected Mat Frame = null;
    protected Size FrameSize;
    protected int FrameRate;

    public Rect FrameRect;

    protected readonly MicroTimer MicroTimer = new MicroTimer();

    public double FrameTime;
    public double Fps;

    protected void FrameReady() {
        OnFrameReady?.Invoke(Frame);
    }
    public Mat PeekFrame() {
        return Frame;
    }
    public Rect GetDefaultRect() {
        var srcRect = this is VideoFramework video ? video.GetSourceSize() : 
            this is CaptureFramework capture ? capture.GetSourceSize() : new Size();

        Rect result;
        var ratio = (double)srcRect.Width / srcRect.Height;
        if (ratio > 2) {
            var space = FrameSize.Height - FrameSize.Width / ratio;
            result = new Rect(0, (int)Math.Round(space / 2), FrameSize.Width,
                (int)Math.Round(FrameSize.Width / ratio));
        } else {
            var space = FrameSize.Width - FrameSize.Height * ratio;
            result = new Rect((int)Math.Round(space / 2), 0,
                (int)Math.Round(FrameSize.Height * ratio), FrameSize.Height);
        }
        return result;
    }

    public bool Enabled {
        get => MicroTimer.Enabled;
        set => MicroTimer.Enabled = value;
    }

}


public class VideoFramework : FrameSource, IDisposable {
    protected readonly VideoCapture _videoCapture;
    private DateTime beginPlay;
    private Queue<DateTime> fpsCounter = new Queue<DateTime>();
    public VideoFramework(string videoPath, Size canvasSize, int refreshRate, Rect? frameRect = null) {
        _videoCapture = new VideoCapture(videoPath);
        Frame = new Mat(canvasSize.Height, canvasSize.Width, MatType.CV_8UC2);
        FrameRate = refreshRate;
        FrameSize = canvasSize;
        FrameRect = frameRect ?? GetDefaultRect();

        MicroTimer.MicroTimerElapsed += FrameTick;
        MicroTimer.Interval = (long)Math.Round(1e6 / _videoCapture.Fps);

        FrameTick(null, null);
    }

    public void FrameTick(object sender,MicroTimerEventArgs args) {
        var s = DateTime.Now;
        var videoFrame = new Mat();
        _videoCapture.Read(videoFrame);
        if (videoFrame.Empty()) {
            _videoCapture.Set(VideoCaptureProperties.PosFrames, 0);
            _videoCapture.Read(videoFrame);
        }
        //fill the frame with pure black
        Frame.SetTo(Scalar.Black);
        videoFrame.DrawTo(Frame, FrameRect);
        FrameReady();
        FrameTime = (DateTime.Now - s).TotalMilliseconds;
        Fps = fpsCounter.Count;
        fpsCounter.Enqueue(DateTime.Now);
        while (fpsCounter.Count > 0 && (DateTime.Now - fpsCounter.Peek()).TotalSeconds > 1) {
            fpsCounter.Dequeue();
        }
    }

    public Size GetSourceSize() {
        return new Size(_videoCapture.FrameWidth, _videoCapture.FrameHeight);
    }

    public void Dispose() {
        MicroTimer.StopAndWait();
        _videoCapture?.Dispose();
    }

}

public class CaptureFramework : FrameSource, IDisposable {
    static IDirect3DDevice _device = Direct3D11Helper.CreateDevice();

    private GraphicsCaptureItem _item;
    private readonly Direct3D11CaptureFramePool _framePool;
    private readonly GraphicsCaptureSession _session;
    private SizeInt32 _lastSize;

    private readonly D3D11.Device _d3dDevice;
    private readonly SwapChain1 _swapChain;

    private D3D11.Texture2DDescription _stagingTextureDesc;
    private D3D11.Texture2D _stagingTexture;

    public int FrameSkipped;

    private Queue<DateTime> fpsCounter = new Queue<DateTime>();

    public CaptureFramework(GraphicsCaptureItem i, Size canvasSize,
                            int refreshRate, Rect? frameRect = null) {
        _item = i;
        Frame = new Mat(canvasSize.Height, canvasSize.Width, MatType.CV_8UC2);
        Frame.SetTo(Scalar.Black);
        FrameSize = canvasSize;
        FrameRate = refreshRate;
        FrameRect = frameRect ?? GetDefaultRect();
        _d3dDevice = Direct3D11Helper.CreateSharpDXDevice(_device);

        var dxgiFactory = new Factory2();
        var description = new SwapChainDescription1 {
            Width = i.Size.Width,
            Height = i.Size.Height,
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription {
                Count = 1,
                Quality = 0
            },
            Usage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = AlphaMode.Premultiplied,
            Flags = SwapChainFlags.None
        };
        _swapChain = new SwapChain1(dxgiFactory, _d3dDevice, ref description);

        _framePool = Direct3D11CaptureFramePool.Create(
            _device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            i.Size);
        _session = _framePool.CreateCaptureSession(i);

        _lastSize = i.Size;

        _stagingTextureDesc = new D3D11.Texture2DDescription {
            CpuAccessFlags = D3D11.CpuAccessFlags.Read,
            BindFlags = D3D11.BindFlags.None,
            Format = Format.B8G8R8A8_UNorm,
            Width = _lastSize.Width,
            Height = _lastSize.Height,
            OptionFlags = D3D11.ResourceOptionFlags.None,
            MipLevels = 1,
            ArraySize = 1,
            SampleDescription = { Count = 1, Quality = 0 },
            Usage = D3D11.ResourceUsage.Staging
        };

        _stagingTexture = new D3D11.Texture2D(_d3dDevice, _stagingTextureDesc);
        _session.StartCapture();

        bool capturing = false;
        MicroTimer.MicroTimerElapsed +=
            (sender, timerEventArgs) => {
                if (!capturing) {
                    var s = DateTime.Now;
                    capturing = true;
                    try {
                        GetFrame();
                        FrameReady();
                    } catch { }
                    capturing = false;
                    FrameTime = (DateTime.Now - s).TotalMilliseconds;
                    Fps = fpsCounter.Count;
                    fpsCounter.Enqueue(DateTime.Now);
                    while (fpsCounter.Count > 0 && (DateTime.Now - fpsCounter.Peek()).TotalSeconds > 1) {
                        fpsCounter.Dequeue();
                    }
                } else {
                    ++FrameSkipped;
                }
            };
        MicroTimer.Interval = (long)Math.Round(1e6 / FrameRate);
        GetFrame();
        FrameReady();
    }

    public Size GetSourceSize() {
        return new Size(_item.Size.Width, _item.Size.Height);
    }

    public void Dispose() {
        MicroTimer.StopAndWait();
        _stagingTexture?.Dispose();
        _session?.Dispose();
        _framePool?.Dispose();
        _swapChain?.Dispose();
        _d3dDevice?.Dispose();
    }

    public ICompositionSurface CreateSurface(Compositor compositor) {
        return compositor.CreateCompositionSurfaceForSwapChain(_swapChain);
    }

    private void GetFrame() {
        var newSize = false;
        using (var frame = _framePool.TryGetNextFrame()) {
            if (frame == null) {
                return;
            }

            if (frame.ContentSize.Width != _lastSize.Width ||
                frame.ContentSize.Height != _lastSize.Height) {
                newSize = true;
                _lastSize = frame.ContentSize;
                _swapChain.ResizeBuffers(
                    2,
                    _lastSize.Width,
                    _lastSize.Height,
                    Format.B8G8R8A8_UNorm,
                    SwapChainFlags.None);
                _stagingTexture.Dispose();
                _stagingTextureDesc = new D3D11.Texture2DDescription {
                    CpuAccessFlags = D3D11.CpuAccessFlags.Read,
                    BindFlags = D3D11.BindFlags.None,
                    Format = Format.B8G8R8A8_UNorm,
                    Width = _lastSize.Width,
                    Height = _lastSize.Height,
                    OptionFlags = D3D11.ResourceOptionFlags.None,
                    MipLevels = 1,
                    ArraySize = 1,
                    SampleDescription = { Count = 1, Quality = 0 },
                    Usage = D3D11.ResourceUsage.Staging
                };
                _stagingTexture = new D3D11.Texture2D(_d3dDevice, _stagingTextureDesc);
                FrameRect = GetDefaultRect();
            }

            using (var bitmap = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface)) {
                _d3dDevice.ImmediateContext.CopyResource(bitmap, _stagingTexture);
                var data = _d3dDevice.ImmediateContext.MapSubresource(_stagingTexture, 0, D3D11.MapMode.Read,
                    D3D11.MapFlags.None);

                //bitmap has 32 bytes(8 pixels) alignment
                var mat = new Mat(_stagingTexture.Description.Height,
                    _stagingTexture.Description.Width + ((32 - (_lastSize.Width % 32)) % 32), MatType.CV_8UC4,
                    data.DataPointer);

                //cut the mat to the correct size
                mat = new Mat(mat, new Rect(0, 0, _lastSize.Width, _lastSize.Height));
                Frame.SetTo(Scalar.Black);
                mat.DrawTo(Frame, FrameRect);
                mat.Dispose();
                _d3dDevice.ImmediateContext.UnmapSubresource(_stagingTexture, 0);
            }

            _swapChain.Present(0, PresentFlags.None);
            if (newSize) {
                _framePool.Recreate(
                    _device,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    _lastSize);
            }
        }
    }
}