#nullable enable

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics.Capture;
using WinRT;

namespace ToNRoundCounter.Application.Recording
{
    /// <summary>
    /// GPU-side bilinear resize path for <see cref="WgcWindowCapture"/>.
    ///
    /// Why this exists: capturing a 4K source and downscaling on the CPU has two heavy costs
    ///   (a) Full-size readback (32 MB / frame at 3840x2160 BGRA) - ~25 ms per frame on a
    ///       typical PCIe 3.0 x16 link, single-threaded copy.
    ///   (b) CPU bilinear/nearest-neighbor scale - 13 ms+ per frame even with unsafe code.
    /// Together they cap capture at ~13 fps even when WGC delivers more.
    ///
    /// This implementation uses an HLSL pixel-shader full-screen pass on the existing D3D11
    /// device to scale the WGC frame texture into a small render-target texture, then copies
    /// only the small RT into a CPU-readable staging texture. Net per-frame cost drops to
    /// roughly the size of the *target* readback (e.g. 8 MB for 1080p) plus a sub-millisecond
    /// GPU draw.
    ///
    /// All D3D11 access is via raw vtable dispatch (matching the style of the rest of this
    /// file) so we avoid a SharpDX/Vortice dependency just for a few helper objects.
    /// </summary>
    internal sealed partial class WgcWindowCapture
    {
        // Lazily-created GPU resources, all keyed off the destination size.
        private IntPtr _resizeRtTexture = IntPtr.Zero;   // ID3D11Texture2D (RENDER_TARGET, target size)
        private IntPtr _resizeRtv = IntPtr.Zero;         // ID3D11RenderTargetView
        private IntPtr _resizeStaging = IntPtr.Zero;     // ID3D11Texture2D (STAGING, target size)
        private int _resizeTargetWidth;
        private int _resizeTargetHeight;

        private IntPtr _resizeVertexShader = IntPtr.Zero;
        private IntPtr _resizePixelShader = IntPtr.Zero;
        private IntPtr _resizeSampler = IntPtr.Zero;
        private bool _resizeShadersReady;

        // HLSL: a 3-vertex full-screen triangle generated entirely from SV_VertexID, sampled
        // with bilinear filtering. No vertex/index buffers required, no input layout binding.
        private const string ResizeVertexShaderHlsl = @"
struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
VSOut main(uint id : SV_VertexID)
{
    VSOut o;
    o.uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(o.uv * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
    return o;
}";

        private const string ResizePixelShaderHlsl = @"
Texture2D<float4> SrcTex : register(t0);
SamplerState SrcSampler : register(s0);
struct PSIn { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
float4 main(PSIn i) : SV_Target { return SrcTex.Sample(SrcSampler, i.uv); }";

        private unsafe bool CopyTextureToBitmapGpuResized(IntPtr sourceTexture, int srcWidth, int srcHeight, Bitmap destination)
        {
            int dstW = destination.Width;
            int dstH = destination.Height;

            if (!EnsureResizeShaders())
            {
                return false;
            }

            if (!EnsureResizeTargets(dstW, dstH))
            {
                return false;
            }

            // Per-frame: build a transient SRV over the WGC source texture. WGC creates frame
            // textures with BIND_SHADER_RESOURCE, so this just needs a default-format view.
            IntPtr srv = IntPtr.Zero;
            try
            {
                int hr = D3D11Device_CreateShaderResourceView(_d3d11DevicePtr, sourceTexture, IntPtr.Zero, out srv);
                if (hr < 0 || srv == IntPtr.Zero)
                {
                    return false;
                }

                // Bind state and issue the draw.
                D3D11Context_OMSetRenderTargets(_d3d11ContextPtr, 1, ref _resizeRtv, IntPtr.Zero);

                var viewport = new D3D11_VIEWPORT
                {
                    TopLeftX = 0f,
                    TopLeftY = 0f,
                    Width = dstW,
                    Height = dstH,
                    MinDepth = 0f,
                    MaxDepth = 1f,
                };
                D3D11Context_RSSetViewports(_d3d11ContextPtr, 1, ref viewport);

                // No input layout / vertex buffer needed: VS uses SV_VertexID.
                D3D11Context_IASetInputLayout(_d3d11ContextPtr, IntPtr.Zero);
                D3D11Context_IASetPrimitiveTopology(_d3d11ContextPtr, /* TRIANGLELIST */ 4);

                D3D11Context_VSSetShader(_d3d11ContextPtr, _resizeVertexShader, IntPtr.Zero, 0);
                D3D11Context_PSSetShader(_d3d11ContextPtr, _resizePixelShader, IntPtr.Zero, 0);
                D3D11Context_PSSetShaderResources(_d3d11ContextPtr, 0, 1, ref srv);
                D3D11Context_PSSetSamplers(_d3d11ContextPtr, 0, 1, ref _resizeSampler);

                D3D11Context_Draw(_d3d11ContextPtr, 3, 0);

                // Unbind SRV before releasing it so the runtime doesn't hold a stale ref.
                IntPtr nullSrv = IntPtr.Zero;
                D3D11Context_PSSetShaderResources(_d3d11ContextPtr, 0, 1, ref nullSrv);
                IntPtr nullRt = IntPtr.Zero;
                D3D11Context_OMSetRenderTargets(_d3d11ContextPtr, 1, ref nullRt, IntPtr.Zero);
            }
            finally
            {
                if (srv != IntPtr.Zero) Marshal.Release(srv);
            }

            // Copy the (small) RT into the staging texture and read it back.
            D3D11Context_CopyResource(_d3d11ContextPtr, _resizeStaging, _resizeRtTexture);

            int mapHr = D3D11Context_Map(_d3d11ContextPtr, _resizeStaging, 0, /* D3D11_MAP_READ */ 1, 0, out var mapped);
            if (mapHr < 0)
            {
                return false;
            }

            try
            {
                var rect = new Rectangle(0, 0, dstW, dstH);
                var data = destination.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte* src = (byte*)mapped.pData;
                    byte* dst = (byte*)data.Scan0;
                    int rowBytes = dstW * 4;
                    for (int y = 0; y < dstH; y++)
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
                D3D11Context_Unmap(_d3d11ContextPtr, _resizeStaging, 0);
            }

            return true;
        }

        /// <summary>
        /// Zero-copy variant of <see cref="CopyTextureToBitmapGpuResized"/>: instead of
        /// reading the resized result back to a CPU bitmap, draw it (or CopyResource it
        /// when the size matches) into the supplied destination ID3D11Texture2D and
        /// return — leaving the data on the GPU so the caller can wrap it as an
        /// IMFDXGISurfaceBuffer and submit it directly to a hardware encoder. This
        /// eliminates the ~26-37 ms / frame staging readback that currently dominates
        /// stage_wgc at 4K.
        ///
        /// <paramref name="destinationTexture"/> must:
        ///   - live on the same ID3D11Device as this capture session (ensure shared device
        ///     via <see cref="TryCreateForWindowSharingDevice"/>),
        ///   - have BindFlags including RENDER_TARGET (and SHADER_RESOURCE if the encoder
        ///     consumes it via SRV; most MFT-based encoders do),
        ///   - be format DXGI_FORMAT_B8G8R8A8_UNORM, dimensions
        ///     (<paramref name="dstWidth"/>, <paramref name="dstHeight"/>).
        /// </summary>
        public bool TryDrawCapturedFrameIntoTexture(IntPtr destinationTexture, int dstWidth, int dstHeight)
        {
            if (_disposed || destinationTexture == IntPtr.Zero) return false;

            Direct3D11CaptureFrame? frame = null;
            Direct3D11CaptureFrame? next;
            while ((next = _framePool.TryGetNextFrame()) != null)
            {
                frame?.Dispose();
                frame = next;
            }
            if (frame == null) return false;

            try
            {
                var size = frame.ContentSize;
                _lastSize = size;
                int srcWidth = Math.Max(1, size.Width);
                int srcHeight = Math.Max(1, size.Height);

                IntPtr srcTexturePtr = IntPtr.Zero;
                try
                {
                    var access = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
                    Guid texIid = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
                    int hr = access.GetInterface(ref texIid, out srcTexturePtr);
                    if (hr < 0 || srcTexturePtr == IntPtr.Zero) return false;

                    if (srcWidth == dstWidth && srcHeight == dstHeight)
                    {
                        // Same-size: a straight GPU CopyResource is far cheaper than the
                        // shader pass.
                        D3D11Context_CopyResource(_d3d11ContextPtr, destinationTexture, srcTexturePtr);
                        return true;
                    }

                    return TryRunResizeShaderToExternalTarget(srcTexturePtr, destinationTexture, dstWidth, dstHeight);
                }
                finally
                {
                    if (srcTexturePtr != IntPtr.Zero) Marshal.Release(srcTexturePtr);
                }
            }
            finally
            {
                frame.Dispose();
            }
        }

        // Like the existing GPU resize path but renders into a caller-owned RTV instead of
        // the internal _resizeRtTexture. We create a transient RTV per frame because the
        // destination texture rotates through a pool — caching one RTV per pool slot would
        // be a future optimization (and would shave ~30 µs/frame).
        private bool TryRunResizeShaderToExternalTarget(IntPtr srcTexture, IntPtr dstTexture, int dstW, int dstH)
        {
            if (!EnsureResizeShaders()) return false;

            IntPtr srv = IntPtr.Zero;
            IntPtr rtv = IntPtr.Zero;
            try
            {
                int hr = D3D11Device_CreateShaderResourceView(_d3d11DevicePtr, srcTexture, IntPtr.Zero, out srv);
                if (hr < 0 || srv == IntPtr.Zero) return false;

                hr = D3D11Device_CreateRenderTargetView(_d3d11DevicePtr, dstTexture, IntPtr.Zero, out rtv);
                if (hr < 0 || rtv == IntPtr.Zero) return false;

                D3D11Context_OMSetRenderTargets(_d3d11ContextPtr, 1, ref rtv, IntPtr.Zero);

                var viewport = new D3D11_VIEWPORT
                {
                    TopLeftX = 0f, TopLeftY = 0f,
                    Width = dstW, Height = dstH,
                    MinDepth = 0f, MaxDepth = 1f,
                };
                D3D11Context_RSSetViewports(_d3d11ContextPtr, 1, ref viewport);

                D3D11Context_IASetInputLayout(_d3d11ContextPtr, IntPtr.Zero);
                D3D11Context_IASetPrimitiveTopology(_d3d11ContextPtr, 4 /* TRIANGLELIST */);
                D3D11Context_VSSetShader(_d3d11ContextPtr, _resizeVertexShader, IntPtr.Zero, 0);
                D3D11Context_PSSetShader(_d3d11ContextPtr, _resizePixelShader, IntPtr.Zero, 0);
                D3D11Context_PSSetShaderResources(_d3d11ContextPtr, 0, 1, ref srv);
                D3D11Context_PSSetSamplers(_d3d11ContextPtr, 0, 1, ref _resizeSampler);

                D3D11Context_Draw(_d3d11ContextPtr, 3, 0);

                // Unbind so subsequent frames don't see stale views.
                IntPtr nullSrv = IntPtr.Zero;
                D3D11Context_PSSetShaderResources(_d3d11ContextPtr, 0, 1, ref nullSrv);
                IntPtr nullRt = IntPtr.Zero;
                D3D11Context_OMSetRenderTargets(_d3d11ContextPtr, 1, ref nullRt, IntPtr.Zero);
                return true;
            }
            finally
            {
                if (rtv != IntPtr.Zero) Marshal.Release(rtv);
                if (srv != IntPtr.Zero) Marshal.Release(srv);
            }
        }

        private bool EnsureResizeShaders()
        {
            if (_resizeShadersReady) return true;

            // Compile VS and PS at runtime. d3dcompiler_47.dll is part of the Windows SDK
            // redist and ships with every modern Windows install (>= 8.1).
            if (!CompileShader(ResizeVertexShaderHlsl, "main", "vs_5_0", out var vsBlob))
            {
                return false;
            }
            try
            {
                if (!CompileShader(ResizePixelShaderHlsl, "main", "ps_5_0", out var psBlob))
                {
                    return false;
                }

                try
                {
                    int hr = D3D11Device_CreateVertexShader(_d3d11DevicePtr, BlobBufferPointer(vsBlob), BlobBufferSize(vsBlob), IntPtr.Zero, out _resizeVertexShader);
                    if (hr < 0 || _resizeVertexShader == IntPtr.Zero) return false;

                    hr = D3D11Device_CreatePixelShader(_d3d11DevicePtr, BlobBufferPointer(psBlob), BlobBufferSize(psBlob), IntPtr.Zero, out _resizePixelShader);
                    if (hr < 0 || _resizePixelShader == IntPtr.Zero) return false;
                }
                finally
                {
                    if (psBlob != IntPtr.Zero) Marshal.Release(psBlob);
                }
            }
            finally
            {
                if (vsBlob != IntPtr.Zero) Marshal.Release(vsBlob);
            }

            // Linear-filtered, clamp-addressed sampler.
            var samplerDesc = new D3D11_SAMPLER_DESC
            {
                Filter = 0x15, // D3D11_FILTER_MIN_MAG_MIP_LINEAR
                AddressU = 3,  // CLAMP
                AddressV = 3,
                AddressW = 3,
                MipLODBias = 0f,
                MaxAnisotropy = 1,
                ComparisonFunc = 0,
                BorderColor0 = 0f,
                BorderColor1 = 0f,
                BorderColor2 = 0f,
                BorderColor3 = 0f,
                MinLOD = 0f,
                MaxLOD = 0f,
            };
            int sHr = D3D11Device_CreateSamplerState(_d3d11DevicePtr, ref samplerDesc, out _resizeSampler);
            if (sHr < 0 || _resizeSampler == IntPtr.Zero) return false;

            _resizeShadersReady = true;
            return true;
        }

        private bool EnsureResizeTargets(int width, int height)
        {
            if (_resizeRtTexture != IntPtr.Zero && _resizeTargetWidth == width && _resizeTargetHeight == height)
            {
                return true;
            }

            // Window resized or first time: rebuild RT + RTV + staging.
            if (_resizeRtv != IntPtr.Zero) { Marshal.Release(_resizeRtv); _resizeRtv = IntPtr.Zero; }
            if (_resizeRtTexture != IntPtr.Zero) { Marshal.Release(_resizeRtTexture); _resizeRtTexture = IntPtr.Zero; }
            if (_resizeStaging != IntPtr.Zero) { Marshal.Release(_resizeStaging); _resizeStaging = IntPtr.Zero; }

            var rtDesc = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = 87, // DXGI_FORMAT_B8G8R8A8_UNORM
                SampleDesc_Count = 1,
                SampleDesc_Quality = 0,
                Usage = 0,                // DEFAULT
                BindFlags = 0x20,         // RENDER_TARGET (BIND_SHADER_RESOURCE not needed)
                CPUAccessFlags = 0,
                MiscFlags = 0,
            };
            int hr = D3D11Device_CreateTexture2D(_d3d11DevicePtr, ref rtDesc, IntPtr.Zero, out _resizeRtTexture);
            if (hr < 0 || _resizeRtTexture == IntPtr.Zero) return false;

            hr = D3D11Device_CreateRenderTargetView(_d3d11DevicePtr, _resizeRtTexture, IntPtr.Zero, out _resizeRtv);
            if (hr < 0 || _resizeRtv == IntPtr.Zero) return false;

            var stagingDesc = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = 87,
                SampleDesc_Count = 1,
                SampleDesc_Quality = 0,
                Usage = 3,                // STAGING
                BindFlags = 0,
                CPUAccessFlags = 0x20000, // READ
                MiscFlags = 0,
            };
            hr = D3D11Device_CreateTexture2D(_d3d11DevicePtr, ref stagingDesc, IntPtr.Zero, out _resizeStaging);
            if (hr < 0 || _resizeStaging == IntPtr.Zero) return false;

            _resizeTargetWidth = width;
            _resizeTargetHeight = height;
            return true;
        }

        private void DisposeGpuResizeResources()
        {
            if (_resizeSampler != IntPtr.Zero) { try { Marshal.Release(_resizeSampler); } catch { } _resizeSampler = IntPtr.Zero; }
            if (_resizePixelShader != IntPtr.Zero) { try { Marshal.Release(_resizePixelShader); } catch { } _resizePixelShader = IntPtr.Zero; }
            if (_resizeVertexShader != IntPtr.Zero) { try { Marshal.Release(_resizeVertexShader); } catch { } _resizeVertexShader = IntPtr.Zero; }
            if (_resizeRtv != IntPtr.Zero) { try { Marshal.Release(_resizeRtv); } catch { } _resizeRtv = IntPtr.Zero; }
            if (_resizeRtTexture != IntPtr.Zero) { try { Marshal.Release(_resizeRtTexture); } catch { } _resizeRtTexture = IntPtr.Zero; }
            if (_resizeStaging != IntPtr.Zero) { try { Marshal.Release(_resizeStaging); } catch { } _resizeStaging = IntPtr.Zero; }
            _resizeShadersReady = false;
        }

        // ---------------- Shader compilation ----------------

        private static bool CompileShader(string source, string entry, string profile, out IntPtr blob)
        {
            blob = IntPtr.Zero;
            byte[] srcBytes = Encoding.ASCII.GetBytes(source);
            IntPtr errors = IntPtr.Zero;
            GCHandle pin = GCHandle.Alloc(srcBytes, GCHandleType.Pinned);
            try
            {
                int hr = D3DCompile(
                    pin.AddrOfPinnedObject(),
                    new UIntPtr((uint)srcBytes.Length),
                    null,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    entry,
                    profile,
                    /* D3DCOMPILE_OPTIMIZATION_LEVEL3 */ (1 << 15),
                    0,
                    out blob,
                    out errors);
                if (hr < 0 || blob == IntPtr.Zero)
                {
                    return false;
                }
                return true;
            }
            finally
            {
                if (errors != IntPtr.Zero) Marshal.Release(errors);
                pin.Free();
            }
        }

        private static unsafe IntPtr BlobBufferPointer(IntPtr blob)
        {
            // ID3D10Blob::GetBufferPointer = vtable slot 3 (after IUnknown 0..2). Returns void*.
            void*** vtbl = (void***)blob;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)((*vtbl)[3]);
            return fn(blob);
        }

        private static unsafe UIntPtr BlobBufferSize(IntPtr blob)
        {
            // ID3D10Blob::GetBufferSize = vtable slot 4. Returns SIZE_T (UIntPtr).
            void*** vtbl = (void***)blob;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, UIntPtr>)((*vtbl)[4]);
            return fn(blob);
        }

        [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int D3DCompile(
            IntPtr pSrcData,
            UIntPtr SrcDataSize,
            [MarshalAs(UnmanagedType.LPStr)] string? pSourceName,
            IntPtr pDefines,
            IntPtr pInclude,
            [MarshalAs(UnmanagedType.LPStr)] string pEntrypoint,
            [MarshalAs(UnmanagedType.LPStr)] string pTarget,
            uint Flags1,
            uint Flags2,
            out IntPtr ppCode,
            out IntPtr ppErrorMsgs);

        // ---------------- Vtable dispatch helpers (resize-only) ----------------

        private static unsafe int D3D11Device_CreateShaderResourceView(IntPtr device, IntPtr resource, IntPtr desc, out IntPtr srv)
        {
            // ID3D11Device::CreateShaderResourceView = slot 7.
            void*** vtbl = (void***)device;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr*, int>)((*vtbl)[7]);
            IntPtr local;
            int hr = fn(device, resource, desc, &local);
            srv = local;
            return hr;
        }

        private static unsafe int D3D11Device_CreateRenderTargetView(IntPtr device, IntPtr resource, IntPtr desc, out IntPtr rtv)
        {
            // Slot 9.
            void*** vtbl = (void***)device;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr*, int>)((*vtbl)[9]);
            IntPtr local;
            int hr = fn(device, resource, desc, &local);
            rtv = local;
            return hr;
        }

        private static unsafe int D3D11Device_CreateVertexShader(IntPtr device, IntPtr bytecode, UIntPtr bytecodeLen, IntPtr classLinkage, out IntPtr shader)
        {
            // Slot 12.
            void*** vtbl = (void***)device;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, UIntPtr, IntPtr, IntPtr*, int>)((*vtbl)[12]);
            IntPtr local;
            int hr = fn(device, bytecode, bytecodeLen, classLinkage, &local);
            shader = local;
            return hr;
        }

        private static unsafe int D3D11Device_CreatePixelShader(IntPtr device, IntPtr bytecode, UIntPtr bytecodeLen, IntPtr classLinkage, out IntPtr shader)
        {
            // Slot 15.
            void*** vtbl = (void***)device;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, UIntPtr, IntPtr, IntPtr*, int>)((*vtbl)[15]);
            IntPtr local;
            int hr = fn(device, bytecode, bytecodeLen, classLinkage, &local);
            shader = local;
            return hr;
        }

        private static unsafe int D3D11Device_CreateSamplerState(IntPtr device, ref D3D11_SAMPLER_DESC desc, out IntPtr sampler)
        {
            // Slot 23.
            void*** vtbl = (void***)device;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, D3D11_SAMPLER_DESC*, IntPtr*, int>)((*vtbl)[23]);
            fixed (D3D11_SAMPLER_DESC* d = &desc)
            {
                IntPtr local;
                int hr = fn(device, d, &local);
                sampler = local;
                return hr;
            }
        }

        private static unsafe void D3D11Context_VSSetShader(IntPtr context, IntPtr shader, IntPtr classInstances, uint numClassInstances)
        {
            // Slot 11.
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, uint, void>)((*vtbl)[11]);
            fn(context, shader, classInstances, numClassInstances);
        }

        private static unsafe void D3D11Context_PSSetShader(IntPtr context, IntPtr shader, IntPtr classInstances, uint numClassInstances)
        {
            // Slot 9.
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, uint, void>)((*vtbl)[9]);
            fn(context, shader, classInstances, numClassInstances);
        }

        private static unsafe void D3D11Context_PSSetShaderResources(IntPtr context, uint startSlot, uint numViews, ref IntPtr srv)
        {
            // Slot 8.
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)((*vtbl)[8]);
            fixed (IntPtr* p = &srv)
            {
                fn(context, startSlot, numViews, p);
            }
        }

        private static unsafe void D3D11Context_PSSetSamplers(IntPtr context, uint startSlot, uint numSamplers, ref IntPtr sampler)
        {
            // Slot 10.
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)((*vtbl)[10]);
            fixed (IntPtr* p = &sampler)
            {
                fn(context, startSlot, numSamplers, p);
            }
        }

        private static unsafe void D3D11Context_IASetInputLayout(IntPtr context, IntPtr layout)
        {
            // Slot 17.
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)((*vtbl)[17]);
            fn(context, layout);
        }

        private static unsafe void D3D11Context_IASetPrimitiveTopology(IntPtr context, uint topology)
        {
            // Slot 24.
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, void>)((*vtbl)[24]);
            fn(context, topology);
        }

        private static unsafe void D3D11Context_OMSetRenderTargets(IntPtr context, uint numViews, ref IntPtr rtv, IntPtr dsv)
        {
            // Slot 33.
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, IntPtr, void>)((*vtbl)[33]);
            fixed (IntPtr* p = &rtv)
            {
                fn(context, numViews, p, dsv);
            }
        }

        private static unsafe void D3D11Context_RSSetViewports(IntPtr context, uint numViewports, ref D3D11_VIEWPORT viewport)
        {
            // Slot 44.
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, D3D11_VIEWPORT*, void>)((*vtbl)[44]);
            fixed (D3D11_VIEWPORT* p = &viewport)
            {
                fn(context, numViewports, p);
            }
        }

        private static unsafe void D3D11Context_Draw(IntPtr context, uint vertexCount, uint startVertex)
        {
            // Slot 13.
            void*** vtbl = (void***)context;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, void>)((*vtbl)[13]);
            fn(context, vertexCount, startVertex);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D11_SAMPLER_DESC
        {
            public uint Filter;
            public uint AddressU;
            public uint AddressV;
            public uint AddressW;
            public float MipLODBias;
            public uint MaxAnisotropy;
            public uint ComparisonFunc;
            public float BorderColor0;
            public float BorderColor1;
            public float BorderColor2;
            public float BorderColor3;
            public float MinLOD;
            public float MaxLOD;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D11_VIEWPORT
        {
            public float TopLeftX;
            public float TopLeftY;
            public float Width;
            public float Height;
            public float MinDepth;
            public float MaxDepth;
        }

        // Public mirror of the private D3D11_TEXTURE2D_DESC defined in WgcWindowCapture.cs,
        // exposed solely so InternalScreenRecorder's zero-copy texture pool can describe the
        // BGRA RT-backed pool textures it needs without redefining the layout.
        [StructLayout(LayoutKind.Sequential)]
        public struct D3D11_TEXTURE2D_DESC_PUBLIC
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

        // Static helper so external callers (specifically InternalScreenRecorder's zero-copy
        // pool) can allocate D3D11 textures on the same device without taking on the full
        // vtable-dispatch dance themselves.
        public static unsafe int CreateTexture2DPublic(IntPtr device, ref D3D11_TEXTURE2D_DESC_PUBLIC desc, out IntPtr texture)
        {
            void*** vtbl = (void***)device;
            var fn = (delegate* unmanaged[Stdcall]<IntPtr, D3D11_TEXTURE2D_DESC_PUBLIC*, IntPtr, IntPtr*, int>)((*vtbl)[5]);
            fixed (D3D11_TEXTURE2D_DESC_PUBLIC* descPtr = &desc)
            {
                IntPtr local;
                int hr = fn(device, descPtr, IntPtr.Zero, &local);
                texture = local;
                return hr;
            }
        }
    }
}
