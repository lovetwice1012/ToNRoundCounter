#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ToNRoundCounter.Application.Recording
{
    internal sealed class WasapiAudioCapture : IDisposable
    {
        private readonly CoreAudioInterop.IMMDeviceEnumerator? _enumerator;
        private readonly CoreAudioInterop.IMMDevice? _device;
        private readonly CoreAudioInterop.IAudioClient _audioClient;
        private readonly CoreAudioInterop.IAudioCaptureClient _captureClient;
        private readonly IntPtr _eventHandle;

        // True when this capture is sourced from process loopback (Windows 11) instead of
        // the legacy default-render-endpoint loopback. Process loopback bypasses the system
        // audio engine, so the captured stream is not affected by Loudness Equalization,
        // Bass Boost, or any other endpoint APO.
        private readonly bool _isProcessLoopback;

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
        private readonly ChannelMixContribution[] _channelMix;
        private readonly bool _simpleStereoPassthrough;
        // Capture path is intentionally a verbatim copy of the WASAPI shared-mix samples
        // into a 32-bit float WAV. Float WAV can losslessly represent values outside
        // [-1, 1] so we do NOT pre-attenuate here, and we never apply nonlinear shaping
        // (soft-clipping) — any nonlinearity in the capture stage produces audible
        // harmonic distortion ("音割れ") regardless of the final loudness. Loudness and
        // peak safety are handled exclusively in the ffmpeg mux stage.
        private const int DiscontinuityRampFrames = 96;

        // Common WAVEFORMATEXTENSIBLE speaker flags used for explicit downmix routing.
        private const uint SPEAKER_FRONT_LEFT = 0x1;
        private const uint SPEAKER_FRONT_RIGHT = 0x2;
        private const uint SPEAKER_FRONT_CENTER = 0x4;
        private const uint SPEAKER_LOW_FREQUENCY = 0x8;
        private const uint SPEAKER_BACK_LEFT = 0x10;
        private const uint SPEAKER_BACK_RIGHT = 0x20;
        private const uint SPEAKER_FRONT_LEFT_OF_CENTER = 0x40;
        private const uint SPEAKER_FRONT_RIGHT_OF_CENTER = 0x80;
        private const uint SPEAKER_BACK_CENTER = 0x100;
        private const uint SPEAKER_SIDE_LEFT = 0x200;
        private const uint SPEAKER_SIDE_RIGHT = 0x400;
        private const uint SPEAKER_TOP_CENTER = 0x800;
        private const uint SPEAKER_TOP_FRONT_LEFT = 0x1000;
        private const uint SPEAKER_TOP_FRONT_CENTER = 0x2000;
        private const uint SPEAKER_TOP_FRONT_RIGHT = 0x4000;
        private const uint SPEAKER_TOP_BACK_LEFT = 0x8000;
        private const uint SPEAKER_TOP_BACK_CENTER = 0x10000;
        private const uint SPEAKER_TOP_BACK_RIGHT = 0x20000;

        private readonly object _disposeSync = new object();
        private byte[]? _transferBuffer;
        private byte[]? _outputBuffer;
        private float _lastOutputLeft;
        private float _lastOutputRight;
        private bool _hasLastOutputSample;
        private long _packetCount;
        private long _capturedFrameCount;
        private long _silentFrameCount;
        private long _dataDiscontinuityPacketCount;
        private long _timestampErrorPacketCount;
        private readonly long _captureStartedTimestamp;
        private bool _disposed;

        private WasapiAudioCapture(
            CoreAudioInterop.IMMDeviceEnumerator? enumerator,
            CoreAudioInterop.IMMDevice? device,
            CoreAudioInterop.IAudioClient audioClient,
            CoreAudioInterop.IAudioCaptureClient captureClient,
            IntPtr eventHandle,
            AudioFormat rawFormat,
            AudioFormat outputFormat,
            bool isProcessLoopback = false)
        {
            _enumerator = enumerator;
            _device = device;
            _audioClient = audioClient;
            _captureClient = captureClient;
            _eventHandle = eventHandle;
            _rawFormat = rawFormat;
            _outputFormat = outputFormat;
            _isProcessLoopback = isProcessLoopback;
            _rawIsFloat = rawFormat.IsFloat;
            _rawChannels = Math.Max(1, rawFormat.Channels);
            _rawBytesPerSample = Math.Max(1, rawFormat.BitsPerSample / 8);
            _rawBlockAlign = rawFormat.BlockAlign > 0 ? rawFormat.BlockAlign : _rawChannels * _rawBytesPerSample;
            _channelMix = BuildChannelMix(rawFormat.ChannelMask, _rawChannels, _rawBytesPerSample, out _simpleStereoPassthrough);
            _captureStartedTimestamp = Stopwatch.GetTimestamp();
        }

        // True when capture is sourced from Windows 11 process loopback (bypasses every
        // endpoint Audio Processing Object — Loudness Equalization, Bass Boost, etc.).
        public bool IsProcessLoopback => _isProcessLoopback;

        // Public format = the normalized (PCM16 stereo) format the writer must declare.
        public AudioFormat Format => _outputFormat;

        // Raw format reported by WASAPI's IAudioClient::GetMixFormat. Surfaced for diagnostics
        // so we can tell whether unexpected loudness shaping (e.g. ultra-narrow dynamic range,
        // pre-clipped waveform) originates from a non-standard endpoint format such as 7.1
        // surround being downmixed, an unusually high bit depth, or a non-float source that
        // would expose a sample-format misinterpretation.
        public AudioFormat RawSourceFormat => _rawFormat;

        // Endpoint device ID of the render device we attached the loopback capture to. Useful
        // when the user has multiple output devices (e.g. realtek + virtual cable) to confirm
        // which physical endpoint is actually being recorded.
        public string? DeviceId
        {
            get
            {
                if (_device == null)
                {
                    return _isProcessLoopback ? CoreAudioInterop.VirtualAudioDeviceProcessLoopback : null;
                }
                try
                {
                    if (_device.GetId(out string? id) == 0)
                    {
                        return id;
                    }
                }
                catch
                {
                }
                return null;
            }
        }

        public CaptureDiagnostics GetDiagnostics()
        {
            long packets = Interlocked.Read(ref _packetCount);
            long capturedFrames = Interlocked.Read(ref _capturedFrameCount);
            long silentFrames = Interlocked.Read(ref _silentFrameCount);
            long discontinuityPackets = Interlocked.Read(ref _dataDiscontinuityPacketCount);
            long timestampErrorPackets = Interlocked.Read(ref _timestampErrorPacketCount);
            double elapsedSeconds = (Stopwatch.GetTimestamp() - _captureStartedTimestamp) / (double)Stopwatch.Frequency;
            return new CaptureDiagnostics(
                packets,
                capturedFrames,
                silentFrames,
                discontinuityPackets,
                timestampErrorPackets,
                _outputFormat.SampleRate,
                elapsedSeconds);
        }

        public string GetDiagnosticsSummary()
        {
            CaptureDiagnostics d = GetDiagnostics();
            var sb = new StringBuilder(384);
            sb.Append("packets=").Append(d.PacketCount)
              .Append(", frames=").Append(d.CapturedFrameCount)
              .Append(", silentFrames=").Append(d.SilentFrameCount)
              .Append(", discontinuityPackets=").Append(d.DataDiscontinuityPacketCount)
              .Append(", timestampErrorPackets=").Append(d.TimestampErrorPacketCount)
              .Append(", discontinuityRate=").Append(d.DiscontinuityRatePercent.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append('%')
              .Append(", activeAudioSeconds=").Append(d.ActiveAudioSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
              .Append(", captureElapsedSeconds=").Append(d.ElapsedSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))
              // Raw WASAPI source format. If a user reports "音割れ" we can verify whether
              // the source itself is being delivered already-clipped (e.g. by Loudness
              // Equalization or a virtual cable doing pre-compression).
              .Append(", rawSource=").Append(_rawFormat.SampleRate).Append("Hz/")
              .Append(_rawFormat.Channels).Append("ch/")
              .Append(_rawFormat.BitsPerSample).Append("bit/")
              .Append(_rawFormat.IsFloat ? "float" : "pcm")
              .Append(", channelMask=0x").Append(_rawFormat.ChannelMask.ToString("X", System.Globalization.CultureInfo.InvariantCulture))
              .Append(", channelMixContribs=").Append(_channelMix.Length)
              .Append(", simpleStereoPassthrough=").Append(_simpleStereoPassthrough ? "1" : "0")
              .Append(", processLoopback=").Append(_isProcessLoopback ? "1" : "0");
            string? id = DeviceId;
            if (!string.IsNullOrEmpty(id))
            {
                sb.Append(", deviceId=").Append(id);
            }
            return sb.ToString();
        }

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

                    // Build the normalized output format the handler / WAV writer will see.
                    // We emit 32-bit IEEE float stereo at the raw sample rate. Keeping the
                    // signal in float all the way to the WAV file (which is then AAC-encoded
                    // by ffmpeg) avoids the hard int16 clamp that produced audible digital
                    // clipping (音割れ) whenever WASAPI's shared-mode mix returned sample
                    // values above +/-1.0 (common when per-app volume or loudness
                    // equalization pushes the engine output hot).
                    var outputFormat = new AudioFormat(
                        sampleRate: rawFormat.SampleRate,
                        channels: 2,
                        bitsPerSample: 32,
                        blockAlign: 8,
                        subFormat: CoreAudioInterop.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT,
                        isFloat: true,
                        validBitsPerSample: 32,
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

        // Capture the audio of a specific process (and its children) using the Windows 11
        // Process Loopback API. The captured stream comes from BEFORE the system audio engine,
        // so it bypasses every endpoint Audio Processing Object — Loudness Equalization,
        // Bass Boost, exclusive-mode session compressors, etc. — which are the documented
        // root cause of "音割れ" / extreme dynamic range compression in legacy WASAPI loopback
        // recordings (typical symptom: crest factor ~ 1.2, indicating the source has been
        // hard-clipped / aggressively compressed by an APO between WASAPI loopback's tap
        // point and what the user actually hears).
        //
        // Returns false ONLY on hard failures: missing OS API (Win10 < 20348), invalid PID,
        // or a fatal HRESULT from the audio service. Notably, an empty / silent target
        // process is NOT a failure — process loopback activation succeeds regardless of
        // whether the target PID currently has an active audio session, and silent frames
        // simply flow until the process starts producing audio. The capture loop tolerates
        // any amount of leading silence.
        //
        // For transient HRESULTs (RESOURCES_INVALIDATED / E_NOTFOUND) we retry up to a few
        // times with short backoff because some game launchers spawn the audio session
        // late (after splash screens, login flows, etc.) and we don't want to take down
        // the entire audio path just because the user hit "record" a millisecond too early.
        public static bool TryCreateForProcess(uint processId, out WasapiAudioCapture? capture, out string? failureReason)
        {
            capture = null;
            failureReason = null;

            if (processId == 0)
            {
                failureReason = "Target process id is zero.";
                return false;
            }

            // Retry on transient HRESULTs. 4 attempts × 250 ms = ~1 s budget — fast enough
            // to be invisible to the user when activation succeeds first try (the common
            // case) but enough to ride out a freshly-launched game's audio init.
            const int maxAttempts = 4;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (TryActivateProcessLoopbackOnce(processId, out capture, out failureReason, out int innerHr))
                {
                    return true;
                }
                bool transient = innerHr == unchecked((int)0x88890004) // AUDCLNT_E_RESOURCES_INVALIDATED
                              || innerHr == unchecked((int)0x80070490) // E_NOTFOUND
                              || innerHr == unchecked((int)0x88890008) // AUDCLNT_E_UNSUPPORTED_FORMAT (some drivers race during early init)
                              || innerHr == unchecked((int)0x8889000F);// AUDCLNT_E_NOT_INITIALIZED variants
                if (!transient || attempt == maxAttempts)
                {
                    return false;
                }
                System.Threading.Thread.Sleep(250);
            }
            return false;
        }

        // Single activation attempt. innerHr is the HRESULT from GetActivateResult (or 0
        // when the failure happened before that point — e.g. timeout, DLL missing).
        private static bool TryActivateProcessLoopbackOnce(uint processId, out WasapiAudioCapture? capture, out string? failureReason, out int innerHr)
        {
            capture = null;
            failureReason = null;
            innerHr = 0;

            CoreAudioInterop.IAudioClient? audioClient = null;
            CoreAudioInterop.IAudioCaptureClient? captureClient = null;
            IntPtr eventHandle = IntPtr.Zero;
            IntPtr formatPtr = IntPtr.Zero;
            IntPtr propvariantPtr = IntPtr.Zero;
            IntPtr activationParamsPtr = IntPtr.Zero;

            try
            {
                // Build AUDIOCLIENT_ACTIVATION_PARAMS for process loopback (include process tree).
                var activationParams = new CoreAudioInterop.AUDIOCLIENT_ACTIVATION_PARAMS
                {
                    ActivationType = CoreAudioInterop.AUDIOCLIENT_ACTIVATION_TYPE.ProcessLoopback,
                    ProcessLoopbackParams = new CoreAudioInterop.AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
                    {
                        TargetProcessId = processId,
                        ProcessLoopbackMode = CoreAudioInterop.PROCESS_LOOPBACK_MODE.IncludeTargetProcessTree,
                    }
                };

                int paramsSize = Marshal.SizeOf<CoreAudioInterop.AUDIOCLIENT_ACTIVATION_PARAMS>();
                activationParamsPtr = Marshal.AllocCoTaskMem(paramsSize);
                Marshal.StructureToPtr(activationParams, activationParamsPtr, false);

                // Wrap in PROPVARIANT (VT_BLOB).
                int pvSize = Marshal.SizeOf<CoreAudioInterop.PROPVARIANT_BLOB>();
                propvariantPtr = Marshal.AllocCoTaskMem(pvSize);
                var propvariant = new CoreAudioInterop.PROPVARIANT_BLOB
                {
                    vt = CoreAudioInterop.VT_BLOB,
                    cbSize = (uint)paramsSize,
                    pBlobData = activationParamsPtr,
                };
                Marshal.StructureToPtr(propvariant, propvariantPtr, false);

                Guid iid = CoreAudioInterop.IID_IAudioClient;
                var handler = new ProcessLoopbackActivationHandler();
                CoreAudioInterop.IActivateAudioInterfaceAsyncOperation asyncOp;
                try
                {
                    CoreAudioInterop.ActivateAudioInterfaceAsync(
                        CoreAudioInterop.VirtualAudioDeviceProcessLoopback,
                        ref iid,
                        propvariantPtr,
                        handler,
                        out asyncOp);
                }
                catch (DllNotFoundException ex)
                {
                    failureReason = $"Process loopback API unavailable on this OS: {ex.Message}.";
                    return false;
                }
                catch (EntryPointNotFoundException ex)
                {
                    failureReason = $"Process loopback API entry point missing on this OS: {ex.Message}.";
                    return false;
                }

                // The activation handler is invoked asynchronously by the audio service.
                // 5 seconds is generous; in practice the call resolves in < 50 ms when the
                // target PID has an active audio session.
                if (!handler.WaitForCompletion(TimeSpan.FromSeconds(5)))
                {
                    failureReason = "Process loopback activation timed out.";
                    GC.KeepAlive(handler);
                    GC.KeepAlive(asyncOp);
                    return false;
                }

                int activateHr = asyncOp.GetActivateResult(out innerHr, out object activatedInterface);
                GC.KeepAlive(handler);
                GC.KeepAlive(asyncOp);
                if (activateHr < 0)
                {
                    failureReason = $"GetActivateResult failed with HRESULT 0x{activateHr:X8}.";
                    return false;
                }
                if (innerHr < 0)
                {
                    failureReason = $"Process loopback activation HRESULT 0x{innerHr:X8} (transient — will retry).";
                    return false;
                }

                audioClient = activatedInterface as CoreAudioInterop.IAudioClient;
                if (audioClient == null)
                {
                    failureReason = "Process loopback activation returned a non-IAudioClient object.";
                    return false;
                }

                // Process loopback REQUIRES that we provide the format ourselves and pass
                // AUTOCONVERTPCM | SRC_DEFAULT_QUALITY so the audio service inserts a
                // resampler/converter between the source mix and our buffer. We choose
                // 48 kHz / float32 / stereo — the universal Windows engine format.
                var format = new CoreAudioInterop.WAVEFORMATEX
                {
                    wFormatTag = CoreAudioInterop.WAVE_FORMAT_IEEE_FLOAT,
                    nChannels = 2,
                    nSamplesPerSec = 48000,
                    wBitsPerSample = 32,
                    nBlockAlign = (ushort)(2 * 4),
                    nAvgBytesPerSec = 48000u * 2u * 4u,
                    cbSize = 0,
                };

                int formatSize = Marshal.SizeOf<CoreAudioInterop.WAVEFORMATEX>();
                formatPtr = Marshal.AllocCoTaskMem(formatSize);
                Marshal.StructureToPtr(format, formatPtr, false);

                // CRITICAL: For process loopback, both hnsBufferDuration AND hnsPeriodicity
                // MUST be 0 (per Microsoft's ApplicationLoopback sample). Passing a non-zero
                // buffer duration causes Initialize to fail with AUDCLNT_E_INVALID_DEVICE_PERIOD
                // (0x88890009) because the virtual process-loopback device has no concept of a
                // device period — the audio service drives buffering from the source mix.
                CoreAudioInterop.CheckHr(
                    audioClient.Initialize(
                        CoreAudioInterop.AUDCLNT_SHAREMODE_SHARED,
                        CoreAudioInterop.AUDCLNT_STREAMFLAGS_LOOPBACK
                            | CoreAudioInterop.AUDCLNT_STREAMFLAGS_EVENTCALLBACK
                            | CoreAudioInterop.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM
                            | CoreAudioInterop.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                        0,
                        0,
                        formatPtr,
                        Guid.Empty),
                    "IAudioClient.Initialize(ProcessLoopback)");

                eventHandle = CoreAudioInterop.CreateEvent(IntPtr.Zero, false, false, null);
                if (eventHandle == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create process-loopback event handle.");
                }
                CoreAudioInterop.CheckHr(audioClient.SetEventHandle(eventHandle), "IAudioClient.SetEventHandle(ProcessLoopback)");

                Guid captureIid = typeof(CoreAudioInterop.IAudioCaptureClient).GUID;
                CoreAudioInterop.CheckHr(
                    audioClient.GetService(ref captureIid, out var captureObj),
                    "IAudioClient.GetService(IAudioCaptureClient,ProcessLoopback)");
                captureClient = (CoreAudioInterop.IAudioCaptureClient)captureObj;

                // Both raw and output formats are 48 kHz / float / stereo for process loopback.
                var pcmFormat = new AudioFormat(
                    sampleRate: 48000,
                    channels: 2,
                    bitsPerSample: 32,
                    blockAlign: 8,
                    subFormat: CoreAudioInterop.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT,
                    isFloat: true,
                    validBitsPerSample: 32,
                    channelMask: 0x3u);

                capture = new WasapiAudioCapture(
                    enumerator: null,
                    device: null,
                    audioClient: audioClient,
                    captureClient: captureClient,
                    eventHandle: eventHandle,
                    rawFormat: pcmFormat,
                    outputFormat: pcmFormat,
                    isProcessLoopback: true);

                audioClient = null;
                captureClient = null;
                eventHandle = IntPtr.Zero;
                return true;
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
                if (formatPtr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(formatPtr);
                }
                if (propvariantPtr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(propvariantPtr);
                }
                if (activationParamsPtr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(activationParamsPtr);
                }
            }
        }

        // Completion handler for ActivateAudioInterfaceAsync. The audio service calls
        // ActivateCompleted on a service thread; we just signal a ManualResetEventSlim so
        // the synchronous helper above can wait on it.
        [ComVisible(true)]
        [Guid("8B1A1A35-7C68-4A4D-9E59-2D33A57B4E26")]
        private sealed class ProcessLoopbackActivationHandler
            : CoreAudioInterop.IActivateAudioInterfaceCompletionHandler
        {
            private readonly ManualResetEventSlim _completed = new ManualResetEventSlim(false);

            public int ActivateCompleted(CoreAudioInterop.IActivateAudioInterfaceAsyncOperation activateOperation)
            {
                _completed.Set();
                return 0;
            }

            public bool WaitForCompletion(TimeSpan timeout) => _completed.Wait(timeout);
        }

        public delegate void AudioBufferHandler(ReadOnlySpan<byte> buffer, int frames);

        public Task CaptureAsync(AudioBufferHandler handler, CancellationToken token)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            // WASAPI loopback capture has hard sub-10ms deadlines: if the thread that calls
            // GetBuffer is delayed past the buffer period, the device overwrites unconsumed
            // samples and flags DataDiscontinuity. The result is audible cracking / 音割れ.
            // Running the capture on the generic ThreadPool is unreliable because a heavy
            // video encode on another pool thread can starve our scheduling. We therefore pin
            // the capture onto a dedicated AboveNormal-priority thread for the whole session.
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    RunCapture(handler, token);
                    tcs.TrySetResult(null);
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled(token);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            })
            {
                Name = "ToN Recorder WASAPI",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };
            thread.Start();
            return tcs.Task;
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
                    Interlocked.Increment(ref _packetCount);
                    Interlocked.Add(ref _capturedFrameCount, framesAvailable);
                    if ((flags & CoreAudioInterop.AudioClientBufferFlags.DataDiscontinuity) != 0)
                    {
                        Interlocked.Increment(ref _dataDiscontinuityPacketCount);
                    }
                    if ((flags & CoreAudioInterop.AudioClientBufferFlags.TimestampError) != 0)
                    {
                        Interlocked.Increment(ref _timestampErrorPacketCount);
                    }

                    int rawBytes = framesAvailable * _rawBlockAlign;
                    if (rawBytes <= 0 || framesAvailable <= 0)
                    {
                        continue;
                    }

                    int outputBytes = framesAvailable * _outputFormat.BlockAlign;
                    EnsureBufferCapacity(ref _outputBuffer, outputBytes);

                    if ((flags & CoreAudioInterop.AudioClientBufferFlags.Silent) != 0)
                    {
                        Interlocked.Add(ref _silentFrameCount, framesAvailable);
                        WriteSilenceFrames(_outputBuffer!, framesAvailable, (flags & CoreAudioInterop.AudioClientBufferFlags.DataDiscontinuity) != 0);
                    }
                    else
                    {
                        EnsureBufferCapacity(ref _transferBuffer, rawBytes);
                        Marshal.Copy(buffer, _transferBuffer!, 0, rawBytes);
                        ConvertToFloat32Stereo(
                            _transferBuffer!,
                            framesAvailable,
                            _outputBuffer!,
                            (flags & CoreAudioInterop.AudioClientBufferFlags.DataDiscontinuity) != 0);
                    }

                    handler(_outputBuffer.AsSpan(0, outputBytes), framesAvailable);
                }
                finally
                {
                    CoreAudioInterop.CheckHr(_captureClient.ReleaseBuffer(framesAvailable), "IAudioCaptureClient.ReleaseBuffer");
                }
            }
        }

        // Convert WASAPI raw bytes to interleaved float32 stereo.
        //
        // Verbatim copy: no gain change, no soft-clip, no compression.
        // The previous implementation applied `value * 0.45` followed by a hyperbolic
        // soft-limiter (`SoftLimitSample`, knee at 0.85, ceiling 0.98). That soft-limiter
        // was the actual source of the audible "音割れ": even though peaks looked safe,
        // every sample whose pre-attenuated magnitude exceeded ~1.89 (i.e. any reasonably
        // hot Windows shared-mix output) was nonlinearly reshaped, which adds harmonic
        // distortion regardless of the final loudness. By writing the raw float samples
        // unchanged into the float32 WAV, we keep the capture losslessly identical to
        // what Windows mixed for the endpoint. Peak safety is enforced in the ffmpeg
        // mux stage by a clean brick-wall limiter (no make-up gain, no ASC).
        // `SanitizeSample` is still applied to filter NaN / Inf that some drivers emit
        // during stream resync (purely defensive, no nonlinear shaping for finite input).
        private void ConvertToFloat32Stereo(byte[] rawBuffer, int frames, byte[] destination, bool smoothPacketStart)
        {
            int rawBlock = _rawBlockAlign;
            int bytesPerSample = _rawBytesPerSample;
            int rampFrames = smoothPacketStart && _hasLastOutputSample ? Math.Min(frames, DiscontinuityRampFrames) : 0;

            int destOffset = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                int frameOffset = frame * rawBlock;

                GetStereoFrame(rawBuffer, frameOffset, bytesPerSample, out float left, out float right);

                float outLeft = AudioSignalProcessor.SanitizeSample(left);
                float outRight = AudioSignalProcessor.SanitizeSample(right);

                if (rampFrames > 0 && frame < rampFrames)
                {
                    // Smooth the packet boundary after discontinuities to reduce click/pop artifacts.
                    float t = (frame + 1f) / rampFrames;
                    outLeft = _lastOutputLeft + ((outLeft - _lastOutputLeft) * t);
                    outRight = _lastOutputRight + ((outRight - _lastOutputRight) * t);
                }

                WriteFloat32(destination, destOffset, outLeft);
                WriteFloat32(destination, destOffset + 4, outRight);
                _lastOutputLeft = outLeft;
                _lastOutputRight = outRight;
                _hasLastOutputSample = true;
                destOffset += 8;
            }
        }

        private void WriteSilenceFrames(byte[] destination, int frames, bool smoothPacketStart)
        {
            int outputBytes = frames * _outputFormat.BlockAlign;
            if (outputBytes <= 0)
            {
                return;
            }

            if (!smoothPacketStart || !_hasLastOutputSample || frames <= 0)
            {
                Array.Clear(destination, 0, outputBytes);
                _lastOutputLeft = 0f;
                _lastOutputRight = 0f;
                _hasLastOutputSample = true;
                return;
            }

            int rampFrames = Math.Min(frames, DiscontinuityRampFrames);
            int destOffset = 0;
            for (int frame = 0; frame < rampFrames; frame++)
            {
                float t = (frame + 1f) / rampFrames;
                float outLeft = _lastOutputLeft * (1f - t);
                float outRight = _lastOutputRight * (1f - t);
                WriteFloat32(destination, destOffset, outLeft);
                WriteFloat32(destination, destOffset + 4, outRight);
                _lastOutputLeft = outLeft;
                _lastOutputRight = outRight;
                _hasLastOutputSample = true;
                destOffset += _outputFormat.BlockAlign;
            }

            if (destOffset < outputBytes)
            {
                Array.Clear(destination, destOffset, outputBytes - destOffset);
            }

            _lastOutputLeft = 0f;
            _lastOutputRight = 0f;
            _hasLastOutputSample = true;
        }

        private void GetStereoFrame(byte[] rawBuffer, int frameOffset, int bytesPerSample, out float left, out float right)
        {
            if (_rawChannels == 1)
            {
                float mono = ReadSampleAsFloat(rawBuffer, frameOffset, bytesPerSample);
                left = mono;
                right = mono;
                return;
            }

            if (_simpleStereoPassthrough)
            {
                left = ReadSampleAsFloat(rawBuffer, frameOffset, bytesPerSample);
                right = ReadSampleAsFloat(rawBuffer, frameOffset + bytesPerSample, bytesPerSample);
                return;
            }

            if (_channelMix.Length == 0)
            {
                // Last-resort fallback to first two channels when no routing info is available.
                left = ReadSampleAsFloat(rawBuffer, frameOffset, bytesPerSample);
                right = ReadSampleAsFloat(rawBuffer, frameOffset + bytesPerSample, bytesPerSample);
                return;
            }

            float mixLeft = 0f;
            float mixRight = 0f;
            for (int i = 0; i < _channelMix.Length; i++)
            {
                ref readonly ChannelMixContribution mix = ref _channelMix[i];
                float sample = ReadSampleAsFloat(rawBuffer, frameOffset + mix.SampleByteOffset, bytesPerSample);
                mixLeft += sample * mix.LeftWeight;
                mixRight += sample * mix.RightWeight;
            }

            left = mixLeft;
            right = mixRight;
        }

        private static ChannelMixContribution[] BuildChannelMix(uint channelMask, int channels, int bytesPerSample, out bool simpleStereoPassthrough)
        {
            simpleStereoPassthrough = channels == 2 && (channelMask == 0 || channelMask == (SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT));
            if (channels <= 1)
            {
                return Array.Empty<ChannelMixContribution>();
            }

            if (simpleStereoPassthrough)
            {
                return Array.Empty<ChannelMixContribution>();
            }

            var contributions = new List<ChannelMixContribution>(channels);
            int index = 0;

            if (channelMask != 0)
            {
                for (int bit = 0; bit < 32 && index < channels; bit++)
                {
                    uint speaker = 1u << bit;
                    if ((channelMask & speaker) == 0)
                    {
                        continue;
                    }

                    (float leftWeight, float rightWeight) = GetSpeakerWeights(speaker, index);
                    contributions.Add(new ChannelMixContribution(index * bytesPerSample, leftWeight, rightWeight));
                    index++;
                }
            }

            while (index < channels)
            {
                (float leftWeight, float rightWeight) = GetFallbackWeights(index);
                contributions.Add(new ChannelMixContribution(index * bytesPerSample, leftWeight, rightWeight));
                index++;
            }

            if (contributions.Count == 0)
            {
                return Array.Empty<ChannelMixContribution>();
            }

            float leftSum = 0f;
            float rightSum = 0f;
            for (int i = 0; i < contributions.Count; i++)
            {
                leftSum += MathF.Abs(contributions[i].LeftWeight);
                rightSum += MathF.Abs(contributions[i].RightWeight);
            }

            float normalizer = Math.Max(1f, Math.Max(leftSum, rightSum));
            for (int i = 0; i < contributions.Count; i++)
            {
                ChannelMixContribution c = contributions[i];
                contributions[i] = new ChannelMixContribution(
                    c.SampleByteOffset,
                    c.LeftWeight / normalizer,
                    c.RightWeight / normalizer);
            }

            return contributions.ToArray();
        }

        private static (float leftWeight, float rightWeight) GetSpeakerWeights(uint speaker, int channelIndex)
        {
            return speaker switch
            {
                SPEAKER_FRONT_LEFT => (1f, 0f),
                SPEAKER_FRONT_RIGHT => (0f, 1f),
                SPEAKER_FRONT_CENTER => (0.70710677f, 0.70710677f),
                SPEAKER_LOW_FREQUENCY => (0.5f, 0.5f),
                SPEAKER_BACK_LEFT => (0.70710677f, 0f),
                SPEAKER_BACK_RIGHT => (0f, 0.70710677f),
                SPEAKER_FRONT_LEFT_OF_CENTER => (0.8660254f, 0f),
                SPEAKER_FRONT_RIGHT_OF_CENTER => (0f, 0.8660254f),
                SPEAKER_BACK_CENTER => (0.5f, 0.5f),
                SPEAKER_SIDE_LEFT => (0.70710677f, 0f),
                SPEAKER_SIDE_RIGHT => (0f, 0.70710677f),
                SPEAKER_TOP_CENTER => (0.5f, 0.5f),
                SPEAKER_TOP_FRONT_LEFT => (0.5f, 0f),
                SPEAKER_TOP_FRONT_CENTER => (0.5f, 0.5f),
                SPEAKER_TOP_FRONT_RIGHT => (0f, 0.5f),
                SPEAKER_TOP_BACK_LEFT => (0.5f, 0f),
                SPEAKER_TOP_BACK_CENTER => (0.5f, 0.5f),
                SPEAKER_TOP_BACK_RIGHT => (0f, 0.5f),
                _ => GetFallbackWeights(channelIndex),
            };
        }

        private static (float leftWeight, float rightWeight) GetFallbackWeights(int channelIndex)
        {
            return channelIndex switch
            {
                0 => (1f, 0f),
                1 => (0f, 1f),
                2 => (0.70710677f, 0.70710677f),
                3 => (0.5f, 0.5f),
                _ => (channelIndex % 2 == 0) ? (0.70710677f, 0f) : (0f, 0.70710677f),
            };
        }

        private readonly struct ChannelMixContribution
        {
            public ChannelMixContribution(int sampleByteOffset, float leftWeight, float rightWeight)
            {
                SampleByteOffset = sampleByteOffset;
                LeftWeight = leftWeight;
                RightWeight = rightWeight;
            }

            public int SampleByteOffset { get; }

            public float LeftWeight { get; }

            public float RightWeight { get; }
        }

        public readonly struct CaptureDiagnostics
        {
            public CaptureDiagnostics(
                long packetCount,
                long capturedFrameCount,
                long silentFrameCount,
                long dataDiscontinuityPacketCount,
                long timestampErrorPacketCount,
                int sampleRate,
                double elapsedSeconds)
            {
                PacketCount = packetCount;
                CapturedFrameCount = capturedFrameCount;
                SilentFrameCount = silentFrameCount;
                DataDiscontinuityPacketCount = dataDiscontinuityPacketCount;
                TimestampErrorPacketCount = timestampErrorPacketCount;
                SampleRate = sampleRate;
                ElapsedSeconds = elapsedSeconds;
            }

            public long PacketCount { get; }

            public long CapturedFrameCount { get; }

            public long SilentFrameCount { get; }

            public long DataDiscontinuityPacketCount { get; }

            public long TimestampErrorPacketCount { get; }

            public int SampleRate { get; }

            public double ElapsedSeconds { get; }

            public double DiscontinuityRatePercent => PacketCount <= 0 ? 0d : (DataDiscontinuityPacketCount * 100d) / PacketCount;

            public double ActiveAudioSeconds
            {
                get
                {
                    long activeFrames = Math.Max(0L, CapturedFrameCount - SilentFrameCount);
                    if (activeFrames <= 0 || SampleRate <= 0)
                    {
                        return 0d;
                    }

                    return activeFrames / (double)SampleRate;
                }
            }
        }

        private static void WriteFloat32(byte[] buffer, int offset, float value)
        {
            // Little-endian IEEE 754 single precision, matching WAVEFORMATEX float layout
            // and ffmpeg's f32le input format. The project is compiled with
            // CheckForOverflowUnderflow=true, so a straight (uint) cast of a negative Int32
            // (which is the case for any float with the sign bit set - i.e. half of all audio
            // samples) would throw OverflowException and kill the capture loop. Use the
            // dedicated UInt32 bit-cast helper which performs a reinterpret rather than an
            // arithmetic conversion.
            uint bits = BitConverter.SingleToUInt32Bits(value);
            buffer[offset] = (byte)(bits & 0xFF);
            buffer[offset + 1] = (byte)((bits >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((bits >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((bits >> 24) & 0xFF);
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
            if (_device != null)
            {
                Marshal.ReleaseComObject(_device);
            }
            if (_enumerator != null)
            {
                Marshal.ReleaseComObject(_enumerator);
            }
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
