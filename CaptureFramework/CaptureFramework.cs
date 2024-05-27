using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.UI.Composition;
using Composition.WindowsRuntimeHelpers;
using OpenCV;
using OpenCV.Net;
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

        public event Action ItemeDestroyed;

        private Thread workThread;
        private Dispatcher dispatcher;
        private bool initialized {
            get;
            set;
        }
        
        public enum SourceType {
            Monitor,
            Window,
        }
        private IntPtr sourceHandle;
        private SourceType sourceType;
        public CaptureFramework(IntPtr handle, SourceType type) {
            sourceHandle = handle;
            sourceType = type;
            workThread = new Thread(() => {
                dispatcher = Dispatcher.CurrentDispatcher;
                Dispatcher.Run();
            });
            workThread.SetApartmentState(ApartmentState.STA);
            workThread.IsBackground = true;
            workThread.Start();
            while (dispatcher == null) {
                Thread.Sleep(1);
            }
        }

        public Size GetSourceSize() {
            return new Size(_lastSize.Width, _lastSize.Height);
        }

        public bool Init() {
            if (!dispatcher.CheckAccess()) {
                return dispatcher.Invoke(Init); 
            }
            if (_d3dDevice != null)
                return false;
            if(_item == null)
            {
                switch (sourceType)
                {
                    case SourceType.Window:
                        _item = CaptureHelper.CreateItemForWindow(sourceHandle);
                        break;
                    case SourceType.Monitor:
                        _item = CaptureHelper.CreateItemForMonitor(sourceHandle);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(sourceType), sourceType, null);
                }
            }

            if (_item == null || _item.Size.Width == 0 || _item.Size.Height == 0 || initialized)
                return false;
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
            //_session.IsBorderRequired = false;

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
            return true;
        }
        public void Dispose() {
            if (!dispatcher.CheckAccess()) {
                dispatcher.Invoke(Dispose);
                return;
            }
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
        public Mat ReadFrame() {
            Mat res;
            if (!dispatcher.CheckAccess()) {
                return dispatcher.Invoke(ReadFrame);  
            }
            if (!initialized) return null;
            reading = true;
            if (_item.Size.Width == 0 || _item.Size.Height == 0) {
                reading = false;
                ItemeDestroyed?.Invoke();
                return null;
            }
            var newSize = false;
            using (var frame = _framePool.TryGetNextFrame()) {
                if (frame == null) {
                    reading = false;
                    return null;
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
                        data.RowPitch/4, Depth.U8, 4,
                        data.DataPointer);
    
                    //cut the mat to the correct size
                    res = bmat.Clone();
                    //CV.Copy(bmat.GetRows(0, _lastSize.Height).GetCols(0, _lastSize.Width),mat);
                    //mat = bmat.GetRows(0, _lastSize.Height).GetCols(0, _lastSize.Width);
                    //new Mat(bmat, new Rect(0, 0, _lastSize.Width, _lastSize.Height)).CopyTo(mat);

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
            return res;
        }
    }
}