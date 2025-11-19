#nullable enable

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ToNRoundCounter.Application.Recording
{
    internal sealed class SimpleAviWriter : IMediaWriter
    {
        private readonly int _width;
        private readonly int _height;
        private readonly int _frameSize;
        private IntPtr _fileHandle;
        private IntPtr _streamHandle;
        private int _frameIndex;
        private bool _disposed;
        private byte[]? _copyBuffer;

        private const int OF_WRITE = 0x00000001;
        private const int OF_CREATE = 0x00001000;
        private const uint StreamTypeVIDEO = 0x73646976; // 'vids'
        private const uint BI_RGB = 0;

        public SimpleAviWriter(string path, int width, int height, int frameRate)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            if (frameRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameRate));
            }

            _width = width;
            _height = height;
            _frameSize = width * height * 4;

            AVIFileInit();

            try
            {
                int result = AVIFileOpen(out _fileHandle, path, OF_WRITE | OF_CREATE, IntPtr.Zero);
                if (result != 0)
                {
                    throw new InvalidOperationException($"AVIFileOpen failed with code {result}.");
                }

                var info = new AVISTREAMINFO
                {
                    fccType = StreamTypeVIDEO,
                    fccHandler = 0,
                    dwScale = 1,
                    dwRate = (uint)frameRate,
                    dwSuggestedBufferSize = (uint)_frameSize,
                    rcFrame = new RECT { Left = 0, Top = 0, Right = width, Bottom = height },
                    szName = new ushort[64]
                };

                result = AVIFileCreateStream(_fileHandle, out _streamHandle, ref info);
                if (result != 0)
                {
                    throw new InvalidOperationException($"AVIFileCreateStream failed with code {result}.");
                }

                var format = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB,
                    biSizeImage = (uint)_frameSize
                };

                result = AVIStreamSetFormat(_streamHandle, 0, ref format, Marshal.SizeOf<BITMAPINFOHEADER>());
                if (result != 0)
                {
                    throw new InvalidOperationException($"AVIStreamSetFormat failed with code {result}.");
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public bool IsHardwareAccelerated => false;

        public bool SupportsAudio => false;

        public void WriteVideoFrame(Bitmap frame)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SimpleAviWriter));
            }

            if (frame.Width != _width || frame.Height != _height)
            {
                throw new InvalidOperationException("Frame size does not match recorder dimensions.");
            }

            var rect = new Rectangle(0, 0, _width, _height);
            var data = frame.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                WriteFrameInternal(data.Scan0, data.Stride);
            }
            finally
            {
                frame.UnlockBits(data);
            }
        }

        public void WriteAudioSample(ReadOnlySpan<byte> data, int frames)
        {
            throw new NotSupportedException("Audio capture is not supported for AVI recordings using SimpleAviWriter. " +
                "Please use MP4, WebM, or MKV format with MediaFoundation for audio recording support.");
        }

        public void CompleteAudio()
        {
        }

        private void WriteFrameInternal(IntPtr buffer, int stride)
        {
            int expectedStride = _width * 4;
            IntPtr dataPointer = buffer;
            int dataSize = _frameSize;
            GCHandle handle = default;

            try
            {
                if (stride != expectedStride)
                {
                    EnsureCopyBuffer();
                    for (int y = 0; y < _height; y++)
                    {
                        IntPtr sourceRow = IntPtr.Add(buffer, y * stride);
                        Marshal.Copy(sourceRow, _copyBuffer!, y * expectedStride, expectedStride);
                    }

                    handle = GCHandle.Alloc(_copyBuffer!, GCHandleType.Pinned);
                    dataPointer = handle.AddrOfPinnedObject();
                    dataSize = _copyBuffer!.Length;
                }

                int result = AVIStreamWrite(_streamHandle, _frameIndex, 1, dataPointer, dataSize, 0, IntPtr.Zero, IntPtr.Zero);
                if (result != 0)
                {
                    throw new InvalidOperationException($"AVIStreamWrite failed with code {result}.");
                }

                _frameIndex++;
            }
            finally
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }

        private void EnsureCopyBuffer()
        {
            int expectedStride = _width * 4;
            if (_copyBuffer == null || _copyBuffer.Length != expectedStride * _height)
            {
                _copyBuffer = new byte[expectedStride * _height];
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_streamHandle != IntPtr.Zero)
            {
                AVIStreamRelease(_streamHandle);
                _streamHandle = IntPtr.Zero;
            }

            if (_fileHandle != IntPtr.Zero)
            {
                AVIFileRelease(_fileHandle);
                _fileHandle = IntPtr.Zero;
            }

            AVIFileExit();
            _disposed = true;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AVISTREAMINFO
        {
            public uint fccType;
            public uint fccHandler;
            public uint dwFlags;
            public uint dwCaps;
            public ushort wPriority;
            public ushort wLanguage;
            public uint dwScale;
            public uint dwRate;
            public uint dwStart;
            public uint dwLength;
            public uint dwInitialFrames;
            public uint dwSuggestedBufferSize;
            public uint dwQuality;
            public uint dwSampleSize;
            public RECT rcFrame;
            public uint dwEditCount;
            public uint dwFormatChangeCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public ushort[] szName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [DllImport("avifil32.dll")]
        private static extern void AVIFileInit();

        [DllImport("avifil32.dll")]
        private static extern int AVIFileOpen(out IntPtr ppfile, string szFile, int mode, IntPtr pclsidHandler);

        [DllImport("avifil32.dll")]
        private static extern int AVIFileCreateStream(IntPtr pfile, out IntPtr ppavi, ref AVISTREAMINFO psi);

        [DllImport("avifil32.dll")]
        private static extern int AVIStreamSetFormat(IntPtr pavi, int lPos, ref BITMAPINFOHEADER lpFormat, int cbFormat);

        [DllImport("avifil32.dll")]
        private static extern int AVIStreamWrite(IntPtr pavi, int lStart, int lSamples, IntPtr lpBuffer, int cbBuffer, int dwFlags, IntPtr plSampWritten, IntPtr plBytesWritten);

        [DllImport("avifil32.dll")]
        private static extern int AVIStreamRelease(IntPtr pavi);

        [DllImport("avifil32.dll")]
        private static extern int AVIFileRelease(IntPtr pfile);

        [DllImport("avifil32.dll")]
        private static extern void AVIFileExit();
    }
}
