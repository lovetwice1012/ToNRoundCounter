#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ToNRoundCounter.Application
{
    public sealed partial class AutoRecordingService
    {
        private sealed class WasapiAudioCapture : IDisposable
        {
            private readonly CoreAudioInterop.IMMDeviceEnumerator _enumerator;
            private readonly CoreAudioInterop.IMMDevice _device;
            private readonly CoreAudioInterop.IAudioClient _audioClient;
            private readonly CoreAudioInterop.IAudioCaptureClient _captureClient;
            private readonly IntPtr _eventHandle;
            private readonly AudioFormat _format;
            private readonly object _disposeSync = new object();
            private byte[]? _transferBuffer;
            private byte[]? _silenceBuffer;
            private bool _disposed;

            private WasapiAudioCapture(
                CoreAudioInterop.IMMDeviceEnumerator enumerator,
                CoreAudioInterop.IMMDevice device,
                CoreAudioInterop.IAudioClient audioClient,
                CoreAudioInterop.IAudioCaptureClient captureClient,
                IntPtr eventHandle,
                AudioFormat format)
            {
                _enumerator = enumerator;
                _device = device;
                _audioClient = audioClient;
                _captureClient = captureClient;
                _eventHandle = eventHandle;
                _format = format;
            }

            public AudioFormat Format => _format;

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
                    enumerator = (CoreAudioInterop.IMMDeviceEnumerator)Activator.CreateInstance(typeof(CoreAudioInterop.MMDeviceEnumeratorComObject));
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

                        capture = new WasapiAudioCapture(enumerator, device, audioClient, captureClient, eventHandle, formatInfo.AudioFormat);
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
                        int bytes = framesAvailable * _format.BlockAlign;
                        if (bytes <= 0)
                        {
                            continue;
                        }

                        if ((flags & CoreAudioInterop.AudioClientBufferFlags.Silent) != 0)
                        {
                            EnsureBufferCapacity(ref _silenceBuffer, bytes);
                            Array.Clear(_silenceBuffer!, 0, bytes);
                            handler(_silenceBuffer.AsSpan(0, bytes), framesAvailable);
                        }
                        else
                        {
                            EnsureBufferCapacity(ref _transferBuffer, bytes);
                            Marshal.Copy(buffer, _transferBuffer!, 0, bytes);
                            handler(_transferBuffer.AsSpan(0, bytes), framesAvailable);
                        }
                    }
                    finally
                    {
                        CoreAudioInterop.CheckHr(_captureClient.ReleaseBuffer(framesAvailable), "IAudioCaptureClient.ReleaseBuffer");
                    }
                }
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

        private static class CoreAudioInterop
        {
            public const uint CLSCTX_ALL = 23;
            public const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
            public const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
            public const uint AUDCLNT_SHAREMODE_SHARED = 0;
            public const uint WAIT_OBJECT_0 = 0x00000000;
            public const uint WAIT_TIMEOUT = 0x00000102;
            public const ushort WAVE_FORMAT_PCM = 1;
            public const ushort WAVE_FORMAT_IEEE_FLOAT = 3;
            public const ushort WAVE_FORMAT_EXTENSIBLE = 0xFFFE;

            public static readonly Guid KSDATAFORMAT_SUBTYPE_PCM = new Guid("00000001-0000-0010-8000-00AA00389B71");
            public static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT = new Guid("00000003-0000-0010-8000-00AA00389B71");

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool CloseHandle(IntPtr hObject);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

            public static void CheckHr(int hr, string message)
            {
                if (hr < 0)
                {
                    throw new InvalidOperationException($"{message} failed with HRESULT 0x{hr:X8}.");
                }
            }

            public static WaveFormatInfo ParseWaveFormat(IntPtr pointer)
            {
                if (pointer == IntPtr.Zero)
                {
                    throw new ArgumentNullException(nameof(pointer));
                }

                var baseFormat = Marshal.PtrToStructure<WAVEFORMATEX>(pointer);
                if (baseFormat.wFormatTag == WAVE_FORMAT_EXTENSIBLE && baseFormat.cbSize >= Marshal.SizeOf<WAVEFORMATEXTENSIBLE>() - Marshal.SizeOf<WAVEFORMATEX>())
                {
                    var extensible = Marshal.PtrToStructure<WAVEFORMATEXTENSIBLE>(pointer);
                    bool isFloat = extensible.SubFormat == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
                    int validBits = extensible.Samples.wValidBitsPerSample != 0 ? extensible.Samples.wValidBitsPerSample : baseFormat.wBitsPerSample;
                    var audioFormat = new AudioFormat(
                        (int)baseFormat.nSamplesPerSec,
                        baseFormat.nChannels,
                        baseFormat.wBitsPerSample,
                        baseFormat.nBlockAlign,
                        extensible.SubFormat,
                        isFloat,
                        validBits,
                        extensible.dwChannelMask);
                    return new WaveFormatInfo(baseFormat, audioFormat);
                }
                else
                {
                    Guid subFormat = baseFormat.wFormatTag switch
                    {
                        WAVE_FORMAT_IEEE_FLOAT => KSDATAFORMAT_SUBTYPE_IEEE_FLOAT,
                        _ => KSDATAFORMAT_SUBTYPE_PCM
                    };

                    bool isFloat = subFormat == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
                    var audioFormat = new AudioFormat(
                        (int)baseFormat.nSamplesPerSec,
                        baseFormat.nChannels,
                        baseFormat.wBitsPerSample,
                        baseFormat.nBlockAlign,
                        subFormat,
                        isFloat,
                        baseFormat.wBitsPerSample,
                        0);
                    return new WaveFormatInfo(baseFormat, audioFormat);
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct WAVEFORMATEX
            {
                public ushort wFormatTag;
                public ushort nChannels;
                public uint nSamplesPerSec;
                public uint nAvgBytesPerSec;
                public ushort nBlockAlign;
                public ushort wBitsPerSample;
                public ushort cbSize;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct WAVEFORMATEXTENSIBLE
            {
                public WAVEFORMATEX Format;
                public SamplesUnion Samples;
                public uint dwChannelMask;
                public Guid SubFormat;

                [StructLayout(LayoutKind.Explicit)]
                public struct SamplesUnion
                {
                    [FieldOffset(0)]
                    public ushort wValidBitsPerSample;
                    [FieldOffset(0)]
                    public ushort wSamplesPerBlock;
                    [FieldOffset(0)]
                    public ushort wReserved;
                }
            }

            public readonly struct WaveFormatInfo
            {
                public WaveFormatInfo(WAVEFORMATEX waveFormat, AudioFormat format)
                {
                    WaveFormat = waveFormat;
                    AudioFormat = format;
                }

                public WAVEFORMATEX WaveFormat { get; }

                public AudioFormat AudioFormat { get; }
            }

            [ComImport]
            [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
            public sealed class MMDeviceEnumeratorComObject
            {
            }

            public enum EDataFlow
            {
                Render,
                Capture,
                All,
            }

            public enum ERole
            {
                Console,
                Multimedia,
                Communications,
            }

            [Flags]
            public enum AudioClientBufferFlags
            {
                None = 0,
                DataDiscontinuity = 0x1,
                Silent = 0x2,
                TimestampError = 0x4,
            }

            [ComImport]
            [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            public interface IMMDeviceEnumerator
            {
                [PreserveSig]
                int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out object devices);

                [PreserveSig]
                int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);

                [PreserveSig]
                int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

                [PreserveSig]
                int RegisterEndpointNotificationCallback(IntPtr client);

                [PreserveSig]
                int UnregisterEndpointNotificationCallback(IntPtr client);
            }

            [ComImport]
            [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            public interface IMMDevice
            {
                [PreserveSig]
                int Activate([MarshalAs(UnmanagedType.LPStruct)] Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);

                [PreserveSig]
                int OpenPropertyStore(uint stgmAccess, out IntPtr properties);

                [PreserveSig]
                int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

                [PreserveSig]
                int GetState(out uint state);
            }

            [ComImport]
            [Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            public interface IAudioClient
            {
                [PreserveSig]
                int Initialize(uint shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr format, Guid audioSessionGuid);

                [PreserveSig]
                int GetBufferSize(out uint bufferFrameCount);

                [PreserveSig]
                int GetStreamLatency(out long latency);

                [PreserveSig]
                int GetCurrentPadding(out uint currentPadding);

                [PreserveSig]
                int IsFormatSupported(uint shareMode, IntPtr format, IntPtr closestMatch);

                [PreserveSig]
                int GetMixFormat(out IntPtr deviceFormat);

                [PreserveSig]
                int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);

                [PreserveSig]
                int Start();

                [PreserveSig]
                int Stop();

                [PreserveSig]
                int Reset();

                [PreserveSig]
                int SetEventHandle(IntPtr eventHandle);

                [PreserveSig]
                int GetService(ref Guid serviceId, [MarshalAs(UnmanagedType.IUnknown)] out object service);
            }

            [ComImport]
            [Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            public interface IAudioCaptureClient
            {
                [PreserveSig]
                int GetBuffer(out IntPtr data, out int numFramesRead, out AudioClientBufferFlags flags, out long devicePosition, out long qpcPosition);

                [PreserveSig]
                int ReleaseBuffer(int numFramesRead);

                [PreserveSig]
                int GetNextPacketSize(out int numFramesInNextPacket);
            }
        }
    }
}
