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
    }
}
