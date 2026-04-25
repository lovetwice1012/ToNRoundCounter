using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace ToNRoundCounter.Infrastructure.Services
{
    /// <summary>
    /// 10-band peaking-EQ ISampleProvider built on NAudio's BiQuadFilter.
    /// Center frequencies follow the ISO standard 1/1-octave grid.
    /// </summary>
    public sealed class EqualizerSampleProvider : ISampleProvider
    {
        public static readonly float[] Frequencies = { 31f, 62f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f };
        public const int BandCount = 10;

        private readonly ISampleProvider _source;
        private readonly int _channels;
        private readonly int _sampleRate;
        private readonly float _q = 1.0f;
        // [channel][band] – swapped atomically via reference replacement.
        private volatile BiQuadFilter[][] _filters;
        private volatile bool _enabled;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public EqualizerSampleProvider(ISampleProvider source, float[] gainsDb, bool enabled)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            _source = source;
            _channels = source.WaveFormat.Channels;
            _sampleRate = source.WaveFormat.SampleRate;
            _filters = BuildFilters(NormalizeGains(gainsDb));
            _enabled = enabled;
        }

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public void UpdateGains(float[] gainsDb, bool enabled)
        {
            float[] g = NormalizeGains(gainsDb);
            _filters = BuildFilters(g);
            _enabled = enabled;
        }

        private static float[] NormalizeGains(float[] gainsDb)
        {
            float[] g = new float[BandCount];
            if (gainsDb != null)
            {
                int n = Math.Min(BandCount, gainsDb.Length);
                for (int i = 0; i < n; i++)
                {
                    float v = gainsDb[i];
                    if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
                    if (v < -24f) v = -24f;
                    if (v > 24f) v = 24f;
                    g[i] = v;
                }
            }
            return g;
        }

        private BiQuadFilter[][] BuildFilters(float[] gainsDb)
        {
            var arr = new BiQuadFilter[_channels][];
            for (int ch = 0; ch < _channels; ch++)
            {
                arr[ch] = new BiQuadFilter[BandCount];
                for (int b = 0; b < BandCount; b++)
                {
                    arr[ch][b] = BiQuadFilter.PeakingEQ(_sampleRate, Frequencies[b], _q, gainsDb[b]);
                }
            }
            return arr;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            if (!_enabled || read <= 0) return read;
            var filters = _filters;
            if (filters == null || filters.Length == 0) return read;
            int channels = _channels;
            for (int i = 0; i < read; i++)
            {
                int ch = i % channels;
                var bandFilters = filters[ch];
                float v = buffer[offset + i];
                for (int b = 0; b < bandFilters.Length; b++)
                {
                    v = bandFilters[b].Transform(v);
                }
                buffer[offset + i] = v;
            }
            return read;
        }
    }
}
