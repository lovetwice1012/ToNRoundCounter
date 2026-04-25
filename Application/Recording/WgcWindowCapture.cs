#nullable enable

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace ToNRoundCounter.Application.Recording
{
    /// <summary>
    /// Windows.Graphics.Capture (WGC) based window capturer. Captures the actual swap-chain
    /// content of DirectX/Vulkan applications (VRChat, games, etc.), unlike PrintWindow / BitBlt
    /// which return DWM compositor thumbnails refreshed at much less than the requested rate
    /// for hardware-accelerated apps (the "1 fps recording" symptom).
    ///
    /// Polling model (TryGetNextFrame) is used to integrate with the existing
    /// CaptureVideoLoopAsync without requiring a WinRT DispatcherQueue.
    /// </summary>
    [SupportedOSPlatform("windows10.0.19041.0")]
    internal sealed partial class WgcWindowCapture : IDisposable
    {
        private readonly GraphicsCaptureItem _item;
        private readonly IDirect3DDevice _device;
        private readonly IntPtr _d3d11DevicePtr;
        private readonly IntPtr _d3d11ContextPtr;
        private readonly Direct3D11CaptureFramePool _framePool;
        private readonly GraphicsCaptureSession _session;
        private readonly bool _ownsD3DDevice;
        private SizeInt32 _lastSize;
        private bool _disposed;

        /// <summary>
        /// Raw ID3D11Device pointer used by this capture session. Exposed (with explicit
        /// add-ref semantics in <see cref="AddRefD3D11Device"/>) so that the recording
        /// pipeline can route the captured / GPU-resized texture directly into a
        /// MediaFoundation hardware encoder configured against the SAME device, eliminating
        /// the need for a CPU readback. Callers MUST NOT release this pointer; use
        /// <see cref="AddRefD3D11Device"/> if they need an owning reference.
        /// </summary>
        internal IntPtr D3D11DevicePointer => _d3d11DevicePtr;

        /// <summary>
        /// Raw ID3D11DeviceContext pointer for the immediate context backing this capture
        /// session. See <see cref="D3D11DevicePointer"/> for ownership rules.
        /// </summary>
        internal IntPtr D3D11ContextPointer => _d3d11ContextPtr;

        // Reusable staging texture (CPU-readable copy target).
        private IntPtr _stagingTexture = IntPtr.Zero;
        private int _stagingWidth;
        private int _stagingHeight;

        private WgcWindowCapture(
            GraphicsCaptureItem item,
            IDirect3DDevice device,
            IntPtr d3d11DevicePtr,
            IntPtr d3d11ContextPtr,
            Direct3D11CaptureFramePool framePool,
            GraphicsCaptureSession session,
            bool ownsD3DDevice)
        {
            _item = item;
            _device = device;
            _d3d11DevicePtr = d3d11DevicePtr;
            _d3d11ContextPtr = d3d11ContextPtr;
            _framePool = framePool;
            _session = session;
            _ownsD3DDevice = ownsD3DDevice;
            _lastSize = item.Size;
        }

        /// <summary>
        /// Returns an owning reference to the underlying ID3D11Device. The caller becomes
        /// responsible for calling <see cref="Marshal.Release(IntPtr)"/> on the returned
        /// pointer. Returns <see cref="IntPtr.Zero"/> if the capture session has been
        /// disposed or never successfully initialized its device.
        /// </summary>
        internal IntPtr AddRefD3D11Device()
        {
            if (_disposed || _d3d11DevicePtr == IntPtr.Zero) return IntPtr.Zero;
            Marshal.AddRef(_d3d11DevicePtr);
            return _d3d11DevicePtr;
        }

        public Size ContentSize => new Size(Math.Max(1, _lastSize.Width), Math.Max(1, _lastSize.Height));

        public static bool IsSupported
        {
            get
            {
                try { return GraphicsCaptureSession.IsSupported(); }
                catch { return false; }
            }
        }

        public static WgcWindowCapture? TryCreateForWindow(IntPtr hwnd, out string? failureReason)
        {
            failureReason = null;
            if (hwnd == IntPtr.Zero)
            {
                failureReason = "Invalid window handle.";
                return null;
            }

            if (!IsSupported)
            {
                failureReason = "Windows.Graphics.Capture is not supported on this system.";
                return null;
            }

            IntPtr d3dDevice = IntPtr.Zero;
            IntPtr d3dContext = IntPtr.Zero;
            IntPtr dxgiDevice = IntPtr.Zero;
            IntPtr inspectable = IntPtr.Zero;
            IntPtr factoryPtr = IntPtr.Zero;
            IntPtr itemPtr = IntPtr.Zero;
            IntPtr classNameHString = IntPtr.Zero;

            try
            {
                int hr = D3D11CreateDevice(
                    IntPtr.Zero,
                    /* HARDWARE */ 1,
                    IntPtr.Zero,
                    /* BGRA_SUPPORT */ 0x20u,
                    IntPtr.Zero, 0, /* SDK_VERSION */ 7,
                    out d3dDevice, out _, out d3dContext);
                if (hr < 0 || d3dDevice == IntPtr.Zero)
                {
                    failureReason = $"D3D11CreateDevice failed (hr=0x{hr:X8}).";
                    return null;
                }

                Guid dxgiIid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"); // IDXGIDevice
                hr = Marshal.QueryInterface(d3dDevice, in dxgiIid, out dxgiDevice);
                if (hr < 0)
                {
                    failureReason = $"QueryInterface(IDXGIDevice) failed (hr=0x{hr:X8}).";
                    return null;
                }

                hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out inspectable);
                if (hr < 0 || inspectable == IntPtr.Zero)
                {
                    failureReason = $"CreateDirect3D11DeviceFromDXGIDevice failed (hr=0x{hr:X8}).";
                    return null;
                }

                IDirect3DDevice device = MarshalInspectable<IDirect3DDevice>.FromAbi(inspectable);

                const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
                hr = WindowsCreateString(className, (uint)className.Length, out classNameHString);
                if (hr < 0)
                {
                    failureReason = $"WindowsCreateString failed (hr=0x{hr:X8}).";
                    return null;
                }

                Guid interopIid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
                hr = RoGetActivationFactory(classNameHString, ref interopIid, out factoryPtr);
                if (hr < 0 || factoryPtr == IntPtr.Zero)
                {
                    failureReason = $"RoGetActivationFactory(IGraphicsCaptureItemInterop) failed (hr=0x{hr:X8}).";
                    return null;
                }

                IGraphicsCaptureItemInterop interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);

                Guid itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"); // IGraphicsCaptureItem
                int itemHr = interop.CreateForWindow(hwnd, ref itemIid, out itemPtr);
                if (itemHr < 0 || itemPtr == IntPtr.Zero)
                {
                    failureReason = $"GraphicsCaptureItem.CreateForWindow failed (hr=0x{itemHr:X8}).";
                    return null;
                }

                GraphicsCaptureItem item = MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);

                // Use a deep, FreeThreaded pool. FreeThreaded means the pool does not
                // require a DispatcherQueue on the calling thread, which lets us safely
                // poll and consume from any background thread (the capture loop runs on
                // a Task.Run worker, not a UI thread). Buffer depth of 12 means we can
                // absorb roughly 200ms of 60fps source bursts without the FramePool wrapping
                // around and silently dropping intermediate game frames - which was the
                // dominant cause of the recorded fps being far below the source render rate.
                Direct3D11CaptureFramePool pool;
                try
                {
                    pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                        device,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        12,
                        item.Size);
                }
                catch
                {
                    // Older OS builds (< 10.0.17763) lack CreateFreeThreaded; fall back
                    // to the regular Create. This still works for our polling consumer.
                    pool = Direct3D11CaptureFramePool.Create(
                        device,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        12,
                        item.Size);
                }

                var session = pool.CreateCaptureSession(item);
                try { session.IsCursorCaptureEnabled = false; } catch { }
                try
                {
                    var prop = typeof(GraphicsCaptureSession).GetProperty("IsBorderRequired");
                    prop?.SetValue(session, false);
                }
                catch
                {
                }

                session.StartCapture();

                var capture = new WgcWindowCapture(item, device, d3dDevice, d3dContext, pool, session, ownsD3DDevice: true);
                d3dDevice = IntPtr.Zero;
                d3dContext = IntPtr.Zero;
                return capture;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return null;
            }
            finally
            {
                if (itemPtr != IntPtr.Zero) Marshal.Release(itemPtr);
                if (factoryPtr != IntPtr.Zero) Marshal.Release(factoryPtr);
                if (classNameHString != IntPtr.Zero) WindowsDeleteString(classNameHString);
                if (inspectable != IntPtr.Zero) Marshal.Release(inspectable);
                if (dxgiDevice != IntPtr.Zero) Marshal.Release(dxgiDevice);
                if (d3dDevice != IntPtr.Zero) Marshal.Release(d3dDevice);
                if (d3dContext != IntPtr.Zero) Marshal.Release(d3dContext);
            }
        }

        /// <summary>
        /// Variant of <see cref="TryCreateForWindow"/> that REUSES an existing ID3D11Device
        /// (and its immediate context) instead of creating a new one. Used by the zero-copy
        /// recording path so the WGC capture, the GPU resize draw, and the MediaFoundation
        /// hardware encoder all run on the same device — eliminating the need to share
        /// textures via keyed-mutex or to copy across device boundaries.
        ///
        /// The caller retains ownership of <paramref name="d3d11Device"/> and
        /// <paramref name="d3d11Context"/>; this method will AddRef both internally so the
        /// returned <see cref="WgcWindowCapture"/> can be disposed independently of the
        /// caller's lifetime. The caller MUST still Release its own references when done.
        /// </summary>
        public static WgcWindowCapture? TryCreateForWindowSharingDevice(
            IntPtr hwnd,
            IntPtr d3d11Device,
            IntPtr d3d11Context,
            out string? failureReason)
        {
            failureReason = null;
            if (hwnd == IntPtr.Zero) { failureReason = "Invalid window handle."; return null; }
            if (d3d11Device == IntPtr.Zero) { failureReason = "External D3D11 device pointer is null."; return null; }
            if (d3d11Context == IntPtr.Zero) { failureReason = "External D3D11 context pointer is null."; return null; }
            if (!IsSupported) { failureReason = "Windows.Graphics.Capture is not supported on this system."; return null; }

            IntPtr dxgiDevice = IntPtr.Zero;
            IntPtr inspectable = IntPtr.Zero;
            IntPtr factoryPtr = IntPtr.Zero;
            IntPtr itemPtr = IntPtr.Zero;
            IntPtr classNameHString = IntPtr.Zero;
            IntPtr addedDevice = IntPtr.Zero;
            IntPtr addedContext = IntPtr.Zero;

            try
            {
                Marshal.AddRef(d3d11Device); addedDevice = d3d11Device;
                Marshal.AddRef(d3d11Context); addedContext = d3d11Context;

                Guid dxgiIid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
                int hr = Marshal.QueryInterface(d3d11Device, in dxgiIid, out dxgiDevice);
                if (hr < 0) { failureReason = $"QueryInterface(IDXGIDevice) failed (hr=0x{hr:X8})."; return null; }

                hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out inspectable);
                if (hr < 0 || inspectable == IntPtr.Zero) { failureReason = $"CreateDirect3D11DeviceFromDXGIDevice failed (hr=0x{hr:X8})."; return null; }

                IDirect3DDevice device = MarshalInspectable<IDirect3DDevice>.FromAbi(inspectable);

                const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
                hr = WindowsCreateString(className, (uint)className.Length, out classNameHString);
                if (hr < 0) { failureReason = $"WindowsCreateString failed (hr=0x{hr:X8})."; return null; }

                Guid interopIid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
                hr = RoGetActivationFactory(classNameHString, ref interopIid, out factoryPtr);
                if (hr < 0 || factoryPtr == IntPtr.Zero) { failureReason = $"RoGetActivationFactory failed (hr=0x{hr:X8})."; return null; }

                IGraphicsCaptureItemInterop interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
                Guid itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                int itemHr = interop.CreateForWindow(hwnd, ref itemIid, out itemPtr);
                if (itemHr < 0 || itemPtr == IntPtr.Zero) { failureReason = $"GraphicsCaptureItem.CreateForWindow failed (hr=0x{itemHr:X8})."; return null; }

                GraphicsCaptureItem item = MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr);

                Direct3D11CaptureFramePool pool;
                try
                {
                    pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 12, item.Size);
                }
                catch
                {
                    pool = Direct3D11CaptureFramePool.Create(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 12, item.Size);
                }

                var session = pool.CreateCaptureSession(item);
                try { session.IsCursorCaptureEnabled = false; } catch { }
                try
                {
                    var prop = typeof(GraphicsCaptureSession).GetProperty("IsBorderRequired");
                    prop?.SetValue(session, false);
                }
                catch { }
                session.StartCapture();

                var capture = new WgcWindowCapture(item, device, addedDevice, addedContext, pool, session, ownsD3DDevice: true);
                addedDevice = IntPtr.Zero;
                addedContext = IntPtr.Zero;
                return capture;
            }
            catch (Exception ex) { failureReason = ex.Message; return null; }
            finally
            {
                if (itemPtr != IntPtr.Zero) Marshal.Release(itemPtr);
                if (factoryPtr != IntPtr.Zero) Marshal.Release(factoryPtr);
                if (classNameHString != IntPtr.Zero) WindowsDeleteString(classNameHString);
                if (inspectable != IntPtr.Zero) Marshal.Release(inspectable);
                if (dxgiDevice != IntPtr.Zero) Marshal.Release(dxgiDevice);
                if (addedDevice != IntPtr.Zero) Marshal.Release(addedDevice);
                if (addedContext != IntPtr.Zero) Marshal.Release(addedContext);
            }
        }

        /// <summary>
        /// Pulls the NEWEST captured frame from WGC and copies it into <paramref name="destination"/>.
        /// Older frames currently queued in the FramePool are discarded.
        ///
        /// Why drain-to-latest (not FIFO): the recorder runs the encoder at a fixed target fps
        /// (`MF_MT_FRAME_RATE`), so the muxed file's timescale assumes that exact rate. If we
        /// fed every WGC-delivered frame the captured count would equal the game's render rate
        /// (which is variable and usually higher than target fps), causing the muxed playback
        /// to play back slow proportional to (game_fps / target_fps). With drain-to-latest,
        /// each tick of the capture loop encodes exactly one fresh frame, so:
        ///   captured_fps == target_fps  →  file duration == real wall-clock duration.
        /// </summary>
        public bool TryAcquireFrame(Bitmap destination)
        {
            if (_disposed)
            {
                return false;
            }

            Direct3D11CaptureFrame? frame = null;
            Direct3D11CaptureFrame? next;
            while ((next = _framePool.TryGetNextFrame()) != null)
            {
                frame?.Dispose();
                frame = next;
            }

            if (frame == null)
            {
                return false;
            }

            try
            {
                var size = frame.ContentSize;
                _lastSize = size;
                int width = Math.Max(1, size.Width);
                int height = Math.Max(1, size.Height);

                IntPtr texturePtr = IntPtr.Zero;
                try
                {
                    var access = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
                    Guid texIid = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
                    int hr = access.GetInterface(ref texIid, out texturePtr);
                    if (hr < 0 || texturePtr == IntPtr.Zero)
                    {
                        return false;
                    }

                    return CopyTextureToBitmap(texturePtr, width, height, destination);
                }
                finally
                {
                    if (texturePtr != IntPtr.Zero) Marshal.Release(texturePtr);
                }
            }
            finally
            {
                frame.Dispose();
            }
        }

        private unsafe bool CopyTextureToBitmap(IntPtr sourceTexture, int width, int height, Bitmap destination)
        {
            // GPU resize fast-path: if the caller provided a destination Bitmap whose size
            // differs from the source frame, run a bilinear resize on the GPU (HLSL pixel
            // shader) and read back only the smaller result, instead of doing a full-size
            // CPU readback followed by a CPU scale. Diagnostics on a 21s 4K → 1080p capture
            // showed the original CPU path spending ~26 ms / frame on the 4K readback alone
            // (PCIe transfer of 32 MB) plus ~14 ms / frame on the CPU scale; the GPU path
            // collapses both into a single ~5-8 ms readback of the 8 MB target buffer.
            if (destination.Width != width || destination.Height != height)
            {
                return CopyTextureToBitmapGpuResized(sourceTexture, width, height, destination);
            }

            if (_stagingTexture == IntPtr.Zero || _stagingWidth != width || _stagingHeight != height)
            {
                if (_stagingTexture != IntPtr.Zero)
                {
                    Marshal.Release(_stagingTexture);
                    _stagingTexture = IntPtr.Zero;
                }

                var desc = new D3D11_TEXTURE2D_DESC
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = 87, // DXGI_FORMAT_B8G8R8A8_UNORM
                    SampleDesc_Count = 1,
                    SampleDesc_Quality = 0,
                    Usage = 3,            // STAGING
                    BindFlags = 0,
                    CPUAccessFlags = 0x20000, // READ
                    MiscFlags = 0,
                };

                int hr = D3D11Device_CreateTexture2D(_d3d11DevicePtr, ref desc, IntPtr.Zero, out _stagingTexture);
                if (hr < 0 || _stagingTexture == IntPtr.Zero)
                {
                    return false;
                }

                _stagingWidth = width;
                _stagingHeight = height;
            }

            D3D11Context_CopyResource(_d3d11ContextPtr, _stagingTexture, sourceTexture);

            int mapHr = D3D11Context_Map(_d3d11ContextPtr, _stagingTexture, 0, /* D3D11_MAP_READ */ 1, 0, out var mapped);
            if (mapHr < 0)
            {
                return false;
            }

            try
            {
                int copyWidth = Math.Min(width, destination.Width);
                int copyHeight = Math.Min(height, destination.Height);
                var rect = new Rectangle(0, 0, destination.Width, destination.Height);
                var data = destination.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte* src = (byte*)mapped.pData;
                    byte* dst = (byte*)data.Scan0;
                    int rowBytes = copyWidth * 4;
                    for (int y = 0; y < copyHeight; y++)
                    {
                        Buffer.MemoryCopy(
                            src + (long)y * mapped.RowPitch,
                            dst + (long)y * data.Stride,
                            data.Stride,
                            rowBytes);
                    }
                }
                finally
                {
                    destination.UnlockBits(data);
                }
            }
            finally
            {
                D3D11Context_Unmap(_d3d11ContextPtr, _stagingTexture, 0);
            }

            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try { _session?.Dispose(); } catch { }
            try { _framePool?.Dispose(); } catch { }

            if (_stagingTexture != IntPtr.Zero) { try { Marshal.Release(_stagingTexture); } catch { } _stagingTexture = IntPtr.Zero; }
            DisposeGpuResizeResources();
            // Only release the D3D device/context if this instance created them. When an
            // external device is shared in (zero-copy encode path) the owner is responsible
            // for the lifetime.
            if (_ownsD3DDevice)
            {
                if (_d3d11ContextPtr != IntPtr.Zero) { try { Marshal.Release(_d3d11ContextPtr); } catch { } }
                if (_d3d11DevicePtr != IntPtr.Zero) { try { Marshal.Release(_d3d11DevicePtr); } catch { } }
            }
        }

        // ---------------- Vtable dispatch helpers ----------------

        private static unsafe int D3D11Device_CreateTexture2D(IntPtr device, ref D3D11_TEXTURE2D_DESC desc, IntPtr initialData, out IntPtr texture)
        {
            // ID3D11Device::CreateTexture2D = vtable slot 5 (after IUnknown 0..2 + CreateBuffer=3 + CreateTexture1D=4).
            void*** vtbl = (void***)device;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, D3D11_TEXTURE2D_DESC*, IntPtr, IntPtr*, int>)((*vtbl)[5]);
            fixed (D3D11_TEXTURE2D_DESC* descPtr = &desc)
            {
                IntPtr local;
                int hr = fn(device, descPtr, initialData, &local);
                texture = local;
                return hr;
            }
        }

        private static unsafe void D3D11Context_CopyResource(IntPtr context, IntPtr destination, IntPtr source)
        {
            // ID3D11DeviceContext vtable: 0..2 IUnknown, 3..6 ID3D11DeviceChild, then methods.
            // CopyResource is slot 47.
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)((*vtbl)[47]);
            fn(context, destination, source);
        }

        private static unsafe int D3D11Context_Map(IntPtr context, IntPtr resource, uint subresource, uint mapType, uint mapFlags, out D3D11_MAPPED_SUBRESOURCE mapped)
        {
            // Slot 14.
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, D3D11_MAPPED_SUBRESOURCE*, int>)((*vtbl)[14]);
            D3D11_MAPPED_SUBRESOURCE local;
            int hr = fn(context, resource, subresource, mapType, mapFlags, &local);
            mapped = local;
            return hr;
        }

        private static unsafe void D3D11Context_Unmap(IntPtr context, IntPtr resource, uint subresource)
        {
            // Slot 15.
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)((*vtbl)[15]);
            fn(context, resource, subresource);
        }

        // ---------------- Native interop ----------------

        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(
            IntPtr pAdapter,
            int DriverType,
            IntPtr Software,
            uint Flags,
            IntPtr pFeatureLevels,
            uint FeatureLevels,
            uint SDKVersion,
            out IntPtr ppDevice,
            out int pFeatureLevel,
            out IntPtr ppImmediateContext);

        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", CallingConvention = CallingConvention.StdCall)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        [DllImport("combase.dll")]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, uint length, out IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            [PreserveSig]
            int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr result);

            [PreserveSig]
            int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr result);
        }

        [ComImport]
        [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDirect3DDxgiInterfaceAccess
        {
            [PreserveSig]
            int GetInterface(ref Guid iid, out IntPtr result);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D11_TEXTURE2D_DESC
        {
            public uint Width;
            public uint Height;
            public uint MipLevels;
            public uint ArraySize;
            public uint Format;
            public uint SampleDesc_Count;
            public uint SampleDesc_Quality;
            public uint Usage;
            public uint BindFlags;
            public uint CPUAccessFlags;
            public uint MiscFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D11_MAPPED_SUBRESOURCE
        {
            public IntPtr pData;
            public uint RowPitch;
            public uint DepthPitch;
        }
    }
}
