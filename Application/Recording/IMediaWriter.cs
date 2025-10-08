#nullable enable

using System;
using System.Drawing;

namespace ToNRoundCounter.Application.Recording
{
    internal interface IMediaWriter : IDisposable
    {
        bool IsHardwareAccelerated { get; }

        bool SupportsAudio { get; }

        void WriteVideoFrame(Bitmap frame);

        void WriteAudioSample(ReadOnlySpan<byte> data, int frames);

        void CompleteAudio();
    }
}
