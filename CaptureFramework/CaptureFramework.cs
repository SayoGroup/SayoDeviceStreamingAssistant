using System;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.UI.Composition;
using Composition.WindowsRuntimeHelpers;
using OpenCvSharp;
using SharpDX.DXGI;
using D3D11 = SharpDX.Direct3D11;

namespace CaptureFramework {
    public class CaptureFramework : IDisposable {
        static IDirect3DDevice _device = Direct3D11Helper.CreateDevice();
        private GraphicsCaptureItem _item;
        private Direct3D11CaptureFramePool _framePool;
        private GraphicsCaptureSession _session;
        private SizeInt32 _lastSize;

        private D3D11.Device _d3dDevice;
        private SwapChain1 _swapChain;

        private D3D11.Texture2DDescription _stagingTextureDesc;
        private D3D11.Texture2D _stagingTexture;

        public bool initialized {
            get;
            private set;
        }

        public CaptureFramework(GraphicsCaptureItem i) {
            _item = i;
        }

        public Size GetSourceSize() {
            return new Size(_lastSize.Width, _lastSize.Height);
        }

        public void Init() {
            if (_d3dDevice != null || _item.Size.Width == 0 || _item.Size.Height == 0 || initialized)
                return;

            _d3dDevice = Direct3D11Helper.CreateSharpDXDevice(_device);
            var dxgiFactory = new Factory2();
            var description = new SwapChainDescription1 {
                Width = _item.Size.Width,
                Height = _item.Size.Height,
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
                _item.Size);
            _session = _framePool.CreateCaptureSession(_item);

            _lastSize = _item.Size;

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
            initialized = true;
        }
        public void Dispose() {
            for (; reading;) Task.Delay(1).Wait();
            initialized = false;
            _stagingTexture?.Dispose();
            _stagingTexture = null;
            _session?.Dispose();
            _session = null;
            _framePool?.Dispose();
            _framePool = null;
            _swapChain?.Dispose();
            _swapChain = null;
            _d3dDevice?.Dispose();
            _d3dDevice = null;
        }

        public ICompositionSurface CreateSurface(Compositor compositor) {
            return compositor.CreateCompositionSurfaceForSwapChain(_swapChain);
        }

        private bool reading = false;
        public bool ReadFrame(Mat mat) {
            if (!initialized) return false;
            reading = true;
            if (mat == null)
                throw new ArgumentNullException(nameof(mat));
            var newSize = false;
            using (var frame = _framePool.TryGetNextFrame()) {
                if (frame == null) {
                    return reading = false;
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
                }

                using (var bitmap = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface)) {
                    _d3dDevice.ImmediateContext.CopyResource(bitmap, _stagingTexture);
                    var data = _d3dDevice.ImmediateContext.MapSubresource(_stagingTexture, 0, D3D11.MapMode.Read,
                        D3D11.MapFlags.None);

                    //bitmap has 32 bytes(8 pixels) alignment
                    var bmat = new Mat(_stagingTexture.Description.Height,
                        _stagingTexture.Description.Width + ((32 - (_lastSize.Width % 32)) % 32), MatType.CV_8UC4,
                        data.DataPointer);

                    //cut the mat to the correct size
                    new Mat(bmat, new Rect(0, 0, _lastSize.Width, _lastSize.Height)).CopyTo(mat);

                    bmat.Dispose();
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
            reading = false;
            return true;
        }
    }
}