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

        // Variant that pins each sample's presentation time to wall-clock elapsed ticks instead
        // of a running frame counter. When the encode pipeline can't sustain the requested fps,
        // this keeps video/audio in sync (and produces a low-fps file) rather than producing a
        // "N x speed" file whose duration is shorter than the wall clock.
        void WriteVideoFrame(Bitmap frame, long presentationTimeTicks);

        void WriteAudioSample(ReadOnlySpan<byte> data, int frames);

        void CompleteAudio();
    }
}
