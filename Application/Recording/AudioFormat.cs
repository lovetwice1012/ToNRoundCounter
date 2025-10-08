#nullable enable

using System;

namespace ToNRoundCounter.Application.Recording
{
    internal readonly struct AudioFormat
    {
        public AudioFormat(int sampleRate, int channels, int bitsPerSample, int blockAlign, Guid subFormat, bool isFloat, int validBitsPerSample, uint channelMask)
        {
            SampleRate = sampleRate;
            Channels = channels;
            BitsPerSample = bitsPerSample;
            BlockAlign = blockAlign;
            SubFormat = subFormat;
            IsFloat = isFloat;
            ValidBitsPerSample = validBitsPerSample;
            ChannelMask = channelMask;
        }

        public int SampleRate { get; }

        public int Channels { get; }

        public int BitsPerSample { get; }

        public int BlockAlign { get; }

        public Guid SubFormat { get; }

        public bool IsFloat { get; }

        public int ValidBitsPerSample { get; }

        public uint ChannelMask { get; }

        public int BytesPerSecond => SampleRate * BlockAlign;

        public long FramesToDurationHns(int frames)
        {
            if (SampleRate <= 0)
            {
                return 0;
            }

            return (long)Math.Round(frames * 10_000_000d / SampleRate);
        }
    }
}
