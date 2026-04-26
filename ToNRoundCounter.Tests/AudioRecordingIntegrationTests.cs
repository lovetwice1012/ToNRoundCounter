using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using ToNRoundCounter.Application.Recording;
using Xunit;

namespace ToNRoundCounter.Tests
{
    [Collection("AudioIntegration")]
    public sealed class AudioRecordingIntegrationTests
    {
        [LocalAudioHardwareTheory]
        [InlineData(997.0, 0.95f)]
        [InlineData(523.25, 0.90f)]
        [InlineData(1733.0, 1.00f)]
        public async Task EndToEnd_ActualLoopbackRecording_PreservesToneWithoutSevereClipping(double toneFrequencyHz, float toneAmplitude)
        {
            if (!WasapiAudioCapture.TryCreateForWindow(IntPtr.Zero, out var capture, out var failureReason) || capture == null)
            {
                throw new InvalidOperationException($"WASAPI loopback capture is unavailable on this machine: {failureReason}");
            }

            string tempRoot = Path.Combine(Path.GetTempPath(), "ToNRoundCounter.AudioIntegration", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            string wavCapturePath = Path.Combine(tempRoot, "capture.wav");
            string referenceWavPath = Path.Combine(tempRoot, "reference.wav");
            string mp4Path = Path.Combine(tempRoot, "recording.mp4");
            string decodedMp4Path = Path.Combine(tempRoot, "recording.decoded.wav");

            const int frameRate = 30;
            const int width = 128;
            const int height = 72;
            const double playbackDurationSeconds = 2.4;
            const int leadInMilliseconds = 250;
            const int tailMilliseconds = 350;

            WaveOutEvent? playback = null;
            WavAudioWriter? wavWriter = null;
            AudioMuxingMediaWriter? muxWriter = null;
            CancellationTokenSource? wavCaptureCts = null;
            CancellationTokenSource? muxCaptureCts = null;

            try
            {
                var wavResult = await CaptureToneToWavAsync(
                    capture,
                    wavCapturePath,
                    referenceWavPath,
                    toneFrequencyHz,
                    toneAmplitude,
                    playbackDurationSeconds,
                    leadInMilliseconds,
                    tailMilliseconds);

                var wavMetrics = wavResult.Metrics;
                var referenceMetrics = wavResult.ReferenceMetrics;
                wavWriter = wavResult.Writer;
                playback = wavResult.Playback;
                wavCaptureCts = wavResult.CaptureCts;

                capture.Dispose();
                capture = null;

                if (!WasapiAudioCapture.TryCreateForWindow(IntPtr.Zero, out capture, out failureReason) || capture == null)
                {
                    throw new InvalidOperationException($"WASAPI loopback capture could not be reopened for full recording verification: {failureReason}");
                }

                var selection = new HardwareEncoderSelection(HardwareAccelerationApi.Software, 0, 0, false, true);
                var mp4Result = await CaptureToneToMp4Async(
                    capture,
                    mp4Path,
                    decodedMp4Path,
                    selection,
                    toneFrequencyHz,
                    toneAmplitude,
                    playbackDurationSeconds,
                    leadInMilliseconds,
                    tailMilliseconds,
                    frameRate,
                    width,
                    height);

                var mp4Metrics = mp4Result.Metrics;
                muxWriter = mp4Result.Writer;
                playback = mp4Result.Playback;
                muxCaptureCts = mp4Result.CaptureCts;

                string summary = $"tone={toneFrequencyHz:F2}Hz amp={toneAmplitude:F2}; Reference metrics: {referenceMetrics}; WAV metrics: {wavMetrics}; MP4 metrics: {mp4Metrics}";
                Console.WriteLine(summary);

                Assert.True(wavMetrics.ActiveDurationSeconds > 1.0, "WAV capture active signal was too short. " + summary);
                Assert.True(referenceMetrics.ActiveDurationSeconds > 1.0, "Reference loopback active signal was too short. " + summary);
                Assert.True(wavMetrics.CrestFactor >= referenceMetrics.CrestFactor - 0.10, "App WAV capture loses too much crest factor versus the reference loopback capture. " + summary);
                Assert.True(wavMetrics.DistortionRatio <= referenceMetrics.DistortionRatio + 0.10, "App WAV capture adds too much distortion versus the reference loopback capture. " + summary);
                Assert.True(wavMetrics.ClippedSampleRatio <= 0.01, "App WAV capture has too many near-clipped samples. " + summary);

                Assert.True(mp4Metrics.ActiveDurationSeconds > 1.0, "Decoded MP4 active signal was too short. " + summary);
                Assert.True(mp4Metrics.CrestFactor >= wavMetrics.CrestFactor - 0.10, "AAC/MP4 stage loses too much crest factor versus the captured WAV. " + summary);
                Assert.True(mp4Metrics.DistortionRatio <= wavMetrics.DistortionRatio + 0.12, "AAC/MP4 stage added too much extra distortion. " + summary);
                Assert.True(mp4Metrics.ClippedSampleRatio <= 0.01, "Final MP4 audio still contains too many near-clipped samples. " + summary);
            }
            finally
            {
                try { playback?.Stop(); } catch { }
                playback?.Dispose();
                wavCaptureCts?.Cancel();
                muxCaptureCts?.Cancel();
                wavCaptureCts?.Dispose();
                muxCaptureCts?.Dispose();
                wavWriter?.Dispose();
                muxWriter?.Dispose();
                capture?.Dispose();

                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        Directory.Delete(tempRoot, recursive: true);
                    }
                }
                catch
                {
                }
            }
        }

        private static async Task<(AudioMetrics Metrics, AudioMetrics ReferenceMetrics, WavAudioWriter Writer, WaveOutEvent Playback, CancellationTokenSource CaptureCts)> CaptureToneToWavAsync(
            WasapiAudioCapture capture,
            string wavCapturePath,
            string referenceWavPath,
            double toneFrequencyHz,
            float toneAmplitude,
            double playbackDurationSeconds,
            int leadInMilliseconds,
            int tailMilliseconds)
        {
            var wavWriter = new WavAudioWriter(wavCapturePath, capture.Format);
            var captureCts = new CancellationTokenSource();
            using var referenceCapture = ReferenceLoopbackCapture.Create();
            using var referenceWriter = new WavAudioWriter(referenceWavPath, referenceCapture.Format);

            Task captureTask = capture.CaptureAsync((buffer, frames) => wavWriter.Write(buffer, frames), captureCts.Token);
            Task referenceTask = referenceCapture.CaptureAsync((buffer, frames) => referenceWriter.Write(buffer, frames), captureCts.Token);

            await Task.Delay(leadInMilliseconds).ConfigureAwait(false);

            var playback = CreatePlayback(capture.Format.SampleRate, toneFrequencyHz, toneAmplitude, playbackDurationSeconds);
            await PlayToCompletionAsync(playback).ConfigureAwait(false);

            await Task.Delay(tailMilliseconds).ConfigureAwait(false);

            captureCts.Cancel();
            await AwaitCancellationAsync(captureTask).ConfigureAwait(false);
            await AwaitCancellationAsync(referenceTask).ConfigureAwait(false);
            wavWriter.Dispose();
            referenceWriter.Dispose();

            return (
                AnalyzeAudioFile(wavCapturePath, toneFrequencyHz),
                AnalyzeAudioFile(referenceWavPath, toneFrequencyHz),
                wavWriter,
                playback,
                captureCts);
        }

        private static async Task<(AudioMetrics Metrics, AudioMuxingMediaWriter Writer, WaveOutEvent Playback, CancellationTokenSource CaptureCts)> CaptureToneToMp4Async(
            WasapiAudioCapture capture,
            string mp4Path,
            string decodedMp4Path,
            HardwareEncoderSelection selection,
            double toneFrequencyHz,
            float toneAmplitude,
            double playbackDurationSeconds,
            int leadInMilliseconds,
            int tailMilliseconds,
            int frameRate,
            int width,
            int height)
        {
            var muxWriter = AudioMuxingMediaWriter.Create(
                "mp4",
                "h264",
                mp4Path,
                width,
                height,
                frameRate,
                capture.Format,
                videoBitrate: 750_000,
                audioBitrate: 192_000,
                hardwareSelection: selection);

            var captureCts = new CancellationTokenSource();
            Task captureTask = capture.CaptureAsync((buffer, frames) => muxWriter.WriteAudioSample(buffer, frames), captureCts.Token);

            int totalFrames = (int)Math.Ceiling((playbackDurationSeconds + (leadInMilliseconds + tailMilliseconds) / 1000d + 0.4d) * frameRate);
            Task videoTask = WriteBlackVideoFramesAsync(muxWriter, width, height, frameRate, totalFrames);

            await Task.Delay(leadInMilliseconds).ConfigureAwait(false);

            var playback = CreatePlayback(capture.Format.SampleRate, toneFrequencyHz, toneAmplitude, playbackDurationSeconds);
            await PlayToCompletionAsync(playback).ConfigureAwait(false);

            await Task.Delay(tailMilliseconds).ConfigureAwait(false);

            captureCts.Cancel();
            await AwaitCancellationAsync(captureTask).ConfigureAwait(false);
            await videoTask.ConfigureAwait(false);

            muxWriter.CompleteAudio();
            muxWriter.Dispose();

            DecodeMp4AudioToWav(mp4Path, decodedMp4Path);
            return (AnalyzeAudioFile(decodedMp4Path, toneFrequencyHz), muxWriter, playback, captureCts);
        }

        private static WaveOutEvent CreatePlayback(int sampleRate, double toneFrequencyHz, float toneAmplitude, double durationSeconds)
        {
            var provider = new SineWaveSampleProvider(sampleRate, 2, toneFrequencyHz, toneAmplitude, durationSeconds);
            var playback = new WaveOutEvent();
            playback.Init(provider);
            return playback;
        }

        private static Task PlayToCompletionAsync(WaveOutEvent playback)
        {
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler(object? sender, StoppedEventArgs args)
            {
                playback.PlaybackStopped -= Handler;
                if (args.Exception != null)
                {
                    tcs.TrySetException(args.Exception);
                }
                else
                {
                    tcs.TrySetResult(null);
                }
            }

            playback.PlaybackStopped += Handler;
            playback.Play();
            return tcs.Task;
        }

        private static async Task AwaitCancellationAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static async Task WriteBlackVideoFramesAsync(AudioMuxingMediaWriter writer, int width, int height, int frameRate, int totalFrames)
        {
            using var bitmap = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Black);
            }

            long frameDurationHns = 10_000_000 / frameRate;
            for (int index = 0; index < totalFrames; index++)
            {
                writer.WriteVideoFrame(bitmap, index * frameDurationHns);
                await Task.Delay(TimeSpan.FromSeconds(1d / frameRate)).ConfigureAwait(false);
            }
        }

        private static void DecodeMp4AudioToWav(string inputPath, string outputPath)
        {
            string ffmpegPath = FfmpegLocator.Locate();
            string args = $"-hide_banner -loglevel error -y -i \"{inputPath}\" -vn -c:a pcm_f32le \"{outputPath}\"";

            var psi = new ProcessStartInfo(ffmpegPath, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to launch ffmpeg for MP4 audio decode.");
            string stderr = process.StandardError.ReadToEnd();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"ffmpeg decode failed with exit code {process.ExitCode}. stdout={stdout} stderr={stderr}");
            }
        }

        private static AudioMetrics AnalyzeAudioFile(string path, double expectedFrequencyHz)
        {
            using var reader = new AudioFileReader(path);
            int channels = Math.Max(1, reader.WaveFormat.Channels);
            int sampleRate = reader.WaveFormat.SampleRate;

            var monoSamples = new List<float>();
            float[] buffer = new float[sampleRate * channels];
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i + channels - 1 < read; i += channels)
                {
                    float sum = 0f;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        sum += buffer[i + ch];
                    }

                    monoSamples.Add(sum / channels);
                }
            }

            if (monoSamples.Count == 0)
            {
                throw new InvalidOperationException($"No samples were decoded from {path}.");
            }

            int activeStart = FindFirstActiveIndex(monoSamples, 0.01f);
            int activeEnd = FindLastActiveIndex(monoSamples, 0.01f);
            if (activeStart < 0 || activeEnd <= activeStart)
            {
                throw new InvalidOperationException($"No active audio region was found in {path}.");
            }

            int activeCount = activeEnd - activeStart + 1;
            int analysisLength = Math.Min(sampleRate, activeCount);
            int analysisStart = activeStart + Math.Max(0, (activeCount - analysisLength) / 2);

            double sumSquares = 0d;
            double peak = 0d;
            int clippedSamples = 0;
            for (int i = 0; i < analysisLength; i++)
            {
                double sample = monoSamples[analysisStart + i];
                double abs = Math.Abs(sample);
                if (abs > peak)
                {
                    peak = abs;
                }
                if (abs >= 0.985d)
                {
                    clippedSamples++;
                }

                sumSquares += sample * sample;
            }

            double rms = Math.Sqrt(sumSquares / analysisLength);
            if (rms <= 0d || peak <= 0d)
            {
                throw new InvalidOperationException($"Decoded audio from {path} had zero RMS/peak.");
            }

            (double fittedRms, double errorRms) = FitSineAndComputeResidual(monoSamples, analysisStart, analysisLength, sampleRate, expectedFrequencyHz);
            double crestFactor = peak / rms;
            double distortionRatio = fittedRms <= 0d ? 1d : errorRms / fittedRms;
            double clippedSampleRatio = clippedSamples / (double)analysisLength;

            return new AudioMetrics(path, sampleRate, activeCount / (double)sampleRate, peak, rms, crestFactor, distortionRatio, clippedSampleRatio);
        }

        private static (double fittedRms, double errorRms) FitSineAndComputeResidual(IReadOnlyList<float> samples, int start, int length, int sampleRate, double frequencyHz)
        {
            double omega = 2d * Math.PI * frequencyHz / sampleRate;
            double sumSinSin = 0d;
            double sumCosCos = 0d;
            double sumSinCos = 0d;
            double sumXSin = 0d;
            double sumXCos = 0d;

            for (int i = 0; i < length; i++)
            {
                double angle = omega * i;
                double sin = Math.Sin(angle);
                double cos = Math.Cos(angle);
                double x = samples[start + i];

                sumSinSin += sin * sin;
                sumCosCos += cos * cos;
                sumSinCos += sin * cos;
                sumXSin += x * sin;
                sumXCos += x * cos;
            }

            double det = (sumSinSin * sumCosCos) - (sumSinCos * sumSinCos);
            if (Math.Abs(det) < 1e-9)
            {
                throw new InvalidOperationException("Sine fit became singular during audio analysis.");
            }

            double a = ((sumXSin * sumCosCos) - (sumXCos * sumSinCos)) / det;
            double b = ((sumXCos * sumSinSin) - (sumXSin * sumSinCos)) / det;

            double fittedSquares = 0d;
            double errorSquares = 0d;
            for (int i = 0; i < length; i++)
            {
                double angle = omega * i;
                double fitted = (a * Math.Sin(angle)) + (b * Math.Cos(angle));
                double error = samples[start + i] - fitted;
                fittedSquares += fitted * fitted;
                errorSquares += error * error;
            }

            return (Math.Sqrt(fittedSquares / length), Math.Sqrt(errorSquares / length));
        }

        private static int FindFirstActiveIndex(IReadOnlyList<float> samples, float threshold)
        {
            for (int i = 0; i < samples.Count; i++)
            {
                if (Math.Abs(samples[i]) >= threshold)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindLastActiveIndex(IReadOnlyList<float> samples, float threshold)
        {
            for (int i = samples.Count - 1; i >= 0; i--)
            {
                if (Math.Abs(samples[i]) >= threshold)
                {
                    return i;
                }
            }

            return -1;
        }

        private sealed class SineWaveSampleProvider : ISampleProvider
        {
            private readonly int _channels;
            private readonly double _frequencyHz;
            private readonly float _amplitude;
            private readonly long _totalSamplesPerChannel;
            private long _sampleIndex;

            public SineWaveSampleProvider(int sampleRate, int channels, double frequencyHz, float amplitude, double durationSeconds)
            {
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
                _channels = channels;
                _frequencyHz = frequencyHz;
                _amplitude = amplitude;
                _totalSamplesPerChannel = (long)Math.Round(sampleRate * durationSeconds);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                int written = 0;
                int samplesRequestedPerChannel = count / _channels;
                int samplesRemainingPerChannel = (int)Math.Max(0, _totalSamplesPerChannel - _sampleIndex);
                int samplesToWritePerChannel = Math.Min(samplesRequestedPerChannel, samplesRemainingPerChannel);

                for (int i = 0; i < samplesToWritePerChannel; i++)
                {
                    float sample = _amplitude * (float)Math.Sin(2d * Math.PI * _frequencyHz * _sampleIndex / WaveFormat.SampleRate);
                    for (int ch = 0; ch < _channels; ch++)
                    {
                        buffer[offset + written++] = sample;
                    }

                    _sampleIndex++;
                }

                return written;
            }
        }

        private sealed class ReferenceLoopbackCapture : IDisposable
        {
            private readonly CoreAudioInterop.IMMDeviceEnumerator _enumerator;
            private readonly CoreAudioInterop.IMMDevice _device;
            private readonly CoreAudioInterop.IAudioClient _audioClient;
            private readonly CoreAudioInterop.IAudioCaptureClient _captureClient;
            private readonly IntPtr _eventHandle;
            private readonly AudioFormat _rawFormat;
            private readonly int _rawChannels;
            private readonly int _rawBytesPerSample;
            private readonly int _rawBlockAlign;
            private readonly bool _rawIsFloat;
            private byte[]? _transferBuffer;
            private byte[]? _outputBuffer;
            private bool _disposed;

            private ReferenceLoopbackCapture(
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
                Format = outputFormat;
                _rawChannels = Math.Max(1, rawFormat.Channels);
                _rawBytesPerSample = Math.Max(1, rawFormat.BitsPerSample / 8);
                _rawBlockAlign = rawFormat.BlockAlign > 0 ? rawFormat.BlockAlign : _rawChannels * _rawBytesPerSample;
                _rawIsFloat = rawFormat.IsFloat;
            }

            public AudioFormat Format { get; }

            public static ReferenceLoopbackCapture Create()
            {
                CoreAudioInterop.IMMDeviceEnumerator? enumerator = null;
                CoreAudioInterop.IMMDevice? device = null;
                CoreAudioInterop.IAudioClient? audioClient = null;
                CoreAudioInterop.IAudioCaptureClient? captureClient = null;
                IntPtr eventHandle = IntPtr.Zero;

                try
                {
                    enumerator = Activator.CreateInstance(typeof(CoreAudioInterop.MMDeviceEnumeratorComObject)) as CoreAudioInterop.IMMDeviceEnumerator
                        ?? throw new InvalidOperationException("Failed to create IMMDeviceEnumerator instance for reference capture.");
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
                        if (rawFormat.SampleRate != 44100 && rawFormat.SampleRate != 48000)
                        {
                            throw new InvalidOperationException($"Unsupported sample rate {rawFormat.SampleRate} Hz for reference capture.");
                        }

                        var outputFormat = new AudioFormat(
                            sampleRate: rawFormat.SampleRate,
                            channels: 2,
                            bitsPerSample: 32,
                            blockAlign: 8,
                            subFormat: CoreAudioInterop.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT,
                            isFloat: true,
                            validBitsPerSample: 32,
                            channelMask: 0x3u);

                        long bufferDuration = 10_000_000;
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
                            throw new InvalidOperationException("Failed to create event handle for reference capture.");
                        }

                        CoreAudioInterop.CheckHr(audioClient.SetEventHandle(eventHandle), "IAudioClient.SetEventHandle");

                        Guid iid = typeof(CoreAudioInterop.IAudioCaptureClient).GUID;
                        CoreAudioInterop.CheckHr(audioClient.GetService(ref iid, out var captureObj), "IAudioClient.GetService(IAudioCaptureClient)");
                        captureClient = (CoreAudioInterop.IAudioCaptureClient)captureObj;

                        var capture = new ReferenceLoopbackCapture(enumerator, device, audioClient, captureClient, eventHandle, rawFormat, outputFormat);
                        enumerator = null;
                        device = null;
                        audioClient = null;
                        captureClient = null;
                        eventHandle = IntPtr.Zero;
                        return capture;
                    }
                    finally
                    {
                        if (mixFormatPtr != IntPtr.Zero)
                        {
                            Marshal.FreeCoTaskMem(mixFormatPtr);
                        }
                    }
                }
                catch
                {
                    if (eventHandle != IntPtr.Zero)
                    {
                        CoreAudioInterop.CloseHandle(eventHandle);
                    }
                    if (captureClient != null) Marshal.ReleaseComObject(captureClient);
                    if (audioClient != null) Marshal.ReleaseComObject(audioClient);
                    if (device != null) Marshal.ReleaseComObject(device);
                    if (enumerator != null) Marshal.ReleaseComObject(enumerator);
                    throw;
                }
            }

            public Task CaptureAsync(WasapiAudioCapture.AudioBufferHandler handler, CancellationToken token)
            {
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
                    IsBackground = true,
                    Name = "ToN Reference WASAPI",
                    Priority = ThreadPriority.AboveNormal,
                };
                thread.Start();
                return tcs.Task;
            }

            private void RunCapture(WasapiAudioCapture.AudioBufferHandler handler, CancellationToken token)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(ReferenceLoopbackCapture));
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
                        else if (waitResult != CoreAudioInterop.WAIT_TIMEOUT)
                        {
                            throw new InvalidOperationException("Waiting for reference audio samples failed.");
                        }
                    }
                }
                finally
                {
                    _audioClient.Stop();
                }
            }

            private void DrainPackets(WasapiAudioCapture.AudioBufferHandler handler, CancellationToken token)
            {
                while (!token.IsCancellationRequested)
                {
                    CoreAudioInterop.CheckHr(_captureClient.GetNextPacketSize(out var framesInNextPacket), "IAudioCaptureClient.GetNextPacketSize");
                    if (framesInNextPacket == 0)
                    {
                        return;
                    }

                    CoreAudioInterop.CheckHr(
                        _captureClient.GetBuffer(out IntPtr buffer, out int framesAvailable, out CoreAudioInterop.AudioClientBufferFlags flags, out _, out _),
                        "IAudioCaptureClient.GetBuffer");

                    try
                    {
                        int rawBytes = framesAvailable * _rawBlockAlign;
                        int outputBytes = framesAvailable * Format.BlockAlign;
                        EnsureCapacity(ref _outputBuffer, outputBytes);

                        if ((flags & CoreAudioInterop.AudioClientBufferFlags.Silent) != 0)
                        {
                            Array.Clear(_outputBuffer!, 0, outputBytes);
                        }
                        else
                        {
                            EnsureCapacity(ref _transferBuffer, rawBytes);
                            Marshal.Copy(buffer, _transferBuffer!, 0, rawBytes);
                            ConvertToFloat32Stereo(_transferBuffer!, framesAvailable, _outputBuffer!);
                        }

                        handler(_outputBuffer.AsSpan(0, outputBytes), framesAvailable);
                    }
                    finally
                    {
                        CoreAudioInterop.CheckHr(_captureClient.ReleaseBuffer(framesAvailable), "IAudioCaptureClient.ReleaseBuffer");
                    }
                }
            }

            private void ConvertToFloat32Stereo(byte[] rawBuffer, int frames, byte[] destination)
            {
                int destOffset = 0;
                for (int frame = 0; frame < frames; frame++)
                {
                    int frameOffset = frame * _rawBlockAlign;
                    float left;
                    float right;
                    if (_rawChannels == 1)
                    {
                        float mono = ReadSampleAsFloat(rawBuffer, frameOffset, _rawBytesPerSample);
                        left = mono;
                        right = mono;
                    }
                    else
                    {
                        left = ReadSampleAsFloat(rawBuffer, frameOffset, _rawBytesPerSample);
                        right = ReadSampleAsFloat(rawBuffer, frameOffset + _rawBytesPerSample, _rawBytesPerSample);
                    }

                    WriteFloat32(destination, destOffset, AudioSignalProcessor.SanitizeSample(left));
                    WriteFloat32(destination, destOffset + 4, AudioSignalProcessor.SanitizeSample(right));
                    destOffset += 8;
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

                return bytesPerSample switch
                {
                    1 => (buffer[offset] - 128) / 128f,
                    2 => (short)(buffer[offset] | (buffer[offset + 1] << 8)) / 32768f,
                    3 => (buffer[offset] | (buffer[offset + 1] << 8) | ((sbyte)buffer[offset + 2] << 16)) / 8388608f,
                    4 => (buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24)) / 2147483648f,
                    _ => 0f,
                };
            }

            private static void WriteFloat32(byte[] buffer, int offset, float value)
            {
                uint bits = BitConverter.SingleToUInt32Bits(value);
                buffer[offset] = (byte)(bits & 0xFF);
                buffer[offset + 1] = (byte)((bits >> 8) & 0xFF);
                buffer[offset + 2] = (byte)((bits >> 16) & 0xFF);
                buffer[offset + 3] = (byte)((bits >> 24) & 0xFF);
            }

            private static void EnsureCapacity(ref byte[]? buffer, int required)
            {
                if (buffer == null || buffer.Length < required)
                {
                    buffer = new byte[required];
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                try { _audioClient.Stop(); } catch { }
                if (_eventHandle != IntPtr.Zero)
                {
                    CoreAudioInterop.CloseHandle(_eventHandle);
                }
                Marshal.ReleaseComObject(_captureClient);
                Marshal.ReleaseComObject(_audioClient);
                Marshal.ReleaseComObject(_device);
                Marshal.ReleaseComObject(_enumerator);
            }
        }

        private readonly record struct AudioMetrics(string Path, int SampleRate, double ActiveDurationSeconds, double Peak, double Rms, double CrestFactor, double DistortionRatio, double ClippedSampleRatio)
        {
            public override string ToString()
            {
                return $"path={System.IO.Path.GetFileName(Path)} sr={SampleRate} active={ActiveDurationSeconds:F2}s peak={Peak:F4} rms={Rms:F4} crest={CrestFactor:F3} dist={DistortionRatio:F3} clip={ClippedSampleRatio:P3}";
            }
        }
    }

    [CollectionDefinition("AudioIntegration", DisableParallelization = true)]
    public sealed class AudioIntegrationCollection : ICollectionFixture<object>
    {
    }

    public sealed class LocalAudioHardwareTheoryAttribute : TheoryAttribute
    {
        public LocalAudioHardwareTheoryAttribute()
        {
            if (IsGitHubActions())
            {
                Skip = "Requires a local Windows audio output device; GitHub hosted runners do not expose a WASAPI endpoint.";
            }
        }

        private static bool IsGitHubActions()
        {
            return string.Equals(
                Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
                "true",
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
