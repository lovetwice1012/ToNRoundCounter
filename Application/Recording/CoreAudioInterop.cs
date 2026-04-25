#nullable enable

using System;
using System.Runtime.InteropServices;

namespace ToNRoundCounter.Application.Recording
{
    internal static class CoreAudioInterop
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

        // IAudioEndpointVolume gives access to the Windows per-endpoint master volume
        // slider. This is essential for loopback capture: WASAPI's capture client
        // returns samples at their FULL pre-mix-volume level (i.e. independent of the
        // slider), so a recording made while the user is listening at 20% master
        // volume would play back 5x louder than the user heard live. Multiplying the
        // captured samples by GetMasterVolumeLevelScalar() restores the signal the
        // user actually heard, dynamically following slider changes without needing
        // any fixed compensation constant.
        [ComImport]
        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint channelCount);
            [PreserveSig] int SetMasterVolumeLevel(float levelDb, Guid eventContext);
            [PreserveSig] int SetMasterVolumeLevelScalar(float levelScalar, Guid eventContext);
            [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float levelScalar);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, Guid eventContext);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float levelScalar, Guid eventContext);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float levelScalar);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid eventContext);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
            [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
            [PreserveSig] int VolumeStepUp(Guid eventContext);
            [PreserveSig] int VolumeStepDown(Guid eventContext);
            [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
            [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
        }

        // IAudioMeterInformation reports the peak value of the signal actually sent to
        // the endpoint AFTER every stage of the audio engine (master volume, per-app
        // session volume, loudness equalization, bass boost, etc.). Because WASAPI
        // loopback taps the mix BEFORE master volume and enhancements, comparing our
        // captured-buffer peak against the meter's live peak gives the exact scalar
        // needed to make the recording match what the user heard. This is the only
        // measurement that handles all Windows audio-processing paths uniformly.
        [ComImport]
        [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioMeterInformation
        {
            [PreserveSig] int GetPeakValue(out float peak);
            [PreserveSig] int GetMeteringChannelCount(out uint channelCount);
            [PreserveSig] int GetChannelsPeakValues(uint channelCount, [Out] float[] peakValues);
            [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
        }

        // ---- Process Loopback support (Windows 10 build 20348+ / Windows 11) ----
        //
        // Process loopback is the modern API used by OBS / Discord / etc. to capture
        // audio for a specific process tree WITHOUT going through the system mix.
        // This bypasses every per-endpoint Windows audio enhancement (Loudness
        // Equalization, Bass Boost, exclusive-mode session compressors, etc.) which
        // are the root cause of "音割れ" reports where the captured WAV shows extreme
        // dynamic range compression (crest factor ~ 1.2) even though no clipping is
        // present and the source content has wide natural dynamics.
        //
        // The activation flow is:
        //   1. Build an AUDIOCLIENT_ACTIVATION_PARAMS struct identifying the target PID
        //   2. Wrap it in a PROPVARIANT (VT_BLOB) and pass it to ActivateAudioInterfaceAsync
        //   3. The async completion handler hands back an IAudioClient bound to the
        //      virtual `VAD\Process_Loopback` endpoint.
        //   4. Initialize that client with PCM/float 48 kHz stereo + LOOPBACK | EVENTCALLBACK
        //   5. Drive the same capture loop as the legacy WASAPI loopback path.

        public const string VirtualAudioDeviceProcessLoopback = "VAD\\Process_Loopback";

        public const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
        public const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;

        public static readonly Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");

        public enum AUDIOCLIENT_ACTIVATION_TYPE
        {
            Default = 0,
            ProcessLoopback = 1,
        }

        public enum PROCESS_LOOPBACK_MODE
        {
            IncludeTargetProcessTree = 0,
            ExcludeTargetProcessTree = 1,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
        {
            public uint TargetProcessId;
            public PROCESS_LOOPBACK_MODE ProcessLoopbackMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AUDIOCLIENT_ACTIVATION_PARAMS
        {
            public AUDIOCLIENT_ACTIVATION_TYPE ActivationType;
            public AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS ProcessLoopbackParams;
        }

        // PROPVARIANT for VT_BLOB. Layout MUST match Win32 PROPVARIANT exactly: 24 bytes on x64.
        //   Offsets: vt=0(2), wReserved1=2(2), wReserved2=4(2), wReserved3=6(2),
        //            blob.cbSize=8(4), padding=12(4), blob.pBlobData=16(8) -> total 24.
        // No trailing padding: the audio service does not read past offset 24 for VT_BLOB,
        // and adding trailing fields silently grows the struct and is just wrong.
        [StructLayout(LayoutKind.Sequential, Size = 24)]
        public struct PROPVARIANT_BLOB
        {
            public ushort vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            public uint cbSize;
            public uint cbSize_padding; // align pBlobData on 8-byte boundary for x64
            public IntPtr pBlobData;
        }

        public const ushort VT_BLOB = 0x0041;

        [ComImport]
        [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IActivateAudioInterfaceAsyncOperation
        {
            [PreserveSig]
            int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
        }

        [ComImport]
        [Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IActivateAudioInterfaceCompletionHandler
        {
            [PreserveSig]
            int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
        }

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
        public static extern void ActivateAudioInterfaceAsync(
            [In, MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            [In] ref Guid riid,
            [In] IntPtr activationParams,
            [In] IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);
    }
}
