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
}
