#nullable enable

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ToNRoundCounter.Application.Recording
{
    internal sealed class GifFrameWriter : IMediaWriter
    {
        private readonly GifBitmapEncoder _encoder = new GifBitmapEncoder();
        private readonly string _path;
        private readonly object _sync = new object();
        private readonly ushort _frameDelay;
        private readonly int _width;
        private readonly int _height;
        private bool _disposed;

        public GifFrameWriter(string path, int width, int height, int frameRate)
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

            _path = path;
            _width = width;
            _height = height;
            _frameDelay = (ushort)Math.Max(1, Math.Round(100.0 / Math.Max(1, frameRate)));
        }

        public bool IsHardwareAccelerated => false;

        public bool SupportsAudio => false;

        public void WriteVideoFrame(Bitmap frame)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(GifFrameWriter));
                }

                if (frame.Width != _width || frame.Height != _height)
                {
                    throw new InvalidOperationException("Frame size does not match recorder dimensions.");
                }

                using var clone = (Bitmap)frame.Clone();
                var bitmapFrame = CreateBitmapFrame(clone, _frameDelay);
                _encoder.Frames.Add(bitmapFrame);
            }
        }

        public void WriteAudioSample(ReadOnlySpan<byte> data, int frames)
        {
            throw new NotSupportedException("Audio capture is not supported for GIF recordings due to format limitations. " +
                "Please use MP4, WebM, or MKV format for audio recording support.");
        }

        public void CompleteAudio()
        {
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                if (_encoder.Frames.Count == 0)
                {
                    using var fallback = new Bitmap(_width, _height);
                    using (var graphics = Graphics.FromImage(fallback))
                    {
                        graphics.Clear(Color.Black);
                    }

                    var fallbackFrame = CreateBitmapFrame(fallback, _frameDelay);
                    _encoder.Frames.Add(fallbackFrame);
                }

                using (var stream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    _encoder.Save(stream);
                }

                _disposed = true;
            }
        }

        private static BitmapFrame CreateBitmapFrame(Bitmap bitmap, ushort frameDelay)
        {
            var source = CreateBitmapSource(bitmap);
            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery("/grctlext/Delay", frameDelay);
            metadata.SetQuery("/grctlext/Disposal", (byte)2);
            metadata.SetQuery("/imgdesc/Left", (ushort)0);
            metadata.SetQuery("/imgdesc/Top", (ushort)0);
            metadata.SetQuery("/imgdesc/Width", (ushort)bitmap.Width);
            metadata.SetQuery("/imgdesc/Height", (ushort)bitmap.Height);
            return BitmapFrame.Create(source, null, metadata, null);
        }

        private static BitmapSource CreateBitmapSource(Bitmap bitmap)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
