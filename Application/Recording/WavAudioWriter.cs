#nullable enable

using System;
using System.Buffers.Binary;
using System.IO;

namespace ToNRoundCounter.Application.Recording
{
    /// <summary>
    /// Minimal PCM WAV (RIFF) writer. Produces a canonical 44-byte header followed by the raw
    /// interleaved PCM bytes handed to <see cref="Write(ReadOnlySpan{byte}, int)"/>. On
    /// <see cref="Dispose"/> the RIFF and data chunk sizes are patched in place so the file is
    /// valid even if audio capture ends mid-frame.
    /// </summary>
    internal sealed class WavAudioWriter : IDisposable
    {
        private readonly FileStream _stream;
        private readonly AudioFormat _format;
        private long _dataBytes;
        private bool _disposed;

        public WavAudioWriter(string path, AudioFormat format)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            _format = format;
            _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            Path = path;
            WriteHeaderPlaceholder();
        }

        public string Path { get; }

        public void Write(ReadOnlySpan<byte> data, int frames)
        {
            if (_disposed || data.IsEmpty || frames <= 0)
            {
                return;
            }

            _stream.Write(data);
            _dataBytes += data.Length;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                PatchHeader();
            }
            finally
            {
                _stream.Dispose();
            }
        }

        // Offsets of the fields that are patched on Dispose. PCM and IEEE-float layouts
        // differ (float requires an extra cbSize + fact chunk), so _dataSizeOffset is
        // resolved at header-write time.
        private long _riffSizeOffset;
        private long _factSampleCountOffset; // -1 when absent (PCM path)
        private long _dataSizeOffset;

        private void WriteHeaderPlaceholder()
        {
            // PCM path  : 44-byte canonical header (fmt chunk size = 16, no fact chunk).
            // Float path: 58-byte extended header (fmt chunk size = 18 with cbSize=0, plus
            //             a 12-byte "fact" chunk carrying the total sample-frame count).
            //             The fact chunk is REQUIRED by the WAV spec for any non-PCM format
            //             and ffmpeg refuses to decode float WAVs that omit it, which was
            //             producing silent output after the float32 switch.
            bool isFloat = _format.IsFloat;
            short formatTag = (short)(isFloat ? 3 /* WAVE_FORMAT_IEEE_FLOAT */ : 1 /* WAVE_FORMAT_PCM */);
            short fmtChunkPayload = (short)(isFloat ? 18 : 16);

            int headerLen = isFloat ? 58 : 44;
            Span<byte> header = stackalloc byte[58];
            header = header.Slice(0, headerLen);

            int p = 0;
            // RIFF
            header[p++] = (byte)'R'; header[p++] = (byte)'I'; header[p++] = (byte)'F'; header[p++] = (byte)'F';
            _riffSizeOffset = p;
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(p, 4), 0); p += 4; // file size - 8 (patched)
            header[p++] = (byte)'W'; header[p++] = (byte)'A'; header[p++] = (byte)'V'; header[p++] = (byte)'E';

            // fmt chunk
            header[p++] = (byte)'f'; header[p++] = (byte)'m'; header[p++] = (byte)'t'; header[p++] = (byte)' ';
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(p, 4), fmtChunkPayload); p += 4;
            BinaryPrimitives.WriteInt16LittleEndian(header.Slice(p, 2), formatTag); p += 2;
            BinaryPrimitives.WriteInt16LittleEndian(header.Slice(p, 2), (short)_format.Channels); p += 2;
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(p, 4), _format.SampleRate); p += 4;
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(p, 4), _format.BytesPerSecond); p += 4;
            BinaryPrimitives.WriteInt16LittleEndian(header.Slice(p, 2), (short)_format.BlockAlign); p += 2;
            BinaryPrimitives.WriteInt16LittleEndian(header.Slice(p, 2), (short)_format.BitsPerSample); p += 2;
            if (isFloat)
            {
                // cbSize = 0 (no extended format info); required when fmt chunk payload is 18.
                BinaryPrimitives.WriteInt16LittleEndian(header.Slice(p, 2), 0); p += 2;

                // fact chunk (mandatory for non-PCM formats)
                header[p++] = (byte)'f'; header[p++] = (byte)'a'; header[p++] = (byte)'c'; header[p++] = (byte)'t';
                BinaryPrimitives.WriteInt32LittleEndian(header.Slice(p, 4), 4); p += 4;
                _factSampleCountOffset = p;
                BinaryPrimitives.WriteInt32LittleEndian(header.Slice(p, 4), 0); p += 4; // sample-frame count (patched)
            }
            else
            {
                _factSampleCountOffset = -1;
            }

            // data chunk header
            header[p++] = (byte)'d'; header[p++] = (byte)'a'; header[p++] = (byte)'t'; header[p++] = (byte)'a';
            _dataSizeOffset = p;
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(p, 4), 0); p += 4; // data size (patched)

            _stream.Write(header);
        }

        private void PatchHeader()
        {
            try
            {
                _stream.Flush();
                long fileSize = _stream.Length;
                long riffSize = fileSize - 8;
                long dataSize = _dataBytes;

                // Guard against overflow - WAV RIFF uses uint32 sizes.
                if (riffSize > uint.MaxValue) riffSize = uint.MaxValue;
                if (dataSize > uint.MaxValue) dataSize = uint.MaxValue;

                Span<byte> u32 = stackalloc byte[4];

                _stream.Seek(_riffSizeOffset, SeekOrigin.Begin);
                BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)riffSize);
                _stream.Write(u32);

                if (_factSampleCountOffset >= 0 && _format.BlockAlign > 0)
                {
                    long sampleFrames = _dataBytes / _format.BlockAlign;
                    if (sampleFrames > uint.MaxValue) sampleFrames = uint.MaxValue;
                    _stream.Seek(_factSampleCountOffset, SeekOrigin.Begin);
                    BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)sampleFrames);
                    _stream.Write(u32);
                }

                _stream.Seek(_dataSizeOffset, SeekOrigin.Begin);
                BinaryPrimitives.WriteUInt32LittleEndian(u32, (uint)dataSize);
                _stream.Write(u32);

                _stream.Flush();
            }
            catch
            {
                // Best effort; if we can't patch the header the WAV will still contain all the
                // captured samples (most players tolerate a zero-length header by scanning the
                // file). Silent swallow is acceptable here.
            }
        }
    }
}
