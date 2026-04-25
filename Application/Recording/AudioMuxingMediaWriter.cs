#nullable enable

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;

namespace ToNRoundCounter.Application.Recording
{
    /// <summary>
    /// <see cref="IMediaWriter"/> implementation that works around Windows 11 24H2's broken AAC
    /// encoder path in Media Foundation SinkWriter by:
    ///   1. Recording video-only via <see cref="MediaFoundationFrameWriter"/> (uses hardware
    ///      H.264 / HEVC encoders, which are unaffected by the 24H2 AAC regression).
    ///   2. Writing captured PCM to a sibling .wav file via <see cref="WavAudioWriter"/>.
    ///   3. On <see cref="Dispose"/>, calling ffmpeg.exe to remux the two streams into the
    ///      final container with AAC audio, then deleting the intermediates.
    ///
    /// ffmpeg.exe is located (or downloaded and cached in %LOCALAPPDATA%) by
    /// <see cref="FfmpegLocator"/>. A LGPL shared build is used so the arrangement is
    /// redistribution-safe: the exe is never bundled with this application's installer, it is
    /// fetched from the upstream project's official release on first use.
    /// </summary>
    internal sealed class AudioMuxingMediaWriter : IMediaWriter
    {
        private readonly MediaFoundationFrameWriter _videoWriter;
        private readonly WavAudioWriter _audioWriter;
        private readonly AudioFormat _audioFormat;
        private readonly string _finalOutputPath;
        private readonly string _videoTempPath;
        private readonly string _audioTempPath;
        private readonly int _audioBitrate;
        private bool _audioCompleted;
        private bool _disposed;
        // Seconds of leading audio to trim during the ffmpeg mux step. Used to compensate
        // for the gap between WASAPI delivering its first audio packet (very low latency,
        // typically < 30 ms after capture start) and Windows.Graphics.Capture delivering its
        // first video frame (~100–300 ms cold-start cost while the swap-chain capture session
        // initializes). Without this trim the recorded audio is audibly ahead of the video.
        private double _audioLeadingTrimSeconds;

        private AudioMuxingMediaWriter(
            MediaFoundationFrameWriter videoWriter,
            WavAudioWriter audioWriter,
            AudioFormat audioFormat,
            string finalOutputPath,
            string videoTempPath,
            string audioTempPath,
            int audioBitrate)
        {
            _videoWriter = videoWriter;
            _audioWriter = audioWriter;
            _audioFormat = audioFormat;
            _finalOutputPath = finalOutputPath;
            _videoTempPath = videoTempPath;
            _audioTempPath = audioTempPath;
            _audioBitrate = audioBitrate;
        }

        public static AudioMuxingMediaWriter Create(
            string extension,
            string codecId,
            string finalOutputPath,
            int width,
            int height,
            int frameRate,
            AudioFormat audioFormat,
            int videoBitrate,
            int audioBitrate,
            HardwareEncoderSelection hardwareSelection)
        {
            // Ensure ffmpeg is available up-front so the recording fails fast instead of losing
            // audio on dispose if the binary isn't obtainable. This also triggers the one-time
            // download the first time audio recording is used on a machine.
            FfmpegLocator.Locate();

            string directory = Path.GetDirectoryName(finalOutputPath) ?? Environment.CurrentDirectory;
            Directory.CreateDirectory(directory);
            string baseName = Path.GetFileNameWithoutExtension(finalOutputPath);
            string normalizedExt = (extension ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(normalizedExt))
            {
                normalizedExt = "mp4";
            }

            string videoTempPath = Path.Combine(directory, baseName + ".video." + normalizedExt);
            string audioTempPath = Path.Combine(directory, baseName + ".audio.wav");

            // Auto-prune old `.audio.wav` diagnostic files so the directory does not grow
            // without bound when source-WAV preservation is enabled. We keep only the N most
            // recent audio sources alongside the user's recordings.
            TryPruneOldAudioSources(directory, keepLatest: 3);

            MediaFoundationFrameWriter? videoWriter = null;
            WavAudioWriter? audioWriter = null;
            try
            {
                // Create the video-only MF writer - by passing null as audioFormat we completely
                // bypass the broken AAC SinkWriter path while keeping HW acceleration for video.
                videoWriter = MediaFoundationFrameWriter.Create(
                    normalizedExt, codecId, videoTempPath, width, height, frameRate,
                    audioFormat: null, videoBitrate, audioBitrate, hardwareSelection);

                audioWriter = new WavAudioWriter(audioTempPath, audioFormat);

                return new AudioMuxingMediaWriter(
                    videoWriter, audioWriter, audioFormat,
                    finalOutputPath, videoTempPath, audioTempPath, audioBitrate);
            }
            catch
            {
                audioWriter?.Dispose();
                videoWriter?.Dispose();
                try { if (File.Exists(videoTempPath)) File.Delete(videoTempPath); } catch { }
                try { if (File.Exists(audioTempPath)) File.Delete(audioTempPath); } catch { }
                throw;
            }
        }

        public bool IsHardwareAccelerated => _videoWriter.IsHardwareAccelerated;

        public bool SupportsAudio => true;

        public string? HardwareFallbackReason => _videoWriter.HardwareFallbackReason;

        public void WriteVideoFrame(Bitmap frame) => _videoWriter.WriteVideoFrame(frame);

        public void WriteVideoFrame(Bitmap frame, long presentationTimeTicks) => _videoWriter.WriteVideoFrame(frame, presentationTimeTicks);

        public void WriteAudioSample(ReadOnlySpan<byte> data, int frames) => _audioWriter.Write(data, frames);

        /// <summary>
        /// Tells the muxer how much leading audio (in seconds) to trim so the audio start
        /// aligns with the first delivered video frame. Called by
        /// <see cref="InternalScreenRecorder"/> right before disposal once both pipelines
        /// have produced their first sample. Negative or zero values are ignored.
        /// </summary>
        public void SetAudioLeadingTrimSeconds(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0.0)
            {
                _audioLeadingTrimSeconds = 0.0;
                return;
            }
            // Cap to a reasonable bound so a malformed timestamp never destroys an entire
            // recording. WGC cold-start latency above 5 s would already mean a recorder bug,
            // not real desync.
            _audioLeadingTrimSeconds = Math.Min(5.0, seconds);
        }

        public void CompleteAudio()
        {
            if (_audioCompleted)
            {
                return;
            }

            _audioCompleted = true;
            _audioWriter.Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Step 1: flush and close both intermediate files.
            try { _videoWriter.Dispose(); } catch { }
            try { _audioWriter.Dispose(); } catch { }

            // Step 2: only attempt to mux if BOTH intermediates exist and are non-empty. If the
            // recording aborted before any samples, fall back to whichever file has content.
            bool videoExists = File.Exists(_videoTempPath) && new FileInfo(_videoTempPath).Length > 0;
            bool audioExists = File.Exists(_audioTempPath) && new FileInfo(_audioTempPath).Length > 44; // header only = empty

            if (!videoExists)
            {
                // Nothing to keep; clean up audio temp and leave.
                try { if (File.Exists(_audioTempPath)) File.Delete(_audioTempPath); } catch { }
                return;
            }

            if (!audioExists)
            {
                // No audio; promote the video-only temp to the final path.
                SafeMove(_videoTempPath, _finalOutputPath);
                try { if (File.Exists(_audioTempPath)) File.Delete(_audioTempPath); } catch { }
                return;
            }

            try
            {
                MuxWithFfmpeg();

                // If mux succeeded the final file is in place - remove the video temp.
                try { File.Delete(_videoTempPath); } catch { }
                // Intentionally KEEP the `.audio.wav` source. We do this so users / support can
                // verify whether any reported "音割れ" (audio distortion) is in the captured
                // source itself (capture-side) or only appears in the muxed output (codec-side).
                // Old `.audio.wav` files are auto-pruned when the next recording begins (see
                // `TryPruneOldAudioSources` in `Create`), keeping disk usage bounded to the most
                // recent recordings only.
            }
            catch (Exception ex)
            {
                // Mux failed - preserve the video-only output as a fallback so the user at least
                // gets the footage, and leave the .wav in place for manual recovery. Also drop
                // a sibling .ffmpeg.log so the failure can be diagnosed without re-running.
                try
                {
                    File.WriteAllText(
                        _finalOutputPath + ".ffmpeg.log",
                        ex.ToString());
                }
                catch
                {
                }

                SafeMove(_videoTempPath, _finalOutputPath);
            }
        }

        private void MuxWithFfmpeg()
        {
            string ffmpegPath = FfmpegLocator.Locate();
            // Use a high AAC bitrate (default 320 kbps if caller did not specify) so the
            // codec contributes negligible artifacts even on dense / transient material.
            // ALAC was tried but Windows' built-in players (Photos / Media Player) refuse to
            // play ALAC inside an MP4 container, so the user perceives "no audio" — AAC is the
            // only universally compatible choice for MP4. At 320 kbps stereo AAC the audible
            // delta vs lossless source is below ear threshold for any practical content.
            int kbps = _audioBitrate > 0 ? Math.Max(32_000, _audioBitrate) / 1000 : 320;

            // -c:v copy     -> do not re-encode video (HW encoded H.264 / HEVC is preserved as-is)
            // -c:a aac      -> AAC at high bitrate; universally playable in MP4 on Windows / macOS / iOS / web.
            // -ar / -ac     -> lock the codec to the exact sample rate / channel count of the
            //                  captured WAV so ffmpeg does not silently resample (resampling can
            //                  add aliasing distortion if the rate ratio does not match).
            int sampleRate = _audioFormat.SampleRate > 0 ? _audioFormat.SampleRate : 48000;
            int channels = _audioFormat.Channels > 0 ? _audioFormat.Channels : 2;

            // Audio filter: linear attenuation + true brick-wall ceiling only.
            // See WasapiAudioCapture.ConvertToFloat32Stereo for the capture-side guarantees.
            //   1. ONLY linear gain (`volume`) and a true brick-wall limiter (`alimiter` with
            //      `level=disabled` and `asc=0`); any compressor / make-up gain produces
            //      audible distortion ("音割れ") on transient content.
            //   2. Pre-attenuate by -6 dB (`volume=0.5`) to leave the limiter passive for
            //      normal Windows shared-mix loopback peaks (≤ ~1.0 → 0.5 after -6 dB).
            //   3. Brick-wall ceiling at 0.95 (-0.45 dBFS) protects AAC from the encoder's
            //      true-peak overshoot (LC AAC routinely overshoots sample peaks by ~0.5 dB
            //      on inter-sample transients).
            string args = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "-hide_banner -loglevel error -y " +
                "-i \"{0}\" {6}-i \"{1}\" " +
                "-map 0:v:0 -map 1:a:0 " +
                "-c:v copy " +
                "-af \"volume=0.5,alimiter=level_in=1:level_out=1:limit=0.95:attack=5:release=200:asc=0:level=disabled\" " +
                "-c:a aac -b:a {2}k -ar {3} -ac {4} " +
                "\"{5}\"",

                _videoTempPath, _audioTempPath, kbps, sampleRate, channels, _finalOutputPath,
                // -ss <seconds> placed BEFORE the audio -i input performs an accurate seek
                // (sample-accurate at WAV PCM input rate) which trims the leading audio that
                // existed before WGC delivered its first video frame.
                _audioLeadingTrimSeconds > 0.0
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "-ss {0:0.######} ", _audioLeadingTrimSeconds)
                    : string.Empty);

            var psi = new ProcessStartInfo(ffmpegPath, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to launch ffmpeg.exe for audio muxing.");
            string stderr = proc.StandardError.ReadToEnd();
            string stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            // Always drop a sibling diagnostic file so frame-rate / sync issues that only
            // reproduce in real recordings can be inspected without re-running. The file is
            // tiny and lives next to the produced media.
            try
            {
                long videoLen = File.Exists(_videoTempPath) ? new FileInfo(_videoTempPath).Length : -1;
                long audioLen = File.Exists(_audioTempPath) ? new FileInfo(_audioTempPath).Length : -1;
                File.WriteAllText(
                    _finalOutputPath + ".ffmpeg.log",
                    string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "ffmpeg: {0}\nargs: {1}\nexit: {2}\nvideo temp size: {3} bytes\naudio temp size: {4} bytes\naudio format: {5}Hz {6}ch {7}-bit {8}\nstdout:\n{9}\nstderr:\n{10}\n",
                        ffmpegPath, args, proc.ExitCode, videoLen, audioLen,
                        _audioFormat.SampleRate, _audioFormat.Channels, _audioFormat.BitsPerSample,
                        _audioFormat.IsFloat ? "float" : "pcm",
                        stdout, stderr));
            }
            catch
            {
            }

            if (proc.ExitCode != 0 || !File.Exists(_finalOutputPath))
            {
                throw new InvalidOperationException(
                    $"ffmpeg mux failed (exit={proc.ExitCode}). stderr: {stderr}");
            }
        }

        private static void SafeMove(string source, string destination)
        {
            try
            {
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
                File.Move(source, destination);
            }
            catch
            {
                // If the move fails we leave the temp file in place; better than losing the capture.
            }
        }

        // Auto-prune older `.audio.wav` diagnostic files in the recordings directory so the
        // disk does not fill up. We keep the `keepLatest` newest source WAVs (one per recording)
        // and delete the rest. This runs at the start of a new recording, so the user always
        // has the source for the most recent N recordings available for distortion diagnosis.
        private static void TryPruneOldAudioSources(string directory, int keepLatest)
        {
            try
            {
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    return;
                }

                var sources = Directory.EnumerateFiles(directory, "*.audio.wav")
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .ToArray();

                for (int i = keepLatest; i < sources.Length; i++)
                {
                    try { sources[i].Delete(); } catch { }
                }
            }
            catch
            {
                // Pruning is best-effort: a failure here must never break recording start-up.
            }
        }
    }
}
