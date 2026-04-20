#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ToNRoundCounter.Application.Recording
{
    internal sealed class WasapiAudioCapture : IDisposable
    {
        private readonly CoreAudioInterop.IMMDeviceEnumerator _enumerator;
        private readonly CoreAudioInterop.IMMDevice _device;
        private readonly CoreAudioInterop.IAudioClient _audioClient;
        private readonly CoreAudioInterop.IAudioCaptureClient _captureClient;
        private readonly IntPtr _eventHandle;

        // Format actually returned by WASAPI (e.g. 32-bit float / 5.1ch / 48000Hz).
        private readonly AudioFormat _rawFormat;

        // Format always emitted to the handler. Fixed to PCM / 16-bit / stereo /
        // raw sample rate so Media Foundation's SinkWriter never has to negotiate
        // a converter MFT whose accepted input set is environment-dependent.
        private readonly AudioFormat _outputFormat;

        private readonly bool _rawIsFloat;
        private readonly int _rawBytesPerSample;
        private readonly int _rawChannels;
        private readonly int _rawBlockAlign;

        private readonly object _disposeSync = new object();
        private byte[]? _transferBuffer;
        private byte[]? _outputBuffer;
        private bool _disposed;

        private WasapiAudioCapture(
            CoreAudioInterop.IMMDeviceEnumerator enumerator,
            CoreAudioInterop.IMMDevice device,
            CoreAudioInterop.IAudioClient audioClient,
            CoreAudioInterop.IAudioCaptureClient captureClient,
            IntPtr eventHandle,
            AudioFormat rawFormat,
            AudioFormat outputFormat)
        {
            _enumerator = enumerator;
            _device = device;
            _audioClient = audioClient;
            _captureClient = captureClient;
            _eventHandle = eventHandle;
            _rawFormat = rawFormat;
            _outputFormat = outputFormat;
            _rawIsFloat = rawFormat.IsFloat;
            _rawChannels = Math.Max(1, rawFormat.Channels);
            _rawBytesPerSample = Math.Max(1, rawFormat.BitsPerSample / 8);
            _rawBlockAlign = rawFormat.BlockAlign > 0 ? rawFormat.BlockAlign : _rawChannels * _rawBytesPerSample;
        }

        // Public format = the normalized (PCM16 stereo) format the writer must declare.
        public AudioFormat Format => _outputFormat;

        public static bool TryCreateForWindow(IntPtr windowHandle, out WasapiAudioCapture? capture, out string? failureReason)
        {
            capture = null;
            failureReason = null;

            CoreAudioInterop.IMMDeviceEnumerator? enumerator = null;
            CoreAudioInterop.IMMDevice? device = null;
            CoreAudioInterop.IAudioClient? audioClient = null;
            CoreAudioInterop.IAudioCaptureClient? captureClient = null;
            IntPtr eventHandle = IntPtr.Zero;

            try
            {
                enumerator = Activator.CreateInstance(typeof(CoreAudioInterop.MMDeviceEnumeratorComObject)) as CoreAudioInterop.IMMDeviceEnumerator;
                if (enumerator is null)
                {
                    throw new InvalidOperationException("Failed to create IMMDeviceEnumerator instance.");
                }
                CoreAudioInterop.CheckHr(
                    enumerator.GetDefaultAudioEndpoint(CoreAudioInterop.EDataFlow.Render, CoreAudioInterop.ERole.Console, out device),
                    "IMMDeviceEnumerator.GetDefaultAudioEndpoint");

                CoreAudioInterop.CheckHr(
                    device.Activate(typeof(CoreAudioInterop.IAudioClient).GUID, CoreAudioInterop.CLSCTX_ALL, IntPtr.Zero, out var audioClientObj),
                    "IMMDevice.Activate(IAudioClient)");
                audioClient = (CoreAudioInterop.IAudioClient)audioClientObj;

                CoreAudioInterop.CheckHr(audioClient.GetMixFormat(out var mixFormatPtr), "IAudioClient.GetMixFormat");
                try
                {
                    var formatInfo = CoreAudioInterop.ParseWaveFormat(mixFormatPtr);
                    AudioFormat rawFormat = formatInfo.AudioFormat;

                    // Media Foundation's AAC encoder MFT only enumerates 44100 / 48000 Hz.
                    // If the device runs at any other shared-mode rate we cannot reliably
                    // hand audio to the SinkWriter, so disable audio capture instead of
                    // letting SetInputMediaType(Audio) fail later.
                    if (rawFormat.SampleRate != 44100 && rawFormat.SampleRate != 48000)
                    {
                        failureReason = $"Unsupported audio sample rate {rawFormat.SampleRate} Hz for built-in recorder.";
                        return false;
                    }

                    if (rawFormat.Channels <= 0 || rawFormat.BitsPerSample <= 0)
                    {
                        failureReason = "WASAPI mix format reported invalid channel/bit-depth values.";
                        return false;
                    }

                    long bufferDuration = 10_000_000; // 1 second

                    CoreAudioInterop.CheckHr(
                        audioClient.Initialize(
                            CoreAudioInterop.AUDCLNT_SHAREMODE_SHARED,
                            CoreAudioInterop.AUDCLNT_STREAMFLAGS_LOOPBACK | CoreAudioInterop.AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                            bufferDuration,
                            0,
                            mixFormatPtr,
                            Guid.Empty),
                        "IAudioClient.Initialize");

                    CoreAudioInterop.CheckHr(audioClient.GetBufferSize(out _), "IAudioClient.GetBufferSize");

                    eventHandle = CoreAudioInterop.CreateEvent(IntPtr.Zero, false, false, null);
                    if (eventHandle == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("Failed to create audio capture event handle.");
                    }

                    CoreAudioInterop.CheckHr(audioClient.SetEventHandle(eventHandle), "IAudioClient.SetEventHandle");

                    Guid iid = typeof(CoreAudioInterop.IAudioCaptureClient).GUID;
                    CoreAudioInterop.CheckHr(
                        audioClient.GetService(ref iid, out var captureObj),
                        "IAudioClient.GetService(IAudioCaptureClient)");
                    captureClient = (CoreAudioInterop.IAudioCaptureClient)captureObj;

                    // Build the normalized output format the handler / SinkWriter will see.
                    var outputFormat = new AudioFormat(
                        sampleRate: rawFormat.SampleRate,
                        channels: 2,
                        bitsPerSample: 16,
                        blockAlign: 4,
                        subFormat: CoreAudioInterop.KSDATAFORMAT_SUBTYPE_PCM,
                        isFloat: false,
                        validBitsPerSample: 16,
                        channelMask: 0x3u /* SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT */);

                    capture = new WasapiAudioCapture(enumerator, device, audioClient, captureClient, eventHandle, rawFormat, outputFormat);
                    enumerator = null;
                    device = null;
                    audioClient = null;
                    captureClient = null;
                    eventHandle = IntPtr.Zero;
                    return true;
                }
                finally
                {
                    if (mixFormatPtr != IntPtr.Zero)
                    {
                        Marshal.FreeCoTaskMem(mixFormatPtr);
                    }
                }
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                capture?.Dispose();
                capture = null;
                return false;
            }
            finally
            {
                if (eventHandle != IntPtr.Zero)
                {
                    CoreAudioInterop.CloseHandle(eventHandle);
                }

                if (captureClient != null)
                {
                    Marshal.ReleaseComObject(captureClient);
                }

                if (audioClient != null)
                {
                    Marshal.ReleaseComObject(audioClient);
                }

                if (device != null)
                {
                    Marshal.ReleaseComObject(device);
                }

                if (enumerator != null)
                {
                    Marshal.ReleaseComObject(enumerator);
                }
            }
        }

        public delegate void AudioBufferHandler(ReadOnlySpan<byte> buffer, int frames);

        public Task CaptureAsync(AudioBufferHandler handler, CancellationToken token)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            RunCapture(handler, token);
            return Task.CompletedTask;
        }

        private void RunCapture(AudioBufferHandler handler, CancellationToken token)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WasapiAudioCapture));
            }

            CoreAudioInterop.CheckHr(_audioClient.Start(), "IAudioClient.Start");

            try
            {
                while (!token.IsCancellationRequested)
                {
                    uint waitResult = CoreAudioInterop.WaitForSingleObject(_eventHandle, 2000);
                    if (waitResult == CoreAudioInterop.WAIT_OBJECT_0)
                    {
                        DrainPackets(handler, token);
                    }
                    else if (waitResult == CoreAudioInterop.WAIT_TIMEOUT)
                    {
                        continue;
                    }
                    else
                    {
                        throw new InvalidOperationException("Waiting for audio samples failed.");
                    }
                }
            }
            finally
            {
                _audioClient.Stop();
            }
        }

        private void DrainPackets(AudioBufferHandler handler, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                CoreAudioInterop.CheckHr(_captureClient.GetNextPacketSize(out var framesInNextPacket), "IAudioCaptureClient.GetNextPacketSize");
                if (framesInNextPacket == 0)
                {
                    return;
                }

                IntPtr buffer;
                CoreAudioInterop.AudioClientBufferFlags flags;
                long devicePosition;
                long qpcPosition;
                CoreAudioInterop.CheckHr(
                    _captureClient.GetBuffer(out buffer, out var framesAvailable, out flags, out devicePosition, out qpcPosition),
                    "IAudioCaptureClient.GetBuffer");

                try
                {
                    int rawBytes = framesAvailable * _rawBlockAlign;
                    if (rawBytes <= 0 || framesAvailable <= 0)
                    {
                        continue;
                    }

                    int outputBytes = framesAvailable * _outputFormat.BlockAlign;
                    EnsureBufferCapacity(ref _outputBuffer, outputBytes);

                    if ((flags & CoreAudioInterop.AudioClientBufferFlags.Silent) != 0)
                    {
                        Array.Clear(_outputBuffer!, 0, outputBytes);
                    }
                    else
                    {
                        EnsureBufferCapacity(ref _transferBuffer, rawBytes);
                        Marshal.Copy(buffer, _transferBuffer!, 0, rawBytes);
                        ConvertToPcm16Stereo(_transferBuffer!, framesAvailable, _outputBuffer!);
                    }

                    handler(_outputBuffer.AsSpan(0, outputBytes), framesAvailable);
                }
                finally
                {
                    CoreAudioInterop.CheckHr(_captureClient.ReleaseBuffer(framesAvailable), "IAudioCaptureClient.ReleaseBuffer");
                }
            }
        }

        // Convert WASAPI raw bytes to interleaved PCM16 stereo, downmixing or
        // up-mixing as needed. Frame count is preserved.
        private void ConvertToPcm16Stereo(byte[] rawBuffer, int frames, byte[] destination)
        {
            int rawBlock = _rawBlockAlign;
            int channels = _rawChannels;
            int bytesPerSample = _rawBytesPerSample;

            int destOffset = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                int frameOffset = frame * rawBlock;

                float left;
                float right;
                if (channels == 1)
                {
                    float mono = ReadSampleAsFloat(rawBuffer, frameOffset, bytesPerSample);
                    left = mono;
                    right = mono;
                }
                else
                {
                    // Use the first two channels (L, R). For 5.1/7.1 sources this
                    // discards surround content, but it guarantees a clean stereo
                    // signal that any AAC encoder MFT accepts. A proper downmix
                    // matrix can be added later if needed.
                    left = ReadSampleAsFloat(rawBuffer, frameOffset, bytesPerSample);
                    right = ReadSampleAsFloat(rawBuffer, frameOffset + bytesPerSample, bytesPerSample);
                }

                short pcmLeft = ToInt16(left);
                short pcmRight = ToInt16(right);

                destination[destOffset] = (byte)(pcmLeft & 0xFF);
                destination[destOffset + 1] = (byte)((pcmLeft >> 8) & 0xFF);
                destination[destOffset + 2] = (byte)(pcmRight & 0xFF);
                destination[destOffset + 3] = (byte)((pcmRight >> 8) & 0xFF);
                destOffset += 4;
            }
        }

        private float ReadSampleAsFloat(byte[] buffer, int offset, int bytesPerSample)
        {
            if (_rawIsFloat)
            {
                if (bytesPerSample == 4)
                {
                    return BitConverter.ToSingle(buffer, offset);
                }
                if (bytesPerSample == 8)
                {
                    return (float)BitConverter.ToDouble(buffer, offset);
                }

                return 0f;
            }

            switch (bytesPerSample)
            {
                case 1:
                    {
                        // 8-bit PCM is unsigned in WAV/WASAPI.
                        int u = buffer[offset];
                        return (u - 128) / 128f;
                    }
                case 2:
                    {
                        short s = (short)(buffer[offset] | (buffer[offset + 1] << 8));
                        return s / 32768f;
                    }
                case 3:
                    {
                        int s24 = buffer[offset]
                                  | (buffer[offset + 1] << 8)
                                  | ((sbyte)buffer[offset + 2] << 16);
                        return s24 / 8388608f;
                    }
                case 4:
                    {
                        int s32 = buffer[offset]
                                  | (buffer[offset + 1] << 8)
                                  | (buffer[offset + 2] << 16)
                                  | (buffer[offset + 3] << 24);
                        return s32 / 2147483648f;
                    }
                default:
                    return 0f;
            }
        }

        private static short ToInt16(float value)
        {
            float scaled = value * 32767f;
            if (scaled > 32767f)
            {
                return short.MaxValue;
            }

            if (scaled < -32768f)
            {
                return short.MinValue;
            }

            return (short)scaled;
        }

        public void Dispose()
        {
            lock (_disposeSync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            try
            {
                _audioClient.Stop();
            }
            catch
            {
            }

            if (_eventHandle != IntPtr.Zero)
            {
                CoreAudioInterop.CloseHandle(_eventHandle);
            }

            Marshal.ReleaseComObject(_captureClient);
            Marshal.ReleaseComObject(_audioClient);
            Marshal.ReleaseComObject(_device);
            Marshal.ReleaseComObject(_enumerator);
        }

        private static void EnsureBufferCapacity(ref byte[]? buffer, int required)
        {
            if (buffer == null || buffer.Length < required)
            {
                buffer = new byte[required];
            }
        }
    }
}
