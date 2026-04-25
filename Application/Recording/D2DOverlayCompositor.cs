#nullable enable

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ToNRoundCounter.Application.Recording
{
    internal sealed class D2DOverlayCompositor : IDisposable
    {
        private const uint D2D1_FACTORY_TYPE_SINGLE_THREADED = 0;
        private const uint D2D1_DEVICE_CONTEXT_OPTIONS_NONE = 0;
        private const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
        private const uint D2D1_ALPHA_MODE_PREMULTIPLIED = 1;
        private const uint D2D1_BITMAP_OPTIONS_TARGET = 0x1;
        private const uint D2D1_BITMAP_INTERPOLATION_MODE_LINEAR = 1;

        private static readonly Guid IID_ID2D1Factory1 = new("bb12d362-daee-4b9a-aa1d-14ba401cfa1f");
        private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        private static readonly Guid IID_IDXGISurface = new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");

        private IntPtr _factory;
        private IntPtr _d2dDevice;
        private IntPtr _deviceContext;
        private bool _disposed;

        public bool IsInitialized => _deviceContext != IntPtr.Zero;

        public bool TryInitialize(IntPtr d3d11Device, out string? failureReason)
        {
            failureReason = null;
            if (_disposed) { failureReason = "Compositor already disposed."; return false; }
            if (IsInitialized) return true;
            if (d3d11Device == IntPtr.Zero) { failureReason = "D3D11 device is null."; return false; }

            IntPtr dxgiDevice = IntPtr.Zero;
            try
            {
                Guid dxgiIid = IID_IDXGIDevice;
                int hr = Marshal.QueryInterface(d3d11Device, in dxgiIid, out dxgiDevice);
                if (hr < 0 || dxgiDevice == IntPtr.Zero)
                {
                    failureReason = $"QueryInterface(IDXGIDevice) failed (hr=0x{hr:X8}).";
                    return false;
                }

                Guid factoryIid = IID_ID2D1Factory1;
                hr = D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, ref factoryIid, IntPtr.Zero, out _factory);
                if (hr < 0 || _factory == IntPtr.Zero)
                {
                    failureReason = $"D2D1CreateFactory failed (hr=0x{hr:X8}).";
                    return false;
                }

                hr = Factory_CreateDevice(_factory, dxgiDevice, out _d2dDevice);
                if (hr < 0 || _d2dDevice == IntPtr.Zero)
                {
                    failureReason = $"ID2D1Factory1::CreateDevice failed (hr=0x{hr:X8}).";
                    return false;
                }

                hr = Device_CreateDeviceContext(_d2dDevice, D2D1_DEVICE_CONTEXT_OPTIONS_NONE, out _deviceContext);
                if (hr < 0 || _deviceContext == IntPtr.Zero)
                {
                    failureReason = $"ID2D1Device::CreateDeviceContext failed (hr=0x{hr:X8}).";
                    return false;
                }

                return true;
            }
            finally
            {
                if (dxgiDevice != IntPtr.Zero) Marshal.Release(dxgiDevice);
            }
        }

        public IntPtr BeginDrawOnTexture(IntPtr texture, out string? failureReason)
        {
            failureReason = null;
            if (!IsInitialized || _disposed) { failureReason = "Compositor not initialized."; return IntPtr.Zero; }
            if (texture == IntPtr.Zero) { failureReason = "Texture is null."; return IntPtr.Zero; }

            IntPtr surface = IntPtr.Zero;
            try
            {
                Guid surfIid = IID_IDXGISurface;
                int hr = Marshal.QueryInterface(texture, in surfIid, out surface);
                if (hr < 0 || surface == IntPtr.Zero)
                {
                    failureReason = $"QueryInterface(IDXGISurface) failed (hr=0x{hr:X8}).";
                    return IntPtr.Zero;
                }

                var props = new D2D1_BITMAP_PROPERTIES1
                {
                    PixelFormat_Format = DXGI_FORMAT_B8G8R8A8_UNORM,
                    PixelFormat_AlphaMode = D2D1_ALPHA_MODE_PREMULTIPLIED,
                    DpiX = 96f,
                    DpiY = 96f,
                    BitmapOptions = D2D1_BITMAP_OPTIONS_TARGET,
                    ColorContext = IntPtr.Zero,
                };

                hr = DeviceContext_CreateBitmapFromDxgiSurface(_deviceContext, surface, ref props, out IntPtr bitmap);
                if (hr < 0 || bitmap == IntPtr.Zero)
                {
                    failureReason = $"CreateBitmapFromDxgiSurface failed (hr=0x{hr:X8}).";
                    return IntPtr.Zero;
                }

                DeviceContext_SetTarget(_deviceContext, bitmap);
                DeviceContext_BeginDraw(_deviceContext);
                return bitmap;
            }
            finally
            {
                if (surface != IntPtr.Zero) Marshal.Release(surface);
            }
        }

        public int EndDraw()
        {
            if (!IsInitialized || _disposed) return -1;
            int hr = DeviceContext_EndDraw(_deviceContext, IntPtr.Zero, IntPtr.Zero);
            DeviceContext_SetTarget(_deviceContext, IntPtr.Zero);
            return hr;
        }

        public bool DrawOverlayBitmap(Bitmap bitmap, RectangleF destination)
        {
            if (!IsInitialized || _disposed || bitmap == null) return false;
            if (bitmap.Width <= 0 || bitmap.Height <= 0 || destination.Width <= 0f || destination.Height <= 0f) return false;

            Bitmap? converted = null;
            IntPtr d2dBitmap = IntPtr.Zero;
            try
            {
                Bitmap source = bitmap;
                if (source.PixelFormat != PixelFormat.Format32bppPArgb)
                {
                    converted = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(converted))
                    {
                        g.Clear(Color.Transparent);
                        g.DrawImageUnscaled(source, 0, 0);
                    }
                    source = converted;
                }

                var sourceRect = new Rectangle(0, 0, source.Width, source.Height);
                var data = source.LockBits(sourceRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    var props = new D2D1_BITMAP_PROPERTIES
                    {
                        PixelFormat_Format = DXGI_FORMAT_B8G8R8A8_UNORM,
                        PixelFormat_AlphaMode = D2D1_ALPHA_MODE_PREMULTIPLIED,
                        DpiX = 96f,
                        DpiY = 96f,
                    };
                    var size = new D2D_SIZE_U { Width = (uint)source.Width, Height = (uint)source.Height };

                    int hr = RenderTarget_CreateBitmap(_deviceContext, size, data.Scan0, (uint)Math.Abs(data.Stride), ref props, out d2dBitmap);
                    if (hr < 0 || d2dBitmap == IntPtr.Zero) return false;
                }
                finally
                {
                    source.UnlockBits(data);
                }

                var destRect = new D2D_RECT_F
                {
                    Left = destination.Left,
                    Top = destination.Top,
                    Right = destination.Right,
                    Bottom = destination.Bottom,
                };
                RenderTarget_DrawBitmap(_deviceContext, d2dBitmap, ref destRect, 1f, D2D1_BITMAP_INTERPOLATION_MODE_LINEAR, IntPtr.Zero);
                return true;
            }
            finally
            {
                if (d2dBitmap != IntPtr.Zero) Marshal.Release(d2dBitmap);
                converted?.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_deviceContext != IntPtr.Zero) { try { Marshal.Release(_deviceContext); } catch { } _deviceContext = IntPtr.Zero; }
            if (_d2dDevice != IntPtr.Zero) { try { Marshal.Release(_d2dDevice); } catch { } _d2dDevice = IntPtr.Zero; }
            if (_factory != IntPtr.Zero) { try { Marshal.Release(_factory); } catch { } _factory = IntPtr.Zero; }
        }

        private const int SLOT_RT_CreateBitmap = 4;
        private const int SLOT_RT_DrawBitmap = 26;
        private const int SLOT_RT_BeginDraw = 48;
        private const int SLOT_RT_EndDraw = 49;
        private const int SLOT_DC_CreateBitmapFromDxgiSurface = 54;
        private const int SLOT_DC_SetTarget = 57;

        private static unsafe int Factory_CreateDevice(IntPtr factory, IntPtr dxgiDevice, out IntPtr d2dDevice)
        {
            void*** vtbl = (void***)factory;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)((*vtbl)[17]);
            IntPtr local;
            int hr = fn(factory, dxgiDevice, &local);
            d2dDevice = local;
            return hr;
        }

        private static unsafe int Device_CreateDeviceContext(IntPtr device, uint options, out IntPtr context)
        {
            void*** vtbl = (void***)device;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)((*vtbl)[4]);
            IntPtr local;
            int hr = fn(device, options, &local);
            context = local;
            return hr;
        }

        private static unsafe int RenderTarget_CreateBitmap(
            IntPtr context, D2D_SIZE_U size, IntPtr sourceData, uint pitch, ref D2D1_BITMAP_PROPERTIES props, out IntPtr bitmap)
        {
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, D2D_SIZE_U, IntPtr, uint, D2D1_BITMAP_PROPERTIES*, IntPtr*, int>)((*vtbl)[SLOT_RT_CreateBitmap]);
            fixed (D2D1_BITMAP_PROPERTIES* p = &props)
            {
                IntPtr local;
                int hr = fn(context, size, sourceData, pitch, p, &local);
                bitmap = local;
                return hr;
            }
        }

        private static unsafe void RenderTarget_DrawBitmap(
            IntPtr context, IntPtr bitmap, ref D2D_RECT_F destination, float opacity, uint interpolationMode, IntPtr sourceRectangle)
        {
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, D2D_RECT_F*, float, uint, IntPtr, void>)((*vtbl)[SLOT_RT_DrawBitmap]);
            fixed (D2D_RECT_F* d = &destination)
            {
                fn(context, bitmap, d, opacity, interpolationMode, sourceRectangle);
            }
        }

        private static unsafe int DeviceContext_CreateBitmapFromDxgiSurface(
            IntPtr context, IntPtr surface, ref D2D1_BITMAP_PROPERTIES1 props, out IntPtr bitmap)
        {
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, D2D1_BITMAP_PROPERTIES1*, IntPtr*, int>)((*vtbl)[SLOT_DC_CreateBitmapFromDxgiSurface]);
            fixed (D2D1_BITMAP_PROPERTIES1* p = &props)
            {
                IntPtr local;
                int hr = fn(context, surface, p, &local);
                bitmap = local;
                return hr;
            }
        }

        private static unsafe void DeviceContext_SetTarget(IntPtr context, IntPtr image)
        {
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)((*vtbl)[SLOT_DC_SetTarget]);
            fn(context, image);
        }

        private static unsafe void DeviceContext_BeginDraw(IntPtr context)
        {
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, void>)((*vtbl)[SLOT_RT_BeginDraw]);
            fn(context);
        }

        private static unsafe int DeviceContext_EndDraw(IntPtr context, IntPtr tag1, IntPtr tag2)
        {
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int>)((*vtbl)[SLOT_RT_EndDraw]);
            return fn(context, tag1, tag2);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D2D1_BITMAP_PROPERTIES1
        {
            public uint PixelFormat_Format;
            public uint PixelFormat_AlphaMode;
            public float DpiX;
            public float DpiY;
            public uint BitmapOptions;
            public IntPtr ColorContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D2D1_BITMAP_PROPERTIES
        {
            public uint PixelFormat_Format;
            public uint PixelFormat_AlphaMode;
            public float DpiX;
            public float DpiY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D2D_SIZE_U
        {
            public uint Width;
            public uint Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D2D_RECT_F
        {
            public float Left;
            public float Top;
            public float Right;
            public float Bottom;
        }

        [DllImport("d2d1.dll", ExactSpelling = true)]
        private static extern int D2D1CreateFactory(
            uint factoryType,
            ref Guid riid,
            IntPtr pFactoryOptions,
            out IntPtr ppIFactory);
    }
}
