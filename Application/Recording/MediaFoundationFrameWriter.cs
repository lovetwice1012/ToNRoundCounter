#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using RecordingCodecInfo = ToNRoundCounter.Application.AutoRecordingService.RecordingCodecInfo;

namespace ToNRoundCounter.Application.Recording
{
    internal sealed class MediaFoundationFrameWriter : IMediaWriter
    {
        private static readonly Dictionary<string, Dictionary<string, FormatDescriptor>> FormatMap = new Dictionary<string, Dictionary<string, FormatDescriptor>>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "mp4",
                new Dictionary<string, FormatDescriptor>(StringComparer.OrdinalIgnoreCase)
                {
                    { "h264", new FormatDescriptor("h264", "AutoRecording_CodecOption_H264", MediaFoundationInterop.MFVideoFormat_H264, MediaFoundationInterop.MFTranscodeContainerType_MPEG4, MediaFoundationInterop.MFAudioFormat_AAC, true, 0, 192000) },
                    { "hevc", new FormatDescriptor("hevc", "AutoRecording_CodecOption_HEVC", MediaFoundationInterop.MFVideoFormat_HEVC, MediaFoundationInterop.MFTranscodeContainerType_MPEG4, MediaFoundationInterop.MFAudioFormat_AAC, true, 0, 192000) },
                    { "av1", new FormatDescriptor("av1", "AutoRecording_CodecOption_AV1", MediaFoundationInterop.MFVideoFormat_AV1, MediaFoundationInterop.MFTranscodeContainerType_MPEG4, MediaFoundationInterop.MFAudioFormat_AAC, true, 0, 192000) },
                    { "vp9", new FormatDescriptor("vp9", "AutoRecording_CodecOption_VP9", MediaFoundationInterop.MFVideoFormat_VP9, MediaFoundationInterop.MFTranscodeContainerType_MPEG4, MediaFoundationInterop.MFAudioFormat_AAC, true, 0, 192000) },
                }
            },
            {
                "mov",
                new Dictionary<string, FormatDescriptor>(StringComparer.OrdinalIgnoreCase)
                {
                    { "h264", new FormatDescriptor("h264", "AutoRecording_CodecOption_H264", MediaFoundationInterop.MFVideoFormat_H264, MediaFoundationInterop.MFTranscodeContainerType_MPEG4, MediaFoundationInterop.MFAudioFormat_AAC, true, 0, 192000) },
                    { "hevc", new FormatDescriptor("hevc", "AutoRecording_CodecOption_HEVC", MediaFoundationInterop.MFVideoFormat_HEVC, MediaFoundationInterop.MFTranscodeContainerType_MPEG4, MediaFoundationInterop.MFAudioFormat_AAC, true, 0, 192000) },
                    { "av1", new FormatDescriptor("av1", "AutoRecording_CodecOption_AV1", MediaFoundationInterop.MFVideoFormat_AV1, MediaFoundationInterop.MFTranscodeContainerType_MPEG4, MediaFoundationInterop.MFAudioFormat_AAC, true, 0, 192000) },
                    { "vp9", new FormatDescriptor("vp9", "AutoRecording_CodecOption_VP9", MediaFoundationInterop.MFVideoFormat_VP9, MediaFoundationInterop.MFTranscodeContainerType_MPEG4, MediaFoundationInterop.MFAudioFormat_AAC, true, 0, 192000) },
                }
            },
            {
                "mkv",
                new Dictionary<string, FormatDescriptor>(StringComparer.OrdinalIgnoreCase)
                {
                    { "h264", new FormatDescriptor("h264", "AutoRecording_CodecOption_H264", MediaFoundationInterop.MFVideoFormat_H264, MediaFoundationInterop.MFTranscodeContainerType_MPEG4, MediaFoundationInterop.MFAudioFormat_AAC, true, 0, 192000) },
                    { "hevc", new FormatDescriptor("hevc", "AutoRecording_CodecOption_HEVC", MediaFoundationInterop.MFVideoFormat_HEVC, MediaFoundationInterop.MFTranscodeContainerType_MPEG4, MediaFoundationInterop.MFAudioFormat_AAC, true, 0, 192000) },
                    { "av1", new FormatDescriptor("av1", "AutoRecording_CodecOption_AV1", MediaFoundationInterop.MFVideoFormat_AV1, MediaFoundationInterop.MFTranscodeContainerType_MPEG4, MediaFoundationInterop.MFAudioFormat_AAC, true, 0, 192000) },
                    { "vp9", new FormatDescriptor("vp9", "AutoRecording_CodecOption_VP9", MediaFoundationInterop.MFVideoFormat_VP9, MediaFoundationInterop.MFTranscodeContainerType_MPEG4, MediaFoundationInterop.MFAudioFormat_AAC, true, 0, 192000) },
                }
            },
            {
                "flv",
                new Dictionary<string, FormatDescriptor>(StringComparer.OrdinalIgnoreCase)
                {
                    { "h264", new FormatDescriptor("h264", "AutoRecording_CodecOption_H264", MediaFoundationInterop.MFVideoFormat_H264, MediaFoundationInterop.MFTranscodeContainerType_MPEG4, MediaFoundationInterop.MFAudioFormat_AAC, true, 0, 160000) },
                }
            },
            {
                "wmv",
                new Dictionary<string, FormatDescriptor>(StringComparer.OrdinalIgnoreCase)
                {
                    { "wmv3", new FormatDescriptor("wmv3", "AutoRecording_CodecOption_WMV3", MediaFoundationInterop.MFVideoFormat_WMV3, MediaFoundationInterop.MFTranscodeContainerType_ASF, MediaFoundationInterop.MFAudioFormat_WMAudioV9, true, 0, 192000) },
                }
            },
            {
                "asf",
                new Dictionary<string, FormatDescriptor>(StringComparer.OrdinalIgnoreCase)
                {
                    { "wmv3", new FormatDescriptor("wmv3", "AutoRecording_CodecOption_WMV3", MediaFoundationInterop.MFVideoFormat_WMV3, MediaFoundationInterop.MFTranscodeContainerType_ASF, MediaFoundationInterop.MFAudioFormat_WMAudioV9, true, 0, 192000) },
                }
            },
            {
                "mpg",
                new Dictionary<string, FormatDescriptor>(StringComparer.OrdinalIgnoreCase)
                {
                    { "mpeg2", new FormatDescriptor("mpeg2", "AutoRecording_CodecOption_MPEG2", MediaFoundationInterop.MFVideoFormat_MPEG2, MediaFoundationInterop.MFTranscodeContainerType_MPEG2, MediaFoundationInterop.MFAudioFormat_MPEG, true, 0, 224000) },
                }
            },
            {
                "vob",
                new Dictionary<string, FormatDescriptor>(StringComparer.OrdinalIgnoreCase)
                {
                    { "mpeg2", new FormatDescriptor("mpeg2", "AutoRecording_CodecOption_MPEG2", MediaFoundationInterop.MFVideoFormat_MPEG2, MediaFoundationInterop.MFTranscodeContainerType_MPEG2, MediaFoundationInterop.MFAudioFormat_MPEG, true, 0, 224000) },
                }
            },
        };

        public readonly struct HardwareAdapterDescriptor
        {
            public HardwareAdapterDescriptor(int luidHighPart, uint luidLowPart, string description)
            {
                LuidHighPart = luidHighPart;
                LuidLowPart = luidLowPart;
                Description = description;
            }

            public int LuidHighPart { get; }

            public uint LuidLowPart { get; }

            public string Description { get; }
        }

        public static IReadOnlyList<HardwareAdapterDescriptor> GetHardwareAdapterDescriptors()
        {
            return HardwareDeviceContext.GetAdapterDescriptors();
        }

        public static IReadOnlyList<RecordingCodecInfo> GetCodecInfo(string extension)
        {
            if (!FormatMap.TryGetValue(extension, out var codecs))
            {
                return Array.Empty<RecordingCodecInfo>();
            }

            var list = new List<RecordingCodecInfo>(codecs.Count);
            foreach (var descriptor in codecs.Values)
            {
                list.Add(new RecordingCodecInfo(descriptor.CodecId, descriptor.LocalizationKey, descriptor.SupportsAudio));
            }

            return list;
        }

        private readonly MediaFoundationInterop.IMFSinkWriter _sinkWriter = null!;
        private readonly int _streamIndex;
        private readonly int _width;
        private readonly int _height;
        private readonly int _targetStride;
        private readonly long _frameRate;
        private readonly long _baseFrameDuration;
        private readonly long _durationRemainder;
        private long _timestamp;
        private long _durationAccumulator;
        private bool _disposed;
        private readonly bool _isHardwareAccelerated;
        private readonly HardwareDeviceContext? _hardwareContext;
        private readonly bool _supportsAudio;
        private readonly int? _audioStreamIndex;
        private readonly AudioFormat? _audioFormat;
        private long _audioTimestamp;
        private readonly object _audioSync = new object();

        private MediaFoundationFrameWriter(string extension, string codecId, string path, int width, int height, int frameRate, FormatDescriptor descriptor, AudioFormat? audioFormat, int videoBitrate, int audioBitrate, HardwareEncoderSelection selection)
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
            _targetStride = width * 4;
            _frameRate = Math.Max(1, frameRate);
            _baseFrameDuration = 10_000_000L / _frameRate;
            _durationRemainder = 10_000_000L % _frameRate;
            int effectiveVideoBitrate = ResolveVideoBitrate(width, height, frameRate, videoBitrate, descriptor.DefaultVideoBitrate);

            MediaFoundationInterop.AddRef();

            bool initialized = false;
            MediaFoundationInterop.IMFMediaType? outputType = null;
            MediaFoundationInterop.IMFMediaType? inputType = null;
            MediaFoundationInterop.IMFSinkWriter? writer = null;
            HardwareDeviceContext? hardwareContext = null;
            int? audioStreamIndex = null;
            AudioFormat? configuredAudioFormat = null;

            try
            {
                writer = CreateSinkWriter(path, descriptor, selection, out _isHardwareAccelerated, out hardwareContext);

                outputType = MediaFoundationInterop.CreateMediaType();
                MediaFoundationInterop.CheckHr(outputType.SetGUID(MediaFoundationInterop.MF_MT_MAJOR_TYPE, MediaFoundationInterop.MFMediaType_Video), "MF_MT_MAJOR_TYPE");
                MediaFoundationInterop.CheckHr(outputType.SetGUID(MediaFoundationInterop.MF_MT_SUBTYPE, descriptor.VideoSubtype), "MF_MT_SUBTYPE");
                MediaFoundationInterop.SetAttributeSize(outputType, MediaFoundationInterop.MF_MT_FRAME_SIZE, width, height);
                MediaFoundationInterop.SetAttributeRatio(outputType, MediaFoundationInterop.MF_MT_FRAME_RATE, frameRate, 1);
                MediaFoundationInterop.SetAttributeRatio(outputType, MediaFoundationInterop.MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
                MediaFoundationInterop.CheckHr(outputType.SetUINT32(MediaFoundationInterop.MF_MT_INTERLACE_MODE, (int)MediaFoundationInterop.MFVideoInterlaceMode.Progressive), "MF_MT_INTERLACE_MODE");
                MediaFoundationInterop.CheckHr(outputType.SetUINT32(MediaFoundationInterop.MF_MT_AVG_BITRATE, effectiveVideoBitrate), "MF_MT_AVG_BITRATE");
                MediaFoundationInterop.CheckHr(outputType.SetUINT32(MediaFoundationInterop.MF_MT_ALL_SAMPLES_INDEPENDENT, 1), "MF_MT_ALL_SAMPLES_INDEPENDENT");

                MediaFoundationInterop.CheckHr(writer.AddStream(outputType, out _streamIndex), "IMFSinkWriter.AddStream");

                inputType = MediaFoundationInterop.CreateMediaType();
                MediaFoundationInterop.CheckHr(inputType.SetGUID(MediaFoundationInterop.MF_MT_MAJOR_TYPE, MediaFoundationInterop.MFMediaType_Video), "Input MF_MT_MAJOR_TYPE");
                MediaFoundationInterop.CheckHr(inputType.SetGUID(MediaFoundationInterop.MF_MT_SUBTYPE, MediaFoundationInterop.MFVideoFormat_RGB32), "Input MF_MT_SUBTYPE");
                MediaFoundationInterop.SetAttributeSize(inputType, MediaFoundationInterop.MF_MT_FRAME_SIZE, width, height);
                MediaFoundationInterop.SetAttributeRatio(inputType, MediaFoundationInterop.MF_MT_FRAME_RATE, frameRate, 1);
                MediaFoundationInterop.SetAttributeRatio(inputType, MediaFoundationInterop.MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
                MediaFoundationInterop.CheckHr(inputType.SetUINT32(MediaFoundationInterop.MF_MT_INTERLACE_MODE, (int)MediaFoundationInterop.MFVideoInterlaceMode.Progressive), "Input MF_MT_INTERLACE_MODE");
                MediaFoundationInterop.CheckHr(inputType.SetUINT32(MediaFoundationInterop.MF_MT_DEFAULT_STRIDE, _targetStride), "Input MF_MT_DEFAULT_STRIDE");
                MediaFoundationInterop.CheckHr(inputType.SetUINT32(MediaFoundationInterop.MF_MT_FIXED_SIZE_SAMPLES, 1), "Input MF_MT_FIXED_SIZE_SAMPLES");
                MediaFoundationInterop.CheckHr(inputType.SetUINT32(MediaFoundationInterop.MF_MT_SAMPLE_SIZE, _targetStride * _height), "Input MF_MT_SAMPLE_SIZE");
                MediaFoundationInterop.CheckHr(inputType.SetUINT32(MediaFoundationInterop.MF_MT_ALL_SAMPLES_INDEPENDENT, 1), "Input MF_MT_ALL_SAMPLES_INDEPENDENT");

                MediaFoundationInterop.CheckHr(writer.SetInputMediaType(_streamIndex, inputType, null), "IMFSinkWriter.SetInputMediaType");
                MediaFoundationInterop.CheckHr(writer.BeginWriting(), "IMFSinkWriter.BeginWriting");

                if (audioFormat.HasValue)
                {
                    if (!descriptor.SupportsAudio)
                    {
                        throw new NotSupportedException($"The '{extension}' container with codec '{codecId}' does not support audio recording.");
                    }

                    audioStreamIndex = InitializeAudioStream(writer, descriptor, audioFormat.Value, audioBitrate);
                    configuredAudioFormat = audioFormat;
                }

                _sinkWriter = writer;
                writer = null;
                _hardwareContext = hardwareContext;
                hardwareContext = null;
                initialized = true;
            }
            catch
            {
                if (writer != null)
                {
                    Marshal.ReleaseComObject(writer);
                }

                throw;
            }
            finally
            {
                if (outputType != null)
                {
                    Marshal.ReleaseComObject(outputType);
                }

                if (inputType != null)
                {
                    Marshal.ReleaseComObject(inputType);
                }

                hardwareContext?.Dispose();

                if (!initialized)
                {
                    _hardwareContext?.Dispose();
                    MediaFoundationInterop.Release();
                }
            }

            _supportsAudio = audioStreamIndex.HasValue;
            _audioStreamIndex = audioStreamIndex;
            _audioFormat = configuredAudioFormat;
        }

        public bool IsHardwareAccelerated => _isHardwareAccelerated;

        public bool SupportsAudio => _supportsAudio;

        private int InitializeAudioStream(MediaFoundationInterop.IMFSinkWriter writer, FormatDescriptor descriptor, AudioFormat format, int requestedAudioBitrate)
        {
            MediaFoundationInterop.IMFMediaType? outputAudioType = null;
            MediaFoundationInterop.IMFMediaType? inputAudioType = null;

            try
            {
                outputAudioType = MediaFoundationInterop.CreateMediaType();
                MediaFoundationInterop.CheckHr(outputAudioType.SetGUID(MediaFoundationInterop.MF_MT_MAJOR_TYPE, MediaFoundationInterop.MFMediaType_Audio), "Audio MF_MT_MAJOR_TYPE");
                MediaFoundationInterop.CheckHr(outputAudioType.SetGUID(MediaFoundationInterop.MF_MT_SUBTYPE, descriptor.AudioSubtype), "Audio MF_MT_SUBTYPE");
                MediaFoundationInterop.CheckHr(outputAudioType.SetUINT32(MediaFoundationInterop.MF_MT_AUDIO_NUM_CHANNELS, format.Channels), "Audio channels");
                MediaFoundationInterop.CheckHr(outputAudioType.SetUINT32(MediaFoundationInterop.MF_MT_AUDIO_SAMPLES_PER_SECOND, format.SampleRate), "Audio sample rate");

                bool outputIsPcm = descriptor.AudioSubtype == MediaFoundationInterop.MFAudioFormat_PCM || descriptor.AudioSubtype == MediaFoundationInterop.MFAudioFormat_Float;
                int resolvedAudioBitrate = ResolveAudioBitrate(requestedAudioBitrate, descriptor.DefaultAudioBitrate, format);
                int averageBytes;
                if (resolvedAudioBitrate > 0)
                {
                    int requestedAverageBytes = Math.Max(1, resolvedAudioBitrate / 8);
                    averageBytes = outputIsPcm ? Math.Max(format.BytesPerSecond, requestedAverageBytes) : requestedAverageBytes;
                }
                else
                {
                    averageBytes = format.BytesPerSecond;
                }

                MediaFoundationInterop.CheckHr(outputAudioType.SetUINT32(MediaFoundationInterop.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, averageBytes), "Audio average bytes");
                if (outputIsPcm)
                {
                    MediaFoundationInterop.CheckHr(outputAudioType.SetUINT32(MediaFoundationInterop.MF_MT_AUDIO_BLOCK_ALIGNMENT, format.BlockAlign), "Audio block alignment");
                    MediaFoundationInterop.CheckHr(outputAudioType.SetUINT32(MediaFoundationInterop.MF_MT_AUDIO_BITS_PER_SAMPLE, format.BitsPerSample), "Audio bits per sample");
                    if (format.ChannelMask != 0)
                    {
                        MediaFoundationInterop.CheckHr(outputAudioType.SetUINT32(MediaFoundationInterop.MF_MT_AUDIO_CHANNEL_MASK, unchecked((int)format.ChannelMask)), "Audio channel mask");
                    }
                }

                if (descriptor.AudioSubtype == MediaFoundationInterop.MFAudioFormat_AAC)
                {
                    MediaFoundationInterop.CheckHr(outputAudioType.SetUINT32(MediaFoundationInterop.MF_MT_AAC_PAYLOAD_TYPE, 0), "Audio AAC payload");
                    MediaFoundationInterop.CheckHr(outputAudioType.SetUINT32(MediaFoundationInterop.MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION, 0x29), "Audio AAC profile");
                }

                MediaFoundationInterop.CheckHr(writer.AddStream(outputAudioType, out int streamIndex), "IMFSinkWriter.AddStream(Audio)");


                // Create input audio type - CORRECTED VERSION
                inputAudioType = MediaFoundationInterop.CreateMediaType();
                MediaFoundationInterop.CheckHr(inputAudioType.SetGUID(MediaFoundationInterop.MF_MT_MAJOR_TYPE, MediaFoundationInterop.MFMediaType_Audio), "Audio input MF_MT_MAJOR_TYPE");

                // Use the actual format from WASAPI capture
                Guid inputSubtype = format.IsFloat ? MediaFoundationInterop.MFAudioFormat_Float : MediaFoundationInterop.MFAudioFormat_PCM;
                MediaFoundationInterop.CheckHr(inputAudioType.SetGUID(MediaFoundationInterop.MF_MT_SUBTYPE, inputSubtype), "Audio input MF_MT_SUBTYPE");

                // Set essential audio format properties
                MediaFoundationInterop.CheckHr(inputAudioType.SetUINT32(MediaFoundationInterop.MF_MT_AUDIO_NUM_CHANNELS, format.Channels), "Audio input channels");
                MediaFoundationInterop.CheckHr(inputAudioType.SetUINT32(MediaFoundationInterop.MF_MT_AUDIO_SAMPLES_PER_SECOND, format.SampleRate), "Audio input sample rate");
                MediaFoundationInterop.CheckHr(inputAudioType.SetUINT32(MediaFoundationInterop.MF_MT_AUDIO_BLOCK_ALIGNMENT, format.BlockAlign), "Audio input block alignment");
                MediaFoundationInterop.CheckHr(inputAudioType.SetUINT32(MediaFoundationInterop.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, format.BytesPerSecond), "Audio input average bytes");
                MediaFoundationInterop.CheckHr(inputAudioType.SetUINT32(MediaFoundationInterop.MF_MT_AUDIO_BITS_PER_SAMPLE, format.BitsPerSample), "Audio input bits per sample");

                // Set valid bits per sample (important for non-float formats)
                if (!format.IsFloat)
                {
                    int validBits = format.ValidBitsPerSample > 0 && format.ValidBitsPerSample <= format.BitsPerSample
                        ? format.ValidBitsPerSample
                        : format.BitsPerSample;
                    MediaFoundationInterop.CheckHr(inputAudioType.SetUINT32(MediaFoundationInterop.MF_MT_AUDIO_VALID_BITS_PER_SAMPLE, validBits), "Audio valid bits");
                }

                // Set channel mask with proper defaults
                uint channelMask = format.ChannelMask;
                if (channelMask == 0)
                {
                    // Provide default channel masks for common configurations
                    channelMask = format.Channels switch
                    {
                        1 => 0x4,      // SPEAKER_FRONT_CENTER
                        2 => 0x3,      // SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT
                        4 => 0x33,     // Quad
                        6 => 0x3F,     // 5.1
                        8 => 0x63F,    // 7.1
                        _ => 0
                    };
                }

                if (channelMask != 0)
                {
                    MediaFoundationInterop.CheckHr(inputAudioType.SetUINT32(MediaFoundationInterop.MF_MT_AUDIO_CHANNEL_MASK, unchecked((int)channelMask)), "Audio input channel mask");
                }

                // Set this flag for better encoder compatibility
                MediaFoundationInterop.CheckHr(inputAudioType.SetUINT32(MediaFoundationInterop.MF_MT_ALL_SAMPLES_INDEPENDENT, 1), "Audio all samples independent");

                MediaFoundationInterop.CheckHr(writer.SetInputMediaType(streamIndex, inputAudioType, null), "IMFSinkWriter.SetInputMediaType(Audio)");

                return streamIndex;
            }
            finally
            {
                if (outputAudioType != null)
                {
                    Marshal.ReleaseComObject(outputAudioType);
                }

                if (inputAudioType != null)
                {
                    Marshal.ReleaseComObject(inputAudioType);
                }
            }
        }

        public static MediaFoundationFrameWriter Create(string extension, string codecId, string path, int width, int height, int frameRate, AudioFormat? audioFormat, int videoBitrate, int audioBitrate, HardwareEncoderSelection selection)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                throw new ArgumentException("Extension is required.", nameof(extension));
            }

            if (!FormatMap.TryGetValue(extension, out var codecs) || !codecs.TryGetValue(codecId, out var descriptor))
            {
                throw new NotSupportedException($"Recording format '{extension}' with codec '{codecId}' is not supported.");
            }

            if (audioFormat.HasValue && !descriptor.SupportsAudio)
            {
                throw new NotSupportedException($"Recording format '{extension}' with codec '{codecId}' does not support audio capture.");
            }

            return new MediaFoundationFrameWriter(extension, codecId, path, width, height, frameRate, descriptor, audioFormat, videoBitrate, audioBitrate, selection);
        }

        private static MediaFoundationInterop.IMFSinkWriter CreateSinkWriter(string path, FormatDescriptor descriptor, HardwareEncoderSelection selection, out bool hardwareEnabled, out HardwareDeviceContext? hardwareContext)
        {
            hardwareContext = null;

            if (selection.Api != HardwareAccelerationApi.Software)
            {
                try
                {
                    return CreateSinkWriterInternal(path, descriptor, selection, requestHardware: true, out hardwareEnabled, out hardwareContext);
                }
                catch (Exception ex)
                {
                    if (!selection.AllowSoftwareFallback)
                    {
                        throw new InvalidOperationException("Failed to initialize Media Foundation sink writer.", ex);
                    }
                }
            }

            return CreateSinkWriterInternal(path, descriptor, selection, requestHardware: false, out hardwareEnabled, out hardwareContext);
        }

        private static MediaFoundationInterop.IMFSinkWriter CreateSinkWriterInternal(string path, FormatDescriptor descriptor, HardwareEncoderSelection selection, bool requestHardware, out bool hardwareEnabled, out HardwareDeviceContext? hardwareContext)
        {
            MediaFoundationInterop.IMFAttributes? attributes = null;
            hardwareContext = null;

            try
            {
                int attributeCount = 1;
                if (descriptor.ContainerType.HasValue)
                {
                    attributeCount++;
                }

                if (requestHardware)
                {
                    attributeCount += 2;
                }

                attributes = MediaFoundationInterop.CreateAttributes(attributeCount);
                MediaFoundationInterop.CheckHr(attributes.SetUINT32(MediaFoundationInterop.MF_SINK_WRITER_DISABLE_THROTTLING, 1), "IMFAttributes.SetUINT32(MF_SINK_WRITER_DISABLE_THROTTLING)");

                if (descriptor.ContainerType.HasValue)
                {
                    MediaFoundationInterop.CheckHr(attributes.SetGUID(MediaFoundationInterop.MF_TRANSCODE_CONTAINERTYPE, descriptor.ContainerType.Value), "IMFAttributes.SetGUID(MF_TRANSCODE_CONTAINERTYPE)");
                }

                if (requestHardware)
                {
                    MediaFoundationInterop.CheckHr(attributes.SetUINT32(MediaFoundationInterop.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1), "IMFAttributes.SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS)");
                    hardwareContext = HardwareDeviceContext.Create(selection);
                    var sinkWriterD3DManager = MediaFoundationInterop.MF_SINK_WRITER_D3D_MANAGER;
                    MediaFoundationInterop.CheckHr(attributes.SetUnknown(ref sinkWriterD3DManager, hardwareContext.DeviceManager), "IMFAttributes.SetUnknown(MF_SINK_WRITER_D3D_MANAGER)");
                }

                var writer = MediaFoundationInterop.CreateSinkWriter(path, attributes);
                hardwareEnabled = requestHardware;
                return writer;
            }
            catch
            {
                hardwareContext?.Dispose();
                throw;
            }
            finally
            {
                if (attributes != null)
                {
                    Marshal.ReleaseComObject(attributes);
                }
            }
        }

        public void WriteVideoFrame(Bitmap frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MediaFoundationFrameWriter));
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

        private void WriteFrameInternal(IntPtr scan0, int stride)
        {
            MediaFoundationInterop.IMFMediaBuffer? buffer = null;
            MediaFoundationInterop.IMFSample? sample = null;
            IntPtr destination = IntPtr.Zero;

            try
            {
                buffer = MediaFoundationInterop.CreateMemoryBuffer(_targetStride * _height);
                MediaFoundationInterop.CheckHr(buffer.Lock(out destination, out var maxLength, out _), "IMFMediaBuffer.Lock");

                int required = _targetStride * _height;
                if (required > maxLength)
                {
                    throw new InvalidOperationException("Allocated Media Foundation buffer is smaller than the frame size.");
                }

                CopyFrame(scan0, stride, destination, _targetStride, _height);

                MediaFoundationInterop.CheckHr(buffer.Unlock(), "IMFMediaBuffer.Unlock");
                destination = IntPtr.Zero;

                MediaFoundationInterop.CheckHr(buffer.SetCurrentLength(required), "IMFMediaBuffer.SetCurrentLength");

                sample = MediaFoundationInterop.CreateSample();
                MediaFoundationInterop.CheckHr(sample.AddBuffer(buffer), "IMFSample.AddBuffer");

                long duration = _baseFrameDuration;
                if (_durationRemainder > 0)
                {
                    _durationAccumulator += _durationRemainder;
                    if (_durationAccumulator >= _frameRate)
                    {
                        duration += 1;
                        _durationAccumulator -= _frameRate;
                    }
                }

                MediaFoundationInterop.CheckHr(sample.SetSampleTime(_timestamp), "IMFSample.SetSampleTime");
                MediaFoundationInterop.CheckHr(sample.SetSampleDuration(duration), "IMFSample.SetSampleDuration");

                _timestamp += duration;

                MediaFoundationInterop.CheckHr(_sinkWriter.WriteSample(_streamIndex, sample), "IMFSinkWriter.WriteSample");
            }
            finally
            {
                if (destination != IntPtr.Zero && buffer != null)
                {
                    buffer.Unlock();
                }

                if (sample != null)
                {
                    Marshal.ReleaseComObject(sample);
                }

                if (buffer != null)
                {
                    Marshal.ReleaseComObject(buffer);
                }
            }
        }

        public void WriteAudioSample(ReadOnlySpan<byte> data, int frames)
        {
            if (!_supportsAudio || !_audioStreamIndex.HasValue || _audioFormat == null)
            {
                throw new InvalidOperationException("Audio stream is not configured for this recording.");
            }

            if (frames <= 0 || data.Length == 0)
            {
                return;
            }

            var format = _audioFormat.Value;
            long duration = format.FramesToDurationHns(frames);

            MediaFoundationInterop.IMFMediaBuffer? buffer = null;
            MediaFoundationInterop.IMFSample? sample = null;
            IntPtr pointer = IntPtr.Zero;

            lock (_audioSync)
            {
                try
                {
                    buffer = MediaFoundationInterop.CreateMemoryBuffer(data.Length);
                    MediaFoundationInterop.CheckHr(buffer.Lock(out pointer, out var maxLength, out _), "Audio IMFMediaBuffer.Lock");
                    if (data.Length > maxLength)
                    {
                        throw new InvalidOperationException("Allocated audio buffer is too small for the captured sample.");
                    }

                    byte[] temp = ArrayPool<byte>.Shared.Rent(data.Length);
                    try
                    {
                        data.CopyTo(temp);
                        Marshal.Copy(temp, 0, pointer, data.Length);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(temp);
                    }

                    MediaFoundationInterop.CheckHr(buffer.SetCurrentLength(data.Length), "Audio IMFMediaBuffer.SetCurrentLength");

                    sample = MediaFoundationInterop.CreateSample();
                    MediaFoundationInterop.CheckHr(sample.AddBuffer(buffer), "Audio IMFSample.AddBuffer");
                    MediaFoundationInterop.CheckHr(sample.SetSampleTime(_audioTimestamp), "Audio IMFSample.SetSampleTime");
                    MediaFoundationInterop.CheckHr(sample.SetSampleDuration(duration), "Audio IMFSample.SetSampleDuration");

                    MediaFoundationInterop.CheckHr(_sinkWriter.WriteSample(_audioStreamIndex.Value, sample), "IMFSinkWriter.WriteSample(Audio)");

                    _audioTimestamp += duration;
                }
                finally
                {
                    if (pointer != IntPtr.Zero && buffer != null)
                    {
                        buffer.Unlock();
                    }

                    if (sample != null)
                    {
                        Marshal.ReleaseComObject(sample);
                    }

                    if (buffer != null)
                    {
                        Marshal.ReleaseComObject(buffer);
                    }
                }
            }
        }

        public void CompleteAudio()
        {
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
                if (_sinkWriter != null)
                {
                    try
                    {
                        MediaFoundationInterop.CheckHr(_sinkWriter.Finalize(), "IMFSinkWriter.Finalize");
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(_sinkWriter);
                    }
                }
            }
            finally
            {
                _hardwareContext?.Dispose();
                MediaFoundationInterop.Release();
            }
        }

        private sealed class HardwareDeviceContext : IDisposable
        {
            private IntPtr _device;
            private IntPtr _deviceContext;
            private MediaFoundationInterop.IMFDXGIDeviceManager? _deviceManager;
            private bool _disposed;

            private HardwareDeviceContext(IntPtr device, IntPtr deviceContext, MediaFoundationInterop.IMFDXGIDeviceManager deviceManager)
            {
                _device = device;
                _deviceContext = deviceContext;
                _deviceManager = deviceManager;
            }

            public MediaFoundationInterop.IMFDXGIDeviceManager DeviceManager
            {
                get
                {
                    if (_disposed || _deviceManager == null)
                    {
                        throw new ObjectDisposedException(nameof(HardwareDeviceContext));
                    }

                    return _deviceManager;
                }
            }

            public static HardwareDeviceContext Create(HardwareEncoderSelection selection)
            {
                if (selection.Api == HardwareAccelerationApi.Software)
                {
                    throw new InvalidOperationException("Hardware encoding selection is set to software.");
                }

                IntPtr device = IntPtr.Zero;
                IntPtr context = IntPtr.Zero;
                MediaFoundationInterop.IMFDXGIDeviceManager? manager = null;
                GCHandle handle = default;

                try
                {
                    var featureLevels = new[]
                    {
                        D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_1,
                        D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_0,
                        D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
                        D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
                        D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1,
                        D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0,
                    };

                    handle = GCHandle.Alloc(featureLevels, GCHandleType.Pinned);

                    if (!TryCreateDevice(selection, handle.AddrOfPinnedObject(), (uint)featureLevels.Length, out device, out context))
                    {
                        throw new InvalidOperationException("Failed to create a Direct3D 11 device for hardware encoding.");
                    }
                }
                finally
                {
                    if (handle.IsAllocated)
                    {
                        handle.Free();
                    }
                }

                TryEnableMultithreadProtection(context);

                try
                {
                    manager = MediaFoundationInterop.CreateDxgiDeviceManager(out var resetToken);
                    MediaFoundationInterop.CheckHr(manager.ResetDevice(device, resetToken), "IMFDXGIDeviceManager.ResetDevice");

                    var result = new HardwareDeviceContext(device, context, manager);
                    device = IntPtr.Zero;
                    context = IntPtr.Zero;
                    manager = null;
                    return result;
                }
                finally
                {
                    if (manager != null)
                    {
                        Marshal.ReleaseComObject(manager);
                    }

                    if (context != IntPtr.Zero)
                    {
                        Marshal.Release(context);
                    }

                    if (device != IntPtr.Zero)
                    {
                        Marshal.Release(device);
                    }
                }
            }

            internal static IReadOnlyList<HardwareAdapterDescriptor> GetAdapterDescriptors()
            {
                var descriptors = new List<HardwareAdapterDescriptor>();
                List<AdapterInfo>? adapters = null;

                try
                {
                    adapters = EnumerateHardwareAdapters();
                    foreach (var adapter in adapters)
                    {
                        descriptors.Add(new HardwareAdapterDescriptor(adapter.LuidHighPart, adapter.LuidLowPart, adapter.Description));
                    }
                }
                finally
                {
                    if (adapters != null)
                    {
                        foreach (var adapter in adapters)
                        {
                            adapter.Dispose();
                        }
                    }
                }

                return descriptors;
            }

            private static bool TryCreateDevice(HardwareEncoderSelection selection, IntPtr featureLevelPtr, uint featureLevelCount, out IntPtr device, out IntPtr context)
            {
                if (TryCreateDeviceOnPreferredAdapter(selection, featureLevelPtr, featureLevelCount, out device, out context))
                {
                    return true;
                }

                if (selection.Api == HardwareAccelerationApi.Auto || (selection.Api == HardwareAccelerationApi.Direct3D11 && !selection.HasAdapter))
                {
                    int hr = D3D11CreateDevice(
                        IntPtr.Zero,
                        D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                        IntPtr.Zero,
                        (uint)(D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_VIDEO_SUPPORT),
                        featureLevelPtr,
                        featureLevelCount,
                        D3D11_SDK_VERSION,
                        out device,
                        out _,
                        out context);

                    if (hr >= 0)
                    {
                        return true;
                    }

                    if (device != IntPtr.Zero)
                    {
                        Marshal.Release(device);
                        device = IntPtr.Zero;
                    }

                    if (context != IntPtr.Zero)
                    {
                        Marshal.Release(context);
                        context = IntPtr.Zero;
                    }
                }

                device = IntPtr.Zero;
                context = IntPtr.Zero;
                return false;
            }

            private static bool TryCreateDeviceOnPreferredAdapter(HardwareEncoderSelection selection, IntPtr featureLevelPtr, uint featureLevelCount, out IntPtr device, out IntPtr context)
            {
                device = IntPtr.Zero;
                context = IntPtr.Zero;

                List<AdapterInfo>? adapters = null;

                try
                {
                    adapters = EnumerateHardwareAdapters();
                    if (adapters.Count == 0)
                    {
                        return false;
                    }

                    if (selection.Api == HardwareAccelerationApi.Direct3D11 && selection.HasAdapter)
                    {
                        foreach (var adapter in adapters)
                        {
                            if (adapter.Matches(selection.AdapterHighPart, selection.AdapterLowPart))
                            {
                                return TryCreateDeviceOnAdapter(adapter, featureLevelPtr, featureLevelCount, out device, out context);
                            }
                        }

                        return false;
                    }

                    foreach (var adapter in adapters)
                    {
                        if (TryCreateDeviceOnAdapter(adapter, featureLevelPtr, featureLevelCount, out device, out context))
                        {
                            return true;
                        }
                    }

                    return false;
                }
                finally
                {
                    if (adapters != null)
                    {
                        foreach (var adapter in adapters)
                        {
                            adapter.Dispose();
                        }
                    }
                }
            }

            private static bool TryCreateDeviceOnAdapter(AdapterInfo adapter, IntPtr featureLevelPtr, uint featureLevelCount, out IntPtr device, out IntPtr context)
            {
                device = IntPtr.Zero;
                context = IntPtr.Zero;

                int hr = D3D11CreateDevice(
                    adapter.AdapterPtr,
                    D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN,
                    IntPtr.Zero,
                    (uint)(D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_VIDEO_SUPPORT),
                    featureLevelPtr,
                    featureLevelCount,
                    D3D11_SDK_VERSION,
                    out device,
                    out _,
                    out context);

                if (hr >= 0)
                {
                    return true;
                }

                if (device != IntPtr.Zero)
                {
                    Marshal.Release(device);
                    device = IntPtr.Zero;
                }

                if (context != IntPtr.Zero)
                {
                    Marshal.Release(context);
                    context = IntPtr.Zero;
                }

                return false;
            }

            private static List<AdapterInfo> EnumerateHardwareAdapters()
            {
                var adapters = new List<AdapterInfo>();
                IntPtr factoryPtr = IntPtr.Zero;
                IDXGIFactory1? factory = null;

                try
                {
                    Guid factoryGuid = typeof(IDXGIFactory1).GUID;
                    int hr = CreateDXGIFactory1(ref factoryGuid, out factoryPtr);
                    if (hr < 0)
                    {
                        return adapters;
                    }

                    factory = (IDXGIFactory1)Marshal.GetObjectForIUnknown(factoryPtr);
                    Marshal.Release(factoryPtr);
                    factoryPtr = IntPtr.Zero;

                    uint index = 0;
                    while (true)
                    {
                        IDXGIAdapter1? adapter = null;
                        try
                        {
                            hr = factory.EnumAdapters1(index, out adapter);
                            if (hr == DXGI_ERROR_NOT_FOUND)
                            {
                                break;
                            }

                            if (hr < 0)
                            {
                                throw new InvalidOperationException($"IDXGIFactory1.EnumAdapters1 failed with HRESULT 0x{hr:X8}.");
                            }

                            if (adapter == null)
                            {
                                continue;
                            }

                            adapter.GetDesc1(out var desc);
                            if ((desc.Flags & (uint)DXGI_ADAPTER_FLAG.DXGI_ADAPTER_FLAG_SOFTWARE) != 0)
                            {
                                continue;
                            }

                            IntPtr adapterPtr = Marshal.GetIUnknownForObject(adapter);
                            adapters.Add(new AdapterInfo(adapterPtr, desc));
                        }
                        finally
                        {
                            if (adapter != null)
                            {
                                Marshal.ReleaseComObject(adapter);
                            }

                            index++;
                        }
                    }

                    adapters.Sort((a, b) =>
                    {
                        int nvidiaComparison = b.IsNvidia.CompareTo(a.IsNvidia);
                        if (nvidiaComparison != 0)
                        {
                            return nvidiaComparison;
                        }

                        int memoryComparison = b.DedicatedVideoMemory.CompareTo(a.DedicatedVideoMemory);
                        if (memoryComparison != 0)
                        {
                            return memoryComparison;
                        }

                        return string.Compare(b.Description, a.Description, StringComparison.OrdinalIgnoreCase);
                    });

                    return adapters;
                }
                finally
                {
                    if (factory != null)
                    {
                        Marshal.ReleaseComObject(factory);
                    }

                    if (factoryPtr != IntPtr.Zero)
                    {
                        Marshal.Release(factoryPtr);
                    }
                }
            }

            private static void TryEnableMultithreadProtection(IntPtr context)
            {
                if (context == IntPtr.Zero)
                {
                    return;
                }

                IntPtr multithreadPtr = IntPtr.Zero;
                try
                {
                    Guid multithreadGuid = typeof(ID3D11Multithread).GUID;
                    int hr = Marshal.QueryInterface(context, ref multithreadGuid, out multithreadPtr);
                    if (hr < 0)
                    {
                        return;
                    }

                    var multithread = (ID3D11Multithread)Marshal.GetObjectForIUnknown(multithreadPtr);
                    try
                    {
                        multithread.SetMultithreadProtected(true);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(multithread);
                    }
                }
                finally
                {
                    if (multithreadPtr != IntPtr.Zero)
                    {
                        Marshal.Release(multithreadPtr);
                    }
                }
            }

            private sealed class AdapterInfo : IDisposable
            {
                public AdapterInfo(IntPtr adapterPtr, DXGI_ADAPTER_DESC1 description)
                {
                    AdapterPtr = adapterPtr;
                    DescriptionRaw = description;
                }

                public IntPtr AdapterPtr { get; }

                private DXGI_ADAPTER_DESC1 DescriptionRaw { get; }

                public bool IsSoftwareAdapter => (DescriptionRaw.Flags & (uint)DXGI_ADAPTER_FLAG.DXGI_ADAPTER_FLAG_SOFTWARE) != 0;

                public bool IsNvidia => DescriptionRaw.VendorId == NvidiaVendorId;

                public ulong DedicatedVideoMemory
                {
                    get
                    {
                        return Environment.Is64BitProcess
                            ? DescriptionRaw.DedicatedVideoMemory.ToUInt64()
                            : DescriptionRaw.DedicatedVideoMemory.ToUInt32();
                    }
                }

                public string Description => DescriptionRaw.Description ?? string.Empty;

                public int LuidHighPart => DescriptionRaw.AdapterLuid.HighPart;

                public uint LuidLowPart => DescriptionRaw.AdapterLuid.LowPart;

                public bool Matches(int highPart, uint lowPart)
                {
                    return DescriptionRaw.AdapterLuid.HighPart == highPart && DescriptionRaw.AdapterLuid.LowPart == lowPart;
                }

                public void Dispose()
                {
                    if (AdapterPtr != IntPtr.Zero)
                    {
                        Marshal.Release(AdapterPtr);
                    }
                }
            }

            private const uint NvidiaVendorId = 0x10DE;
            private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                try
                {
                    if (_deviceManager != null)
                    {
                        Marshal.ReleaseComObject(_deviceManager);
                        _deviceManager = null;
                    }

                    if (_deviceContext != IntPtr.Zero)
                    {
                        Marshal.Release(_deviceContext);
                        _deviceContext = IntPtr.Zero;
                    }

                    if (_device != IntPtr.Zero)
                    {
                        Marshal.Release(_device);
                        _device = IntPtr.Zero;
                    }
                }
                catch
                {
                }
            }
        }

        private enum D3D_DRIVER_TYPE : uint
        {
            D3D_DRIVER_TYPE_UNKNOWN = 0,
            D3D_DRIVER_TYPE_HARDWARE = 1,
        }

        [Flags]
        private enum D3D11_CREATE_DEVICE_FLAG : uint
        {
            D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20,
            D3D11_CREATE_DEVICE_VIDEO_SUPPORT = 0x800,
        }

        private enum D3D_FEATURE_LEVEL : uint
        {
            D3D_FEATURE_LEVEL_12_1 = 0x0000C100,
            D3D_FEATURE_LEVEL_12_0 = 0x0000C000,
            D3D_FEATURE_LEVEL_11_1 = 0x0000B100,
            D3D_FEATURE_LEVEL_11_0 = 0x0000B000,
            D3D_FEATURE_LEVEL_10_1 = 0x0000A100,
            D3D_FEATURE_LEVEL_10_0 = 0x0000A000,
        }

        private const uint D3D11_SDK_VERSION = 7;

        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(
            IntPtr pAdapter,
            D3D_DRIVER_TYPE DriverType,
            IntPtr Software,
            uint Flags,
            IntPtr pFeatureLevels,
            uint FeatureLevels,
            uint SDKVersion,
            out IntPtr ppDevice,
            out D3D_FEATURE_LEVEL pFeatureLevel,
            out IntPtr ppImmediateContext);

        [DllImport("dxgi.dll")]
        private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

        [ComImport]
        [Guid("b2daad8b-03d4-4dbf-95eb-32ab4b63d0ab")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ID3D11Multithread
        {
            void Enter();
            void Leave();
            void SetMultithreadProtected(bool bMTProtect);
            bool GetMultithreadProtected();
        }

        [ComImport]
        [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIFactory1
        {
            [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);
            [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr unknown);
            [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);
            [PreserveSig] int GetParent(ref Guid riid, out IntPtr parent);
            [PreserveSig] int EnumAdapters(uint adapter, out IntPtr ppAdapter);
            [PreserveSig] int MakeWindowAssociation(IntPtr hwnd, uint flags);
            [PreserveSig] int GetWindowAssociation(out IntPtr hwnd);
            [PreserveSig] int CreateSwapChain(IntPtr device, IntPtr desc, out IntPtr swapChain);
            [PreserveSig] int CreateSoftwareAdapter(IntPtr module, out IntPtr adapter);
            [PreserveSig] int EnumAdapters1(uint adapter, out IDXGIAdapter1 ppAdapter);
            [PreserveSig] int IsCurrent();
        }

        [ComImport]
        [Guid("29038f61-3839-4626-91fd-086879011a05")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIAdapter1
        {
            [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);
            [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr unknown);
            [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);
            [PreserveSig] int GetParent(ref Guid riid, out IntPtr parent);
            [PreserveSig] int EnumOutputs(uint output, out IntPtr ppOutput);
            [PreserveSig] int GetDesc(IntPtr desc);
            [PreserveSig] int CheckInterfaceSupport(ref Guid guid, out long umdVersion);
            [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 desc);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DXGI_ADAPTER_DESC1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public UIntPtr DedicatedVideoMemory;
            public UIntPtr DedicatedSystemMemory;
            public UIntPtr SharedSystemMemory;
            public LUID AdapterLuid;
            public uint Flags;
        }

        [Flags]
        private enum DXGI_ADAPTER_FLAG : uint
        {
            DXGI_ADAPTER_FLAG_NONE = 0,
            DXGI_ADAPTER_FLAG_SOFTWARE = 0x2,
        }

        private static int ResolveVideoBitrate(int width, int height, int frameRate, int requestedBitrate, int defaultBitrate)
        {
            if (requestedBitrate > 0)
            {
                return requestedBitrate;
            }

            if (defaultBitrate > 0)
            {
                return defaultBitrate;
            }

            return CalculateBitrate(width, height, frameRate);
        }

        private static int ResolveAudioBitrate(int requestedBitrate, int defaultBitrate, AudioFormat format)
        {
            int fallback = defaultBitrate > 0 ? defaultBitrate : format.BytesPerSecond * 8;
            if (requestedBitrate > 0)
            {
                int minimum = format.BytesPerSecond * 8;
                return requestedBitrate < minimum ? minimum : requestedBitrate;
            }

            return fallback;
        }

        private static int CalculateBitrate(int width, int height, int frameRate)
        {
            long pixelsPerSecond = (long)Math.Max(1, width) * Math.Max(1, height) * Math.Max(1, frameRate);
            long bitRate = pixelsPerSecond * 8L;
            if (bitRate < 1_000_000L)
            {
                bitRate = 1_000_000L;
            }

            if (bitRate > int.MaxValue)
            {
                bitRate = int.MaxValue;
            }

            return (int)bitRate;
        }

        private static unsafe void CopyFrame(IntPtr source, int sourceStride, IntPtr destination, int destinationStride, int height)
        {
            byte* src = (byte*)source.ToPointer();
            if (sourceStride < 0)
            {
                src += (long)(height - 1) * (-sourceStride);
                sourceStride = -sourceStride;
            }

            byte* dst = (byte*)destination.ToPointer();
            int rowLength = Math.Min(sourceStride, destinationStride);

            for (int y = 0; y < height; y++)
            {
                Buffer.MemoryCopy(src + (long)y * sourceStride, dst + (long)y * destinationStride, destinationStride, rowLength);
            }
        }

        private readonly struct FormatDescriptor
        {
            public FormatDescriptor(string codecId, string localizationKey, Guid videoSubtype, Guid? containerType, Guid audioSubtype, bool supportsAudio, int defaultVideoBitrate, int defaultAudioBitrate)
            {
                CodecId = codecId;
                LocalizationKey = localizationKey;
                VideoSubtype = videoSubtype;
                ContainerType = containerType;
                AudioSubtype = audioSubtype;
                SupportsAudio = supportsAudio && audioSubtype != Guid.Empty;
                DefaultVideoBitrate = defaultVideoBitrate;
                DefaultAudioBitrate = defaultAudioBitrate;
            }

            public string CodecId { get; }

            public string LocalizationKey { get; }

            public Guid VideoSubtype { get; }

            public Guid? ContainerType { get; }

            public Guid AudioSubtype { get; }

            public bool SupportsAudio { get; }

            public int DefaultVideoBitrate { get; }

            public int DefaultAudioBitrate { get; }
        }

        private static class MediaFoundationInterop
        {
            private const int MF_VERSION = 0x00020070;
            private const int MFSTARTUP_FULL = 0;

            private static readonly object Sync = new object();
            private static int _refCount;

            public static readonly Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFMediaType_Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFVideoFormat_HEVC = new Guid("43564548-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFVideoFormat_AV1 = new Guid("31305641-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFVideoFormat_VP9 = new Guid("30395056-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFVideoFormat_WMV3 = new Guid("33564D57-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFVideoFormat_MPEG2 = new Guid("E06D8026-DB46-11CF-B4D1-00805F6CBBEA");
            public static readonly Guid MFVideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFTranscodeContainerType_ASF = new Guid("430F6F6E-B6BF-4FC1-A0BD-9EE46EEE2AFB");
            public static readonly Guid MFTranscodeContainerType_MPEG4 = new Guid("DC6CD05D-B9D0-40EF-BD35-FA622C1AB28A");
            public static readonly Guid MFTranscodeContainerType_MPEG2 = new Guid("BFC2DBF9-7BB4-4F8F-AFDE-E112C44BA882");
            public static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new Guid("A634A91C-822B-41B9-A494-4DE4643612B0");
            public static readonly Guid MF_SINK_WRITER_DISABLE_THROTTLING = new Guid("08B845D8-2B74-4AFE-9D53-BE16D2D5AE4F");
            public static readonly Guid MF_SINK_WRITER_D3D_MANAGER = new Guid("EC82238C-1EA6-4DBF-8451-4D3EBE0B6837");
            public static readonly Guid MF_TRANSCODE_CONTAINERTYPE = new Guid("150FF23F-4ABC-478B-AC4F-E1916FBA1CCA");
            public static readonly Guid MF_MT_MAJOR_TYPE = new Guid("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
            public static readonly Guid MF_MT_SUBTYPE = new Guid("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
            public static readonly Guid MF_MT_FRAME_SIZE = new Guid("1652C33D-D6B2-4012-B834-72030849A37D");
            public static readonly Guid MF_MT_FRAME_RATE = new Guid("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
            public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new Guid("C6376A1E-8D0A-4027-BE45-6D9A0AD39BB6");
            public static readonly Guid MF_MT_INTERLACE_MODE = new Guid("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");
            public static readonly Guid MF_MT_DEFAULT_STRIDE = new Guid("644B4E48-1E02-4516-B0EB-C01CA9D4AA75");
            public static readonly Guid MF_MT_FIXED_SIZE_SAMPLES = new Guid("B8EBEFAF-B718-4E04-B0A9-116775E3321B");
            public static readonly Guid MF_MT_SAMPLE_SIZE = new Guid("DAD3AB78-1990-408B-BCE2-EB41B83B0ED5");
            public static readonly Guid MF_MT_AVG_BITRATE = new Guid("20332624-FB0D-4D9E-BD0D-CBF6786C102E");
            public static readonly Guid MF_MT_ALL_SAMPLES_INDEPENDENT = new Guid("C9173739-5E56-461C-B713-46FB995CB95F");
            public static readonly Guid MFAudioFormat_PCM = new Guid("00000001-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFAudioFormat_Float = new Guid("00000003-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFAudioFormat_AAC = new Guid("00001610-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFAudioFormat_WMAudioV9 = new Guid("00000162-0000-0010-8000-00AA00389B71");
            public static readonly Guid MFAudioFormat_MPEG = new Guid("00000050-0000-0010-8000-00AA00389B71");
            public static readonly Guid MF_MT_AUDIO_NUM_CHANNELS = new Guid("FBAAEB32-0A2C-43C4-8EF6-1A0AAF0CBFB1");
            public static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new Guid("5FAEE9F9-7B2E-43BD-9E94-05BC827116B7");
            public static readonly Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new Guid("322DE230-9E08-450B-A165-1DD51BE1E3A0");
            public static readonly Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = new Guid("1AAB75C8-C9E6-4B9C-AF90-E67CB18F3464");
            public static readonly Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new Guid("F2DEAF05-FEAF-4F2B-9FC9-C16BCEB54C6D");
            public static readonly Guid MF_MT_AUDIO_VALID_BITS_PER_SAMPLE = new Guid("8448455D-0058-4615-8F88-2CC812AABADD");
            public static readonly Guid MF_MT_AUDIO_CHANNEL_MASK = new Guid("55FB5765-644A-4AFD-9164-F478FFE4472E");
            public static readonly Guid MF_MT_AUDIO_PREFER_WAVEFORMATEX = new Guid("A901AABA-E037-458A-B5C6-DD90BCA80902");
            public static readonly Guid MF_MT_AAC_PAYLOAD_TYPE = new Guid("BFBABE79-7434-4D1C-9445-D25A5BBEED83");
            public static readonly Guid MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION = new Guid("7632F0E6-9538-4D61-ACDA-E072CD37434C");

            public static void AddRef()
            {
                lock (Sync)
                {
                    if (_refCount == 0)
                    {
                        CheckHr(MFStartup(MF_VERSION, MFSTARTUP_FULL), nameof(MFStartup));
                    }

                    _refCount++;
                }
            }

            public static void Release()
            {
                lock (Sync)
                {
                    if (_refCount == 0)
                    {
                        return;
                    }

                    _refCount--;
                    if (_refCount == 0)
                    {
                        MFShutdown();
                    }
                }
            }

            public static void CheckHr(int hr, string operation)
            {
                if (hr < 0)
                {
                    try
                    {
                        Marshal.ThrowExceptionForHR(hr);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"{operation} failed with HRESULT 0x{hr:X8}.", ex);
                    }
                }
            }

            public static IMFAttributes CreateAttributes(int size)
            {
                CheckHr(MFCreateAttributes(out var attributes, size), nameof(MFCreateAttributes));
                return attributes;
            }

            public static IMFMediaType CreateMediaType()
            {
                CheckHr(MFCreateMediaType(out var type), nameof(MFCreateMediaType));
                return type;
            }

            public static IMFSinkWriter CreateSinkWriter(string path, IMFAttributes? attributes)
            {
                IntPtr rawPtr = IntPtr.Zero;
                IntPtr sinkWriterPtr = IntPtr.Zero;
                try
                {
                    CheckHr(MFCreateSinkWriterFromURL(path, IntPtr.Zero, attributes, out rawPtr), nameof(MFCreateSinkWriterFromURL));

                    Guid iid = IMFSinkWriterGuid;
                    int hr = Marshal.QueryInterface(rawPtr, ref iid, out sinkWriterPtr);
                    if (hr < 0)
                    {
                        if (hr == E_NOINTERFACE)
                        {
                            throw new NotSupportedException("Media Foundation sink writer is not available on this system. Install the Media Feature Pack or enable the Media Foundation optional Windows feature.");
                        }

                        CheckHr(hr, "Marshal.QueryInterface(IMFSinkWriter)");
                    }

                    return (IMFSinkWriter)Marshal.GetObjectForIUnknown(sinkWriterPtr);
                }
                finally
                {
                    if (sinkWriterPtr != IntPtr.Zero)
                    {
                        Marshal.Release(sinkWriterPtr);
                    }

                    if (rawPtr != IntPtr.Zero)
                    {
                        Marshal.Release(rawPtr);
                    }
                }
            }

            public static IMFSample CreateSample()
            {
                CheckHr(MFCreateSample(out var sample), nameof(MFCreateSample));
                return sample;
            }

            public static IMFMediaBuffer CreateMemoryBuffer(int size)
            {
                CheckHr(MFCreateMemoryBuffer(size, out var buffer), nameof(MFCreateMemoryBuffer));
                return buffer;
            }

            public static IMFDXGIDeviceManager CreateDxgiDeviceManager(out uint resetToken)
            {
                CheckHr(MFCreateDXGIDeviceManager(out resetToken, out var manager), nameof(MFCreateDXGIDeviceManager));
                return manager;
            }

            public static void SetAttributeSize(IMFMediaType type, Guid key, int width, int height)
            {
                uint safeWidth = (uint)Math.Max(0, width);
                uint safeHeight = (uint)Math.Max(0, height);
                CheckHr(type.SetUINT64(ref key, PackToLong(safeWidth, safeHeight)), "IMFAttributes::SetUINT64 (size)");
            }

            public static void SetAttributeRatio(IMFMediaType type, Guid key, int numerator, int denominator)
            {
                uint safeNumerator = (uint)Math.Max(0, numerator);
                uint safeDenominator = (uint)Math.Max(0, denominator);
                CheckHr(type.SetUINT64(ref key, PackToLong(safeNumerator, safeDenominator)), "IMFAttributes::SetUINT64 (ratio)");
            }

            private static long PackToLong(uint high, uint low)
            {
                return ((long)high << 32) | low;
            }

            [DllImport("mfplat.dll")]
            private static extern int MFStartup(int version, int dwFlags);

            [DllImport("mfplat.dll")]
            private static extern int MFShutdown();

            private const int E_NOINTERFACE = unchecked((int)0x80004002);

            private static readonly Guid IMFSinkWriterGuid = new Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D");

            [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
            private static extern int MFCreateSinkWriterFromURL(string? pwszOutputURL, IntPtr pUnkSink, IMFAttributes? pAttributes, out IntPtr ppSinkWriter);

            [DllImport("mfplat.dll")]
            private static extern int MFCreateMediaType(out IMFMediaType ppMFType);

            [DllImport("mfplat.dll")]
            private static extern int MFCreateAttributes(out IMFAttributes ppMFAttributes, int cInitialSize);

            [DllImport("mfplat.dll")]
            private static extern int MFCreateSample(out IMFSample ppIMFSample);

            [DllImport("mfplat.dll")]
            private static extern int MFCreateMemoryBuffer(int cbMaxLength, out IMFMediaBuffer ppBuffer);

            [DllImport("mfplat.dll")]
            private static extern int MFCreateDXGIDeviceManager(out uint resetToken, out IMFDXGIDeviceManager? ppDeviceManager);

            [ComImport]
            [Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            public interface IMFAttributes
            {
                [PreserveSig] int GetItem([In] ref Guid guidKey, IntPtr pValue);
                [PreserveSig] int GetItemType([In] ref Guid guidKey, out MF_ATTRIBUTE_TYPE pType);
                [PreserveSig] int CompareItem([In] ref Guid guidKey, [In] ref PropVariant value, out bool result);
                [PreserveSig] int Compare(IMFAttributes pTheirs, MF_ATTRIBUTES_MATCH_TYPE matchType, out bool result);
                [PreserveSig] int GetUINT32([In] ref Guid guidKey, out int value);
                [PreserveSig] int GetUINT64([In] ref Guid guidKey, out long value);
                [PreserveSig] int GetDouble([In] ref Guid guidKey, out double value);
                [PreserveSig] int GetGUID([In] ref Guid guidKey, out Guid value);
                [PreserveSig] int GetStringLength([In] ref Guid guidKey, out int length);
                [PreserveSig] int GetString([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int size, out int length);
                [PreserveSig] int GetAllocatedString([In] ref Guid guidKey, out IntPtr value, out int length);
                [PreserveSig] int GetBlobSize([In] ref Guid guidKey, out int size);
                [PreserveSig] int GetBlob([In] ref Guid guidKey, [Out] byte[] buffer, int bufferSize, out int size);
                [PreserveSig] int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr buffer, out int size);
                [PreserveSig] int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, out IntPtr ppv);
                [PreserveSig] int SetItem([In] ref Guid guidKey, [In] ref PropVariant value);
                [PreserveSig] int DeleteItem([In] ref Guid guidKey);
                [PreserveSig] int DeleteAllItems();
                [PreserveSig] int SetUINT32([In] ref Guid guidKey, int value);
                [PreserveSig] int SetUINT64([In] ref Guid guidKey, long value);
                [PreserveSig] int SetDouble([In] ref Guid guidKey, double value);
                [PreserveSig] int SetGUID([In] ref Guid guidKey, [In] ref Guid value);
                [PreserveSig] int SetString([In] ref Guid guidKey, [In, MarshalAs(UnmanagedType.LPWStr)] string value);
                [PreserveSig] int SetBlob([In] ref Guid guidKey, [In] byte[] buffer, int size);
                [PreserveSig] int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object value);
                [PreserveSig] int LockStore();
                [PreserveSig] int UnlockStore();
                [PreserveSig] int GetCount(out int count);
                [PreserveSig] int GetItemByIndex(int index, out Guid guidKey, IntPtr value);
                [PreserveSig] int CopyAllItems(IMFAttributes destination);
            }

            [ComImport]
            [Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            public interface IMFMediaType : IMFAttributes
            {
                [PreserveSig] int GetMajorType(out Guid guid);
                [PreserveSig] int IsCompressedFormat([MarshalAs(UnmanagedType.Bool)] out bool compressed);
                [PreserveSig] int IsEqual(IMFMediaType type, out MF_MEDIATYPE_EQUAL matchFlags);
                [PreserveSig] int GetRepresentation(Guid guid, out IntPtr representation);
                [PreserveSig] int FreeRepresentation(Guid guid, IntPtr representation);
            }

            [ComImport]
            [Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D")]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            public interface IMFSinkWriter
            {
                [PreserveSig] int AddStream(IMFMediaType targetMediaType, out int streamIndex);
                [PreserveSig] int SetInputMediaType(int streamIndex, IMFMediaType inputMediaType, IMFAttributes? encodingParameters);
                [PreserveSig] int BeginWriting();
                [PreserveSig] int WriteSample(int streamIndex, IMFSample sample);
                [PreserveSig] int SendStreamTick(int streamIndex, long timestamp);
                [PreserveSig] int PlaceMarker(int streamIndex, IntPtr context);
                [PreserveSig] int NotifyEndOfSegment(int streamIndex);
                [PreserveSig] int Flush(int streamIndex);
                [PreserveSig] int Finalize();
                [PreserveSig] int GetServiceForStream(int streamIndex, ref Guid guidService, ref Guid riid, out IntPtr service);
                [PreserveSig] int GetStatistics(int streamIndex, out MF_SINK_WRITER_STATISTICS statistics);
            }

            [ComImport]
            [Guid("ca86aa50-c46e-429e-9866-2fc0ba7a656f")]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            public interface IMFDXGIDeviceManager
            {
                [PreserveSig] int ResetDevice(IntPtr pDevice, uint resetToken);
                [PreserveSig] int OpenDeviceHandle(out IntPtr phDevice);
                [PreserveSig] int CloseDeviceHandle(IntPtr hDevice);
                [PreserveSig] int TestDevice(IntPtr hDevice);
                [PreserveSig] int LockDevice(IntPtr hDevice, Guid riid, out IntPtr ppv, bool block);
                [PreserveSig] int UnlockDevice(IntPtr hDevice, bool saveState);
                [PreserveSig] int GetVideoService(IntPtr hDevice, Guid riid, out IntPtr ppService);
            }

            [ComImport]
            [Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            public interface IMFSample : IMFAttributes
            {
                [PreserveSig] int GetSampleFlags(out int sampleFlags);
                [PreserveSig] int SetSampleFlags(int sampleFlags);
                [PreserveSig] int GetSampleTime(out long sampleTime);
                [PreserveSig] int SetSampleTime(long sampleTime);
                [PreserveSig] int GetSampleDuration(out long sampleDuration);
                [PreserveSig] int SetSampleDuration(long sampleDuration);
                [PreserveSig] int GetBufferCount(out int count);
                [PreserveSig] int GetBufferByIndex(int index, out IMFMediaBuffer buffer);
                [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
                [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
                [PreserveSig] int RemoveBufferByIndex(int index);
                [PreserveSig] int RemoveAllBuffers();
                [PreserveSig] int GetTotalLength(out int totalLength);
                [PreserveSig] int CopyToBuffer(IMFMediaBuffer buffer);
            }

            [ComImport]
            [Guid("045fa593-8799-42b8-9737-8464f7cbfc8d")]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            public interface IMFMediaBuffer
            {
                [PreserveSig] int Lock(out IntPtr buffer, out int maxLength, out int currentLength);
                [PreserveSig] int Unlock();
                [PreserveSig] int GetCurrentLength(out int length);
                [PreserveSig] int SetCurrentLength(int length);
                [PreserveSig] int GetMaxLength(out int maxLength);
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MF_SINK_WRITER_STATISTICS
            {
                public int cb;
                public long llLastTimestampReceived;
                public long llLastTimestampEncoded;
                public long llLastTimestampProcessed;
                public long llLastStreamTickReceived;
                public long llLastSinkSampleTimestamp;
                public long llLastSinkSampleDuration;
                public int dwNumSamplesReceived;
                public int dwNumSamplesEncoded;
                public int dwNumSamplesProcessed;
                public int dwNumStreamTicksReceived;
            }

            public enum MFVideoInterlaceMode
            {
                Progressive = 2,
            }

            public enum MF_ATTRIBUTE_TYPE
            {
                MF_ATTRIBUTE_UINT32 = 19,
                MF_ATTRIBUTE_UINT64 = 21,
                MF_ATTRIBUTE_DOUBLE = 5,
                MF_ATTRIBUTE_GUID = 72,
                MF_ATTRIBUTE_STRING = 31,
                MF_ATTRIBUTE_BLOB = 4113,
                MF_ATTRIBUTE_IUNKNOWN = 13,
            }

            public enum MF_ATTRIBUTES_MATCH_TYPE
            {
                OurItems = 0,
                TheirItems = 1,
                AllItems = 2,
                Intersection = 3,
            }

            [Flags]
            public enum MF_MEDIATYPE_EQUAL
            {
                None = 0,
                MajorTypes = 0x1,
                FormatTypes = 0x2,
                AllFields = 0x4,
            }

            [StructLayout(LayoutKind.Explicit)]
            public struct PropVariant
            {
                [FieldOffset(0)] public ushort vt;
                [FieldOffset(8)] public IntPtr pointerValue;
                [FieldOffset(8)] public int intValue;
                [FieldOffset(8)] public long longValue;
            }
        }
    }
}
