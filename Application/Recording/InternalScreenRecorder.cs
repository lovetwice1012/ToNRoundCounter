#nullable enable

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ToNRoundCounter.Application.Recording
{
    internal sealed class InternalScreenRecorder : IDisposable
    {
        private readonly IntPtr _windowHandle;
        private readonly int _frameRate;
        private readonly bool _includeOverlay;
        private readonly IMediaWriter _writer;
        private readonly WasapiAudioCapture? _audioCapture;
        private readonly CancellationTokenSource _cts;
        private readonly Task _videoTask;
        private readonly Task? _audioTask;
        private readonly Task _completionTask;
        private readonly object _stateLock = new object();
        private readonly object _audioLock = new object();
        private string _stopReason = "Completed";
        private bool _stopReasonSet;
        private bool _hasError;
        private bool _stopRequested;
        private bool _disposed;
        private readonly bool _isHardwareAccelerated;
        private readonly Size _targetSize;
        private WgcWindowCapture? _wgcCapture;
        private bool _timerResolutionRaised;
        private readonly Func<IReadOnlyList<RecordingOverlayBitmap>>? _overlaySnapshotProvider;
        private D2DOverlayCompositor? _overlayCompositor;
        private bool _overlayCompositorUnavailable;

        // Producer/consumer plumbing. The capture and the encode pipelines are decoupled so a
        // momentary spike in the HW encoder (typical for NVENC at 4K) cannot back-pressure either
        // the WGC capture (which would cause real frame drops) or the WASAPI audio callback
        // (which causes the audible 「音割れ」 artifacts when the WAV writer thread is blocked).
        // Bitmaps are pooled to avoid the 30 × 33 MB BGRA allocation per second that would
        // otherwise crush the GC on 4K recordings.
        //
        // FullMode.DropWrite: if the encoder fell so far behind that the queue is full, we drop
        // the NEW frame instead of blocking the capture loop. Coupled with wall-clock presentation
        // timestamps (see CaptureVideoLoopAsync), this produces an mp4 whose wall-clock duration
        // matches the real capture window — it just becomes visibly choppier when the encoder
        // cannot sustain the requested fps, instead of collapsing the timeline to encoder speed.
        private readonly Channel<VideoPacket> _videoQueue = Channel.CreateBounded<VideoPacket>(
            new BoundedChannelOptions(VideoQueueDepth)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = true,
            });
        private readonly ConcurrentBag<Bitmap> _bitmapPool = new();
        private Task? _videoEncodeTask;

        private readonly Channel<AudioPacket> _audioQueue = Channel.CreateUnbounded<AudioPacket>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        private Task? _audioWriteTask;
        private long _videoFramesQueued;
        private long _videoFramesWritten;
        // Counts capture-loop iterations that produced a frame but had to drop it because the
        // encoder queue was full even after a brief retry. Surfaced in the .diag.log so we can
        // tell whether the perceived "low fps / slow video" symptom comes from the encoder
        // being a bottleneck (high dropped count) or the capture itself underperforming
        // (queued count below frameRate * realDuration).
        private long _videoFramesDropped;
        private readonly string _outputPath = string.Empty;
        // Capture-loop instrumentation: total time spent inside the throttle Task.Delay vs
        // executing the body (capture + resize + enqueue), plus the slowest single body
        // observation. Surfaced in .diag.log to pinpoint whether 「fps が低い」 is caused by
        // (a) Task.Delay over-sleeping, (b) WGC TryAcquireFrame, (c) GDI+ resize, or
        // (d) the encoder back-pressuring TryWrite.
        private long _captureLoopIterations;
        private long _captureLoopThrottleNanos;
        private long _captureLoopBodyNanos;
        private long _captureLoopMaxBodyNanos;
        private long _captureLoopWgcSuccess;
        private long _captureLoopWgcMiss;
        // Sub-stage timing (in 100-ns ticks summed). Lets us isolate whether the per-iteration
        // body cost is dominated by the WGC GPU readback (CopyResource/Map) or by the GDI+
        // bookkeeping copies (DrawImageUnscaled into lastCapturedFrame, encoder enqueue, etc.).
        private long _captureStageWgcNanos;
        private long _captureStageRentBitmapNanos;
        private long _captureStageDrawScaleNanos;
        private long _captureStageEnqueueNanos;
        private long _captureStageOverlayNanos;

        // Wall-clock anchor (high-resolution Stopwatch ticks since the recorder constructor
        // ran). Both the video capture loop and the audio capture callback record the tick
        // value of their FIRST delivered sample relative to this anchor. The delta between
        // those two anchors is the A/V desync we need to compensate at mux time.
        private readonly long _wallClockStartTicks = Stopwatch.GetTimestamp();
        private long _firstVideoFrameWallTicks = -1;
        private long _firstAudioPacketWallTicks = -1;
        // Once known (set when first video frame is captured), all subsequent video PTS are
        // shifted so the first encoded frame has PTS=0. Without this, the first frame would
        // have a wall-clock duration of “WGC cold-start latency” (typically 100–300 ms),
        // making the video appear to start late relative to audio.
        private long _videoPresentationOffsetTicks = -1;

        // Encoder queue depth. Sized for ~4s of 60fps source (256 frames) so a sustained HW
        // encoder stall (NVENC initialization burst, momentary GPU contention, brief disk
        // write hiccup, etc.) does not start dropping frames immediately. With wall-clock
        // presentation timestamps the encoder is free to consume out of phase with capture;
        // only sustained encoder underrun beyond this depth causes drops.
        private const int VideoQueueDepth = 256;

        // Zero-copy GPU→encoder pipeline. When the recorder is using a hardware-accelerated
        // MediaFoundationFrameWriter AND no on-screen overlay is being composited (overlay
        // requires a CPU bitmap blit), we route the captured frame as an ID3D11Texture2D
        // straight from WGC → GPU resize → encoder MFT, eliminating the ~26-37 ms staging
        // readback that previously dominated stage_wgc at 4K. The pool size is intentionally
        // generous (8) because IMFSinkWriter holds an outstanding reference to each submitted
        // sample until the encoder consumes it, and we must not overwrite a slot that is
        // still being read.
        private const int ZeroCopyPoolSize = 8;
        private bool _zeroCopyEnabled;
        private IntPtr _zeroCopyD3DDevice = IntPtr.Zero;
        private IntPtr _zeroCopyD3DContext = IntPtr.Zero;
        private readonly IntPtr[] _zeroCopyTexturePool = new IntPtr[ZeroCopyPoolSize];
        private readonly System.Collections.Concurrent.ConcurrentQueue<int> _zeroCopyFreeSlots = new();
        private bool _zeroCopyTexturesInitialized;

        private readonly struct VideoPacket
        {
            public VideoPacket(Bitmap frame, long presentationTicks)
            {
                Frame = frame;
                PresentationTicks = presentationTicks;
                TextureSlot = -1;
            }

            public VideoPacket(int textureSlot, long presentationTicks)
            {
                Frame = null!;
                PresentationTicks = presentationTicks;
                TextureSlot = textureSlot;
            }

            public Bitmap Frame { get; }
            public long PresentationTicks { get; }
            // -1 means CPU bitmap mode; >=0 means zero-copy path: index into _zeroCopyTexturePool.
            public int TextureSlot { get; }
        }

        private readonly struct AudioPacket
        {
            public AudioPacket(byte[] buffer, int byteCount, int frameCount)
            {
                Buffer = buffer;
                ByteCount = byteCount;
                FrameCount = frameCount;
            }

            public byte[] Buffer { get; }
            public int ByteCount { get; }
            public int FrameCount { get; }
        }

        private InternalScreenRecorder(IntPtr windowHandle, int frameRate, Size targetSize, bool includeOverlay, IMediaWriter writer, WasapiAudioCapture? audioCapture, string outputPath, Func<IReadOnlyList<RecordingOverlayBitmap>>? overlaySnapshotProvider)
        {
            _windowHandle = windowHandle;
            _frameRate = frameRate;
            _targetSize = targetSize;
            _includeOverlay = includeOverlay;
            _cts = new CancellationTokenSource();
            _writer = writer;
            _audioCapture = audioCapture;
            _overlaySnapshotProvider = overlaySnapshotProvider;
            _isHardwareAccelerated = writer.IsHardwareAccelerated;
            _outputPath = outputPath;

            // Try to enable the zero-copy GPU→encoder path. Requirements:
            //   - hardware-accelerated writer (so the encoder MFT can consume DXGI surfaces),
            //   - the writer exposes its underlying D3D11 device (HW path always does),
            //   - overlays, when enabled, can be composited onto the GPU texture via D2D.
            // If any requirement fails we fall back to the legacy CPU bitmap pipeline,
            // which still works, just without the per-frame readback elimination.
            if (_isHardwareAccelerated && writer is MediaFoundationFrameWriter mfw)
            {
                IntPtr dev = mfw.AddRefEncoderD3D11Device();
                IntPtr ctx = mfw.AddRefEncoderD3D11Context();
                if (dev != IntPtr.Zero && ctx != IntPtr.Zero)
                {
                    _zeroCopyD3DDevice = dev;
                    _zeroCopyD3DContext = ctx;
                    _zeroCopyEnabled = true;
                    if (_includeOverlay && _overlaySnapshotProvider != null)
                    {
                        _overlayCompositor = new D2DOverlayCompositor();
                        if (!_overlayCompositor.TryInitialize(_zeroCopyD3DDevice, out _))
                        {
                            _overlayCompositor.Dispose();
                            _overlayCompositor = null;
                            _overlayCompositorUnavailable = true;
                        }
                    }
                }
                else
                {
                    if (dev != IntPtr.Zero) Marshal.Release(dev);
                    if (ctx != IntPtr.Zero) Marshal.Release(ctx);
                }
            }

            // Boost the system multimedia timer resolution to ~1ms so Task.Delay/Thread.Sleep
            // inside the capture loop can actually fire at the requested frame interval.
            // Without this, the default Windows timer tick (~15.6ms) caps effective capture
            // around 30 fps even when 60+ fps was requested, producing the
            // 「キャプチャしたフレーム数が少なすぎる」symptom.
            try
            {
                if (timeBeginPeriod(1) == 0)
                {
                    _timerResolutionRaised = true;
                }
            }
            catch { }

            _videoTask = Task.Run(() => CaptureVideoLoopAsync(_cts.Token));
            _videoEncodeTask = Task.Run(() => VideoEncodeLoopAsync(_cts.Token));

            if (_audioCapture != null)
            {
                _audioTask = Task.Run(() => CaptureAudioLoopAsync(_cts.Token));
                _audioWriteTask = Task.Run(() => AudioWriteLoopAsync(_cts.Token));
            }

            // Completion waits for *all* pipeline stages so the file is fully flushed before
            // the muxing dispose runs.
            var completionTasks = new List<Task> { _videoTask, _videoEncodeTask };
            if (_audioTask != null) completionTasks.Add(_audioTask);
            if (_audioWriteTask != null) completionTasks.Add(_audioWriteTask);
            _completionTask = Task.WhenAll(completionTasks);
        }

        public Task Completion => _completionTask;

        public string StopReason
        {
            get
            {
                lock (_stateLock)
                {
                    return _stopReason;
                }
            }
        }

        public bool HasError
        {
            get
            {
                lock (_stateLock)
                {
                    return _hasError;
                }
            }
        }

        public bool StoppedByOwner
        {
            get
            {
                lock (_stateLock)
                {
                    return _stopRequested;
                }
            }
        }

        public bool IsHardwareAccelerated => _isHardwareAccelerated;

        // Diagnostic-only: populated when the recorder was started with audio enabled but the
        // SinkWriter could not connect WASAPI loopback to the encoder MFT and we silently fell
        // back to a video-only file. Used by AutoRecordingService to log the actual reason so
        // users see why the recording came out without sound.
        public string? AudioFallbackReason { get; private set; }

        // Diagnostic-only: populated when the recorder was started with hardware acceleration
        // requested but the HW SinkWriter creation failed and we silently fell back to the
        // software encoder. Surfacing this is critical because the SW H.264 encoder cannot keep
        // up with 4K30 in real time, which causes the capture loop to fill gaps with duplicate
        // frames -- the resulting file has the correct 30 fps header but plays as roughly 1
        // unique frame per second.
        public string? HardwareFallbackReason { get; private set; }

        // Snapshot-style diagnostic string from the active WASAPI capture pipeline.
        // Helps distinguish capture-side discontinuities from downstream encoding issues.
        public string? AudioCaptureDiagnosticsSummary => _audioCapture?.GetDiagnosticsSummary();

        public static bool TryCreate(string windowHint, int requestedFrameRate, string resolutionOptionId, string outputPath, string extension, string codecId, bool includeOverlay, int videoBitrate, int audioBitrate, HardwareEncoderSelection hardwareSelection, bool captureAudio, Func<IReadOnlyList<RecordingOverlayBitmap>>? overlaySnapshotProvider, out InternalScreenRecorder? recorder, out int actualFrameRate, out Size targetResolution, out string? failureReason)
        {
            recorder = null;
            actualFrameRate = 0;
            targetResolution = Size.Empty;
            failureReason = null;

            string hint = string.IsNullOrWhiteSpace(windowHint) ? "VRChat" : windowHint.Trim();
            IntPtr handle = FindTargetWindow(hint);
            if (handle == IntPtr.Zero)
            {
                failureReason = $"Window matching '{hint}' was not found.";
                return false;
            }

            if (!GetWindowRect(handle, out var rect))
            {
                failureReason = "Failed to query target window bounds.";
                return false;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
            {
                failureReason = "Target window size is zero.";
                return false;
            }

            targetResolution = AutoRecordingService.ResolveRecordingTargetSize(resolutionOptionId, width, height);
            if (targetResolution.Width <= 0 || targetResolution.Height <= 0)
            {
                failureReason = "Target resolution is invalid.";
                return false;
            }

            targetResolution = NormalizeTargetSizeForWriter(extension, targetResolution);

            IMediaWriter? writer = null;
            WasapiAudioCapture? audioCapture = null;
            WasapiAudioCapture? audioCaptureToTransfer = null;
            AudioFormat? audioFormat = null;

            try
            {
                string? wasapiFailureReason = null;
                if (captureAudio)
                {
                    // Audio capture strategy (in priority order):
                    //
                    //   1. Process Loopback (Windows 11 build 20348+). Captures the target
                    //      process tree's audio BEFORE the system audio engine, so it bypasses
                    //      Loudness Equalization, Bass Boost, and any other endpoint Audio
                    //      Processing Object (APO). This is the same API OBS uses and is the
                    //      ONLY way to record without the documented "音割れ" / extreme
                    //      compression artifact (crest factor ~ 1.2 on captured WAV) caused
                    //      by an enabled APO between the WASAPI loopback tap point and what
                    //      the user actually hears.
                    //
                    //   2. (Disabled by design) Endpoint loopback (`TryCreateForWindow`). This
                    //      is the legacy path and the one that produces the bad recording on
                    //      affected systems — DO NOT silently fall back to it. If process
                    //      loopback fails (Windows 10 < 20348, or no audio session yet on the
                    //      target PID) we continue with VIDEO-ONLY recording. The user can
                    //      then disable Loudness Equalization manually in Windows sound
                    //      settings and re-attempt.
                    uint pid = 0;
                    try
                    {
                        _ = GetWindowThreadProcessId(handle, out pid);
                    }
                    catch
                    {
                        pid = 0;
                    }

                    string? processLoopbackError = null;
                    if (pid != 0)
                    {
                        if (!WasapiAudioCapture.TryCreateForProcess(pid, out audioCapture, out processLoopbackError))
                        {
                            audioCapture?.Dispose();
                            audioCapture = null;
                        }
                    }
                    else
                    {
                        processLoopbackError = "Failed to resolve process id for the target window.";
                    }

                    if (audioCapture == null)
                    {
                        wasapiFailureReason = string.IsNullOrEmpty(processLoopbackError)
                            ? "Process loopback audio capture failed (no audio recorded)."
                            : $"Process loopback failed: {processLoopbackError}. Audio not recorded — to avoid the legacy WASAPI loopback path which is affected by Windows audio enhancements (Loudness Equalization etc.), audio is intentionally disabled instead of falling back.";
                        audioFormat = null;
                    }
                    else
                    {
                        audioFormat = audioCapture.Format;
                    }
                }

                int frameRate = AutoRecordingService.ApplyFrameRateLimits(codecId, hardwareSelection, requestedFrameRate, targetResolution);
                actualFrameRate = frameRate;
                writer = CreateWriter(extension, codecId, outputPath, targetResolution.Width, targetResolution.Height, frameRate, audioFormat, videoBitrate, audioBitrate, hardwareSelection, out string? audioFallbackReason);

                // WASAPI failure trumps the encoder-side fallback because it happens first.
                if (wasapiFailureReason != null)
                {
                    audioFallbackReason = wasapiFailureReason;
                }

                if (audioCapture != null && !writer.SupportsAudio)
                {
                    audioCapture.Dispose();
                    audioCapture = null;
                }

                audioCaptureToTransfer = audioCapture;
                audioCapture = null;
                recorder = new InternalScreenRecorder(handle, frameRate, targetResolution, includeOverlay, writer, audioCaptureToTransfer, outputPath, overlaySnapshotProvider);
                recorder.AudioFallbackReason = audioFallbackReason;
                if (writer is MediaFoundationFrameWriter mfWriter)
                {
                    recorder.HardwareFallbackReason = mfWriter.HardwareFallbackReason;
                }
                else if (writer is AudioMuxingMediaWriter muxWriter)
                {
                    recorder.HardwareFallbackReason = muxWriter.HardwareFallbackReason;
                }
                audioCaptureToTransfer = null;
                return true;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                writer?.Dispose();
                audioCapture?.Dispose();
                audioCaptureToTransfer?.Dispose();
                recorder?.Dispose();
                recorder = null;
                actualFrameRate = 0;
                targetResolution = Size.Empty;
                return false;
            }
        }

        private static Size NormalizeTargetSizeForWriter(string extension, Size targetSize)
        {
            string normalizedExtension = (extension ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();

            // Media Foundation encoders commonly require even frame dimensions.
            // Keep AVI/GIF unchanged because they are handled by custom writers.
            if (normalizedExtension == "avi" || normalizedExtension == "gif")
            {
                return targetSize;
            }

            int normalizedWidth = targetSize.Width;
            int normalizedHeight = targetSize.Height;

            if ((normalizedWidth & 1) != 0)
            {
                normalizedWidth = Math.Max(16, normalizedWidth - 1);
            }

            if ((normalizedHeight & 1) != 0)
            {
                normalizedHeight = Math.Max(16, normalizedHeight - 1);
            }

            return new Size(normalizedWidth, normalizedHeight);
        }

        private static IMediaWriter CreateWriter(string extension, string codecId, string outputPath, int width, int height, int frameRate, AudioFormat? audioFormat, int videoBitrate, int audioBitrate, HardwareEncoderSelection hardwareSelection, out string? audioFallbackReason)
        {
            audioFallbackReason = null;
            string normalizedExtension = (extension ?? string.Empty).ToLowerInvariant();
            
            switch (normalizedExtension)
            {
                case "gif":
                    if (audioFormat.HasValue)
                    {
                        throw new NotSupportedException("Audio capture is not supported for GIF recordings.");
                    }

                    return new GifFrameWriter(outputPath, width, height, frameRate);
                case "avi":
                    if (audioFormat.HasValue)
                    {
                        throw new NotSupportedException("Audio capture is not supported for AVI recordings.");
                    }

                    return new SimpleAviWriter(outputPath, width, height, frameRate);
                default:
                    if (audioFormat.HasValue)
                    {
                        // Windows 11 24H2 broke the SinkWriter -> AAC encoder pipeline (every
                        // output media type we hand it is rejected with MF_E_INVALIDMEDIATYPE,
                        // and MFTranscodeGetAudioOutputAvailableTypes returns unpopulated shells).
                        // We work around that by recording video-only via Media Foundation and
                        // muxing the captured PCM with an external ffmpeg.exe on stop.
                        try
                        {
                            return AudioMuxingMediaWriter.Create(
                                normalizedExtension, codecId, outputPath, width, height, frameRate,
                                audioFormat.Value, videoBitrate, audioBitrate, hardwareSelection);
                        }
                        catch (Exception ex)
                        {
                            // If ffmpeg cannot be located/downloaded (offline first-run, etc.)
                            // fall back to SinkWriter's legacy audio path; on non-24H2 OS builds
                            // it will still work.
                            try
                            {
                                var writer = MediaFoundationFrameWriter.Create(
                                    normalizedExtension, codecId, outputPath, width, height, frameRate,
                                    audioFormat, videoBitrate, audioBitrate, hardwareSelection);
                                audioFallbackReason = $"ffmpeg-based muxing unavailable ({ex.Message}); using Media Foundation audio path.";
                                return writer;
                            }
                            catch (Exception innerEx) when (IsAudioMediaTypeNegotiationFailure(innerEx))
                            {
                                audioFallbackReason = ExtractFirstMessage(innerEx);
                                return MediaFoundationFrameWriter.Create(
                                    normalizedExtension, codecId, outputPath, width, height, frameRate,
                                    null, videoBitrate, audioBitrate, hardwareSelection);
                            }
                        }
                    }

                    return MediaFoundationFrameWriter.Create(normalizedExtension, codecId, outputPath, width, height, frameRate, null, videoBitrate, audioBitrate, hardwareSelection);
            }
        }

        private static string ExtractFirstMessage(Exception exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                string message = current.Message ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
            return exception.GetType().Name;
        }

        private static bool IsAudioMediaTypeNegotiationFailure(Exception exception)
        {
            if (exception == null)
            {
                return false;
            }

            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                string message = current.Message ?? string.Empty;
                if (message.IndexOf("SetInputMediaType(Audio)", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("AddStream(Audio)", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("0xC00D36B4", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("0xC00D36B2", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("AAC encoder MFT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("CLSID_AACMFTEncoder", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public void Stop(string reason)
        {
            lock (_stateLock)
            {
                if (_stopRequested)
                {
                    return;
                }

                _stopRequested = true;
            }

            SetStopReason(reason, false);
            _cts.Cancel();

            try
            {
                if (!_completionTask.Wait(TimeSpan.FromSeconds(10)))
                {
                    SetStopReason("Stop timeout exceeded", true);
                }
            }
            catch (AggregateException ex)
            {
                ex.Handle(e => e is OperationCanceledException);
            }
            catch (OperationCanceledException)
            {
            }

            // Inform the muxer of the audio-leading offset so it trims that many seconds of
            // leading audio. We do this AFTER the pipelines have flushed (so both first-sample
            // anchors are valid) but BEFORE Dispose runs ffmpeg (where the trim takes effect).
            ApplyAudioVideoSyncOffset();

            // Drop a sibling .diag.log so the user can attach actual capture/encode counters
            // when reporting "video looks slow / fps feels low". This makes it possible to
            // tell at a glance whether the bottleneck is capture (queued < target * duration)
            // or the encoder (dropped > 0). Best-effort; never throws into Stop().
            try
            {
                WriteRecordingDiagnostics();
            }
            catch
            {
            }
        }

        private void WriteRecordingDiagnostics()
        {
            if (string.IsNullOrEmpty(_outputPath))
            {
                return;
            }
            long realDurationTicks = Stopwatch.GetTimestamp() - _wallClockStartTicks;
            double realDurationSec = (double)realDurationTicks / Stopwatch.Frequency;
            long queued = Interlocked.Read(ref _videoFramesQueued);
            long written = Interlocked.Read(ref _videoFramesWritten);
            long dropped = Interlocked.Read(ref _videoFramesDropped);
            double observedCaptureFps = realDurationSec > 0 ? queued / realDurationSec : 0;
            double observedEncodeFps = realDurationSec > 0 ? written / realDurationSec : 0;
            long firstVideoTicks = Interlocked.Read(ref _firstVideoFrameWallTicks);
            long firstAudioTicks = Interlocked.Read(ref _firstAudioPacketWallTicks);
            double firstVideoSec = firstVideoTicks >= 0 ? (double)firstVideoTicks / Stopwatch.Frequency : -1;
            double firstAudioSec = firstAudioTicks >= 0 ? (double)firstAudioTicks / Stopwatch.Frequency : -1;
            double trimSec = (firstVideoTicks >= 0 && firstAudioTicks >= 0)
                ? Math.Max(0, (double)(firstVideoTicks - firstAudioTicks) / Stopwatch.Frequency)
                : 0;
            string diag = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "target_fps={0}\nreal_duration_sec={1:0.######}\n" +
                "video_frames_queued={2} (capture-loop succeeded TryWrite)\n" +
                "video_frames_written={3} (encoder finished WriteSample)\n" +
                "video_frames_dropped={4} (queue full even after retry)\n" +
                "observed_capture_fps={5:0.##}\nobserved_encode_fps={6:0.##}\n" +
                "first_video_wall_sec={7:0.######}\nfirst_audio_wall_sec={8:0.######}\n" +
                "audio_leading_trim_sec={9:0.######}\nqueue_depth={10}\n" +
                "capture_loop_iterations={11}\n" +
                "capture_loop_throttle_total_ms={12:0.###} (Task.Delay time)\n" +
                "capture_loop_body_total_ms={13:0.###} (capture+resize+enqueue)\n" +
                "capture_loop_avg_body_ms={14:0.###}\n" +
                "capture_loop_max_body_ms={15:0.###}\n" +
                "wgc_success={16} wgc_miss={17}\n" +
                "stage_wgc_total_ms={18:0.###} (TryAcquireFrame: GPU CopyResource+Map+memcpy)\n" +
                "stage_rest_total_ms={19:0.###} (RentBitmap+GDI+ DrawImageUnscaled+enqueue)\n" +
                "stage_rent_bitmap_total_ms={20:0.###}\n" +
                "stage_draw_scale_total_ms={21:0.###} (only Path B / resize)\n" +
                "stage_enqueue_total_ms={22:0.###}\n" +
                "stage_overlay_total_ms={23:0.###}\n",
                _frameRate, realDurationSec, queued, written, dropped,
                observedCaptureFps, observedEncodeFps, firstVideoSec, firstAudioSec,
                trimSec, VideoQueueDepth,
                Interlocked.Read(ref _captureLoopIterations),
                Interlocked.Read(ref _captureLoopThrottleNanos) / 1_000_000.0,
                Interlocked.Read(ref _captureLoopBodyNanos) / 1_000_000.0,
                Interlocked.Read(ref _captureLoopIterations) > 0
                    ? (Interlocked.Read(ref _captureLoopBodyNanos) / 1_000_000.0) / Interlocked.Read(ref _captureLoopIterations)
                    : 0,
                Interlocked.Read(ref _captureLoopMaxBodyNanos) / 1_000_000.0,
                Interlocked.Read(ref _captureLoopWgcSuccess),
                Interlocked.Read(ref _captureLoopWgcMiss),
                Interlocked.Read(ref _captureStageWgcNanos) / 1_000_000.0,
                Math.Max(0, (Interlocked.Read(ref _captureLoopBodyNanos) - Interlocked.Read(ref _captureStageWgcNanos)) / 1_000_000.0),
                Interlocked.Read(ref _captureStageRentBitmapNanos) / 1_000_000.0,
                Interlocked.Read(ref _captureStageDrawScaleNanos) / 1_000_000.0,
                Interlocked.Read(ref _captureStageEnqueueNanos) / 1_000_000.0,
                Interlocked.Read(ref _captureStageOverlayNanos) / 1_000_000.0);
            try
            {
                System.IO.File.WriteAllText(_outputPath + ".diag.log", diag);
            }
            catch
            {
            }
        }

        private void ApplyAudioVideoSyncOffset()
        {
            if (_writer is not AudioMuxingMediaWriter muxWriter)
            {
                return;
            }

            long videoTicks = Interlocked.Read(ref _firstVideoFrameWallTicks);
            long audioTicks = Interlocked.Read(ref _firstAudioPacketWallTicks);
            if (videoTicks < 0 || audioTicks < 0)
            {
                return;
            }

            // Positive delta = audio packet arrived BEFORE the first video frame. Trim that
            // much leading audio so the resulting mux starts both streams at the same instant.
            long deltaTicks = videoTicks - audioTicks;
            if (deltaTicks <= 0)
            {
                return;
            }
            double seconds = (double)deltaTicks / Stopwatch.Frequency;
            try
            {
                muxWriter.SetAudioLeadingTrimSeconds(seconds);
            }
            catch
            {
            }
        }

        // Brief-retry enqueue. The encoder queue is sized for ~4s of 60fps headroom but a
        // single GPU/disk hiccup can still saturate it momentarily; spinning ~1 ms before
        // dropping lets HW NVENC swallow that burst, recovering visible smoothness without
        // letting the capture loop fall behind real time.
        private bool TryEnqueueVideoPacket(VideoPacket packet)
        {
            if (_videoQueue.Writer.TryWrite(packet))
            {
                return true;
            }
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < 1.0)
            {
                if (_videoQueue.Writer.TryWrite(packet))
                {
                    return true;
                }
                Thread.SpinWait(64);
            }
            return false;
        }

        // Computes the wall-clock-based presentation time (in 100-ns / hns units) for the
        // current video frame. The first call also anchors the writer's PTS=0 to the time of
        // the very first frame, and stores the wall-clock arrival of that first frame so the
        // muxer can later compute (video_first - audio_first) and trim the leading audio that
        // existed before WGC produced anything (otherwise audio runs ahead of video).
        private long ComputeVideoPresentationTicks(Stopwatch captureStopwatch)
        {
            long wallTicks = Stopwatch.GetTimestamp() - _wallClockStartTicks;
            // 100-ns units expected by Media Foundation.
            long hns = (long)((double)wallTicks * 10_000_000.0 / Stopwatch.Frequency);

            if (Interlocked.Read(ref _firstVideoFrameWallTicks) < 0)
            {
                if (Interlocked.CompareExchange(ref _firstVideoFrameWallTicks, wallTicks, -1) == -1)
                {
                    _videoPresentationOffsetTicks = hns;
                }
            }

            long offset = Interlocked.Read(ref _videoPresentationOffsetTicks);
            if (offset < 0)
            {
                offset = hns;
            }
            long shifted = hns - offset;
            return shifted < 0 ? 0 : shifted;
        }

        private async Task CaptureVideoLoopAsync(CancellationToken token)
        {
            // Throttle the capture to the user's requested frame rate. Both WGC and legacy
            // PrintWindow paths share this throttle so:
            //   captured_frame_count_per_second == _frameRate
            //   muxed_file_duration            == real wall-clock duration
            // This matters because the encoder is configured with MF_MT_FRAME_RATE = _frameRate
            // and the muxer's stts table is computed against that rate. If we delivered MORE
            // frames per real second than _frameRate, the muxed file would play back slower
            // than reality (file duration = captured_frames / _frameRate). The earlier
            // "drain every WGC frame" approach hit exactly that bug, producing ~1.1x slow
            // playback whenever the source game ran above the target fps.
            var frameInterval = TimeSpan.FromSeconds(1d / Math.Max(1, _frameRate));
            long frameIntervalTicks = Math.Max(1L, frameInterval.Ticks);
            var captureStopwatch = Stopwatch.StartNew();
            long nextDeadlineTicks = frameIntervalTicks;
            var targetSize = _targetSize;
            Bitmap? scaleScratch = null;

            // Try Windows.Graphics.Capture first - it's the only API that captures the actual
            // swap-chain content of DirectX/Vulkan apps (VRChat, games). PrintWindow / BitBlt
            // only return DWM thumbnails which on Windows 11 24H2 are refreshed at less than
            // 1 Hz for hardware-accelerated apps, producing the "1 fps recording" symptom.
            //
            // EXCEPTION: when the user enables "include overlay" we MUST route through
            // CopyFromScreen instead, because WGC captures only the target window's swap
            // chain and external topmost overlay forms (round-stats, voting panel, etc.)
            // would otherwise be invisible in the recording. CopyFromScreen captures the
            // screen region behind the window, including those overlay windows.
            if (_wgcCapture == null && (!_includeOverlay || _zeroCopyEnabled))
            {
                if (_zeroCopyEnabled)
                {
                    // Re-create the WGC session against the encoder's D3D11 device so the
                    // captured frame texture, the GPU resize draw, and the encoder MFT all
                    // operate on the same device. This is the prerequisite for handing the
                    // resized RT directly to MFCreateDXGISurfaceBuffer without any cross-
                    // device copy.
                    _wgcCapture = WgcWindowCapture.TryCreateForWindowSharingDevice(
                        _windowHandle, _zeroCopyD3DDevice, _zeroCopyD3DContext, out _);
                    if (_wgcCapture == null)
                    {
                        // Shared-device creation failed for some reason; fall back to the
                        // private-device path so capture still works (just with the readback).
                        _zeroCopyEnabled = false;
                        _wgcCapture = WgcWindowCapture.TryCreateForWindow(_windowHandle, out _);
                    }
                }
                else
                {
                    _wgcCapture = WgcWindowCapture.TryCreateForWindow(_windowHandle, out _);
                }
            }

            try
            {
                while (!token.IsCancellationRequested)
                {
                    long iterStartTicks = captureStopwatch.Elapsed.Ticks;
                    var elapsedTicks = iterStartTicks;
                    if (elapsedTicks < nextDeadlineTicks)
                    {
                        var delay = TimeSpan.FromTicks(nextDeadlineTicks - elapsedTicks);
                        if (delay > TimeSpan.Zero)
                        {
                            try
                            {
                                await Task.Delay(delay, token).ConfigureAwait(false);
                            }
                            catch (TaskCanceledException) when (token.IsCancellationRequested)
                            {
                                break;
                            }
                        }
                    }
                    long throttleEndTicks = captureStopwatch.Elapsed.Ticks;
                    Interlocked.Add(ref _captureLoopThrottleNanos, (throttleEndTicks - iterStartTicks) * 100);

                    // Cumulative deadline (advance from the previous deadline rather than from
                    // "now") so over the course of the recording we emit exactly _frameRate
                    // frames per real second on average even if individual ticks slip due to
                    // OS scheduler jitter. Without this the recording fps slowly drifts below
                    // the target after every stall.
                    nextDeadlineTicks += frameIntervalTicks;
                    long nowTicks = captureStopwatch.Elapsed.Ticks;
                    if (nextDeadlineTicks < nowTicks - frameIntervalTicks * 4)
                    {
                        // Catastrophically far behind (>4 frames). Resync rather than spam a
                        // catch-up burst - prevents the encoder queue from overflowing if the
                        // app was suspended (sleep/lid-close) for several seconds.
                        nextDeadlineTicks = nowTicks + frameIntervalTicks;
                    }

                    if (!IsWindow(_windowHandle))
                    {
                        SetStopReason("Target window is no longer available.", true);
                        break;
                    }

                    if (!TryGetWindowBounds(_windowHandle, out var rect))
                    {
                        SetStopReason("Failed to retrieve window bounds.", true);
                        break;
                    }

                    int width = Math.Max(1, rect.Right - rect.Left);
                    int height = Math.Max(1, rect.Bottom - rect.Top);

                    try
                    {
                        // Zero-copy GPU path (preferred when HW encoder + no overlay): the
                        // captured WGC frame is GPU-resized into one of our pool textures
                        // and handed straight to the encoder MFT as a DXGI surface buffer.
                        // Replaces both the staging readback (~26 ms / frame at 4K) and the
                        // pooled-Bitmap allocation (~0.16 ms / frame). See ZeroCopyPoolSize
                        // for pool depth rationale.
                        if (_zeroCopyEnabled)
                        {
                            if (!_zeroCopyTexturesInitialized)
                            {
                                if (!EnsureZeroCopyTexturePool(targetSize.Width, targetSize.Height))
                                {
                                    // Allocation failed (driver OOM, format unsupported, etc.).
                                    // Disable zero-copy and fall through to the bitmap path
                                    // for the rest of the recording.
                                    _zeroCopyEnabled = false;
                                }
                            }

                            if (_zeroCopyEnabled && _zeroCopyFreeSlots.TryDequeue(out int slot))
                            {
                                long wgcStartTicks0 = captureStopwatch.Elapsed.Ticks;
                                bool capturedZc = _wgcCapture != null &&
                                                  _wgcCapture.TryDrawCapturedFrameIntoTexture(
                                                      _zeroCopyTexturePool[slot], targetSize.Width, targetSize.Height);
                                long wgcEndTicks0 = captureStopwatch.Elapsed.Ticks;
                                Interlocked.Add(ref _captureStageWgcNanos, (wgcEndTicks0 - wgcStartTicks0) * 100);
                                if (capturedZc) Interlocked.Increment(ref _captureLoopWgcSuccess); else Interlocked.Increment(ref _captureLoopWgcMiss);
                                if (!capturedZc)
                                {
                                    _zeroCopyFreeSlots.Enqueue(slot);
                                }
                                else
                                {
                                    long overlayStartTicks0 = captureStopwatch.Elapsed.Ticks;
                                    TryCompositeOverlayIntoTexture(_zeroCopyTexturePool[slot], rect, targetSize);
                                    Interlocked.Add(ref _captureStageOverlayNanos, (captureStopwatch.Elapsed.Ticks - overlayStartTicks0) * 100);

                                    long presentationHns0 = ComputeVideoPresentationTicks(captureStopwatch);
                                    long enqStart0 = captureStopwatch.Elapsed.Ticks;
                                    bool enqOk0 = TryEnqueueVideoPacket(new VideoPacket(slot, presentationHns0));
                                    Interlocked.Add(ref _captureStageEnqueueNanos, (captureStopwatch.Elapsed.Ticks - enqStart0) * 100);
                                    if (enqOk0)
                                    {
                                        Interlocked.Increment(ref _videoFramesQueued);
                                    }
                                    else
                                    {
                                        // Encoder back-pressure: return the slot to the free
                                        // pool so we don't permanently leak it.
                                        Interlocked.Increment(ref _videoFramesDropped);
                                        _zeroCopyFreeSlots.Enqueue(slot);
                                    }
                                }
                                goto IterEnd;
                            }
                            else if (_zeroCopyEnabled)
                            {
                                // No free slots — encoder is behind. Skip emit (wall-clock PTS
                                // will extend the previous frame's duration).
                                Interlocked.Increment(ref _captureLoopWgcMiss);
                                goto IterEnd;
                            }
                        }

                        // Path A (fast, common): the source window resolution exactly matches the
                        // recorder target. We rent a target-sized Bitmap from the pool and ask
                        // WGC to write directly into it - no extra copy.
                        // Path B (resize): we rent a Bitmap matching the *source* size, capture
                        // into it, then GDI+ resize once into a target-sized pooled Bitmap.

                        if (width == targetSize.Width && height == targetSize.Height)
                        {
                            long rentStart = captureStopwatch.Elapsed.Ticks;
                            Bitmap encodeFrame = RentBitmap(targetSize.Width, targetSize.Height);
                            Interlocked.Add(ref _captureStageRentBitmapNanos, (captureStopwatch.Elapsed.Ticks - rentStart) * 100);
                            long wgcStartTicks = captureStopwatch.Elapsed.Ticks;
                            bool captured = TryCapture(encodeFrame, rect);
                            long wgcEndTicks = captureStopwatch.Elapsed.Ticks;
                            Interlocked.Add(ref _captureStageWgcNanos, (wgcEndTicks - wgcStartTicks) * 100);
                            if (captured) Interlocked.Increment(ref _captureLoopWgcSuccess); else Interlocked.Increment(ref _captureLoopWgcMiss);
                            if (!captured)
                            {
                                ReturnBitmap(encodeFrame);
                            }
                            else
                            {
                                long presentationHns = ComputeVideoPresentationTicks(captureStopwatch);
                                long enqStart = captureStopwatch.Elapsed.Ticks;
                                bool enqOk = TryEnqueueVideoPacket(new VideoPacket(encodeFrame, presentationHns));
                                Interlocked.Add(ref _captureStageEnqueueNanos, (captureStopwatch.Elapsed.Ticks - enqStart) * 100);
                                if (enqOk)
                                {
                                    Interlocked.Increment(ref _videoFramesQueued);
                                }
                                else
                                {
                                    Interlocked.Increment(ref _videoFramesDropped);
                                    ReturnBitmap(encodeFrame);
                                }
                            }
                        }
                        else
                        {
                            // GPU-resize path: capture directly into a target-sized bitmap.
                            // WgcWindowCapture.CopyTextureToBitmap detects that the
                            // destination dimensions differ from the source frame size and
                            // performs a bilinear downscale on the GPU (HLSL pixel shader),
                            // then reads back only the (much smaller) target buffer. This
                            // replaces the prior CPU pipeline (full-source readback + GDI+
                            // / nearest-neighbor CPU scale), which was the dominant cost at
                            // 4K source - see WgcWindowCapture.GpuResize.cs for details.
                            long rentStart2 = captureStopwatch.Elapsed.Ticks;
                            Bitmap encodeFrame = RentBitmap(targetSize.Width, targetSize.Height);
                            Interlocked.Add(ref _captureStageRentBitmapNanos, (captureStopwatch.Elapsed.Ticks - rentStart2) * 100);

                            long wgcStartTicks = captureStopwatch.Elapsed.Ticks;
                            bool captured = TryCapture(encodeFrame, rect);
                            long wgcEndTicks = captureStopwatch.Elapsed.Ticks;
                            Interlocked.Add(ref _captureStageWgcNanos, (wgcEndTicks - wgcStartTicks) * 100);
                            if (captured) Interlocked.Increment(ref _captureLoopWgcSuccess); else Interlocked.Increment(ref _captureLoopWgcMiss);
                            if (captured)
                            {
                                // The GPU resize already produced the final target-sized
                                // frame; record 0 ns for stage_draw_scale to make it visible
                                // in diagnostics that the CPU scale step has been eliminated.
                                Interlocked.Add(ref _captureStageDrawScaleNanos, 0);
                                long presentationHns = ComputeVideoPresentationTicks(captureStopwatch);
                                long enqStart2 = captureStopwatch.Elapsed.Ticks;
                                bool enqOk2 = TryEnqueueVideoPacket(new VideoPacket(encodeFrame, presentationHns));
                                Interlocked.Add(ref _captureStageEnqueueNanos, (captureStopwatch.Elapsed.Ticks - enqStart2) * 100);
                                if (enqOk2)
                                {
                                    Interlocked.Increment(ref _videoFramesQueued);
                                }
                                else
                                {
                                    Interlocked.Increment(ref _videoFramesDropped);
                                    ReturnBitmap(encodeFrame);
                                }
                            }
                            else
                            {
                                ReturnBitmap(encodeFrame);
                            }
                            // else: skip emit on miss; wall-clock PTS extends previous frame's
                            // duration to cover the gap. See Path A for detailed rationale.
                        }
IterEnd: ;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        SetStopReason($"Capture error: {ex.Message}", true);
                        break;
                    }

                    long iterEndTicks = captureStopwatch.Elapsed.Ticks;
                    long bodyNanos = (iterEndTicks - throttleEndTicks) * 100;
                    Interlocked.Add(ref _captureLoopBodyNanos, bodyNanos);
                    Interlocked.Increment(ref _captureLoopIterations);
                    long prevMax;
                    do
                    {
                        prevMax = Interlocked.Read(ref _captureLoopMaxBodyNanos);
                        if (bodyNanos <= prevMax) break;
                    }
                    while (Interlocked.CompareExchange(ref _captureLoopMaxBodyNanos, bodyNanos, prevMax) != prevMax);
                }

                if (!_stopReasonSet)
                {
                    SetStopReason("Recording completed", false);
                }
            }
            finally
            {
                scaleScratch?.Dispose();
                _videoQueue.Writer.TryComplete();
            }
        }

        private bool TryCapture(Bitmap target, RECT rect)
        {
            if (_wgcCapture != null)
            {
                try
                {
                    if (_wgcCapture.TryAcquireFrame(target))
                    {
                        return true;
                    }
                    // No new WGC frame yet - return false; caller will fill black or reuse last.
                    // We do NOT fall back to PrintWindow on a per-frame miss because that would
                    // mix DWM-thumbnail frames into the swap-chain stream.
                    return false;
                }
                catch
                {
                    try { _wgcCapture.Dispose(); } catch { }
                    _wgcCapture = null;
                }
            }
            return TryCaptureWindow(_windowHandle, rect, target, _includeOverlay);
        }

        private bool TryCompositeOverlayIntoTexture(IntPtr texture, RECT windowRect, Size targetSize)
        {
            if (!_includeOverlay || _overlayCompositorUnavailable || _overlaySnapshotProvider == null)
            {
                return true;
            }

            var compositor = _overlayCompositor;
            if (compositor == null || !compositor.IsInitialized)
            {
                _overlayCompositorUnavailable = true;
                return false;
            }

            IReadOnlyList<RecordingOverlayBitmap> overlays;
            try
            {
                overlays = _overlaySnapshotProvider();
            }
            catch
            {
                return false;
            }

            if (overlays.Count == 0)
            {
                return true;
            }

            IntPtr targetBitmap = compositor.BeginDrawOnTexture(texture, out _);
            if (targetBitmap == IntPtr.Zero)
            {
                DisposeOverlayBitmaps(overlays);
                return false;
            }

            try
            {
                float sourceWidth = Math.Max(1, windowRect.Right - windowRect.Left);
                float sourceHeight = Math.Max(1, windowRect.Bottom - windowRect.Top);
                float scaleX = targetSize.Width / sourceWidth;
                float scaleY = targetSize.Height / sourceHeight;
                var targetBounds = new RectangleF(0, 0, targetSize.Width, targetSize.Height);

                foreach (var overlay in overlays)
                {
                    using Bitmap bitmap = overlay.Bitmap;
                    if (bitmap.Width <= 0 || bitmap.Height <= 0)
                    {
                        continue;
                    }

                    var dest = new RectangleF(
                        (overlay.ScreenLocation.X - windowRect.Left) * scaleX,
                        (overlay.ScreenLocation.Y - windowRect.Top) * scaleY,
                        bitmap.Width * scaleX,
                        bitmap.Height * scaleY);

                    if (!dest.IntersectsWith(targetBounds))
                    {
                        continue;
                    }

                    compositor.DrawOverlayBitmap(bitmap, dest);
                }
            }
            finally
            {
                int hr = compositor.EndDraw();
                if (targetBitmap != IntPtr.Zero) Marshal.Release(targetBitmap);
                if (hr < 0)
                {
                    _overlayCompositorUnavailable = true;
                }
            }

            return true;
        }

        private static void DisposeOverlayBitmaps(IReadOnlyList<RecordingOverlayBitmap> overlays)
        {
            foreach (var overlay in overlays)
            {
                try { overlay.Bitmap.Dispose(); } catch { }
            }
        }

        private async Task VideoEncodeLoopAsync(CancellationToken token)
        {
            try
            {
                await foreach (var packet in _videoQueue.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    if (packet.TextureSlot >= 0)
                    {
                        // Zero-copy path: hand the GPU texture (lives in our pool) directly
                        // to the encoder MFT via IMFDXGISurfaceBuffer. The slot is returned
                        // to the free queue once WriteSample has accepted it (the encoder
                        // internally copies into its own pool, so we may safely overwrite
                        // the slot on the next frame).
                        try
                        {
                            ((MediaFoundationFrameWriter)_writer).WriteVideoFrameTexture(
                                _zeroCopyTexturePool[packet.TextureSlot], 0, packet.PresentationTicks);
                            Interlocked.Increment(ref _videoFramesWritten);
                        }
                        catch (Exception ex)
                        {
                            SetStopReason($"Video encode error: {ex.Message}", true);
                            _zeroCopyFreeSlots.Enqueue(packet.TextureSlot);
                            // Drain remaining frames so the queue closes cleanly.
                            while (_videoQueue.Reader.TryRead(out var leftover))
                            {
                                if (leftover.TextureSlot >= 0) _zeroCopyFreeSlots.Enqueue(leftover.TextureSlot);
                                else if (leftover.Frame != null) ReturnBitmap(leftover.Frame);
                            }
                            break;
                        }

                        _zeroCopyFreeSlots.Enqueue(packet.TextureSlot);
                        continue;
                    }

                    var frame = packet.Frame;
                    try
                    {
                        _writer.WriteVideoFrame(frame, packet.PresentationTicks);
                        Interlocked.Increment(ref _videoFramesWritten);
                    }
                    catch (Exception ex)
                    {
                        SetStopReason($"Video encode error: {ex.Message}", true);
                        // Drain remaining frames so the queue closes cleanly.
                        ReturnBitmap(frame);
                        while (_videoQueue.Reader.TryRead(out var leftover))
                        {
                            if (leftover.TextureSlot >= 0) _zeroCopyFreeSlots.Enqueue(leftover.TextureSlot);
                            else if (leftover.Frame != null) ReturnBitmap(leftover.Frame);
                        }
                        break;
                    }

                    ReturnBitmap(frame);
                }
            }
            catch (OperationCanceledException)
            {
                while (_videoQueue.Reader.TryRead(out var leftover))
                {
                    if (leftover.TextureSlot >= 0) _zeroCopyFreeSlots.Enqueue(leftover.TextureSlot);
                    else if (leftover.Frame != null) ReturnBitmap(leftover.Frame);
                }
            }
        }

        // Allocates the rotating pool of GPU textures used by the zero-copy capture path.
        // All textures are sized to the encoder target, formatted as BGRA (matches NVENC's
        // ARGB input mode and Microsoft's Intel/AMD QSV MFTs), and bound as both
        // RENDER_TARGET (so the GPU resize shader can draw into them) and SHADER_RESOURCE
        // (so the encoder MFT can sample them). Pool depth = ZeroCopyPoolSize; see field
        // comment for rationale.
        private bool EnsureZeroCopyTexturePool(int width, int height)
        {
            if (_zeroCopyTexturesInitialized) return true;
            if (_zeroCopyD3DDevice == IntPtr.Zero) return false;

            var desc = new WgcWindowCapture.D3D11_TEXTURE2D_DESC_PUBLIC
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = 87, // DXGI_FORMAT_B8G8R8A8_UNORM
                SampleDesc_Count = 1,
                SampleDesc_Quality = 0,
                Usage = 0,                // DEFAULT
                BindFlags = 0x20 | 0x8,   // RENDER_TARGET | SHADER_RESOURCE
                CPUAccessFlags = 0,
                MiscFlags = 0,
            };

            for (int i = 0; i < ZeroCopyPoolSize; i++)
            {
                int hr = WgcWindowCapture.CreateTexture2DPublic(_zeroCopyD3DDevice, ref desc, out var tex);
                if (hr < 0 || tex == IntPtr.Zero)
                {
                    // Roll back any partially allocated slots so Dispose() does not see junk.
                    for (int j = 0; j < i; j++)
                    {
                        if (_zeroCopyTexturePool[j] != IntPtr.Zero) { Marshal.Release(_zeroCopyTexturePool[j]); _zeroCopyTexturePool[j] = IntPtr.Zero; }
                    }
                    return false;
                }
                _zeroCopyTexturePool[i] = tex;
                _zeroCopyFreeSlots.Enqueue(i);
            }

            _zeroCopyTexturesInitialized = true;
            return true;
        }

        // Fast nearest-neighbor BGRA → BGRA scaler. Replaces GDI+ DrawImage in the resize
        // path. Diagnostics on a 21s recording showed GDI+ DrawImage averaging ~80 ms / frame
        // at 4K → 1080p (single-threaded CPU code, dominant fps killer). This LockBits +
        // unsafe pointer copy form is ~10× faster (~5-8 ms / frame) and produces visually
        // identical output for game-recording use, where the source is rendered at the same
        // pixel grid as the target so anti-aliased downscale offers no perceptible benefit.
        private static unsafe void NearestNeighborScale(Bitmap source, Bitmap destination)
        {
            int srcW = source.Width;
            int srcH = source.Height;
            int dstW = destination.Width;
            int dstH = destination.Height;

            var srcRect = new Rectangle(0, 0, srcW, srcH);
            var dstRect = new Rectangle(0, 0, dstW, dstH);
            var srcData = source.LockBits(srcRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var dstData = destination.LockBits(dstRect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte* srcBase = (byte*)srcData.Scan0;
                    byte* dstBase = (byte*)dstData.Scan0;
                    int srcStride = srcData.Stride;
                    int dstStride = dstData.Stride;

                    // Pre-compute source X coordinate per destination column (fixed-point 16.16)
                    // so the inner loop is a single shift + 4-byte load/store.
                    int* xMap = stackalloc int[dstW];
                    long xStep = ((long)srcW << 16) / Math.Max(1, dstW);
                    long xAcc = xStep / 2; // half-pixel center
                    for (int x = 0; x < dstW; x++)
                    {
                        int sx = (int)(xAcc >> 16);
                        if (sx >= srcW) sx = srcW - 1;
                        xMap[x] = sx * 4;
                        xAcc += xStep;
                    }

                    long yStep = ((long)srcH << 16) / Math.Max(1, dstH);
                    long yAcc = yStep / 2;
                    for (int y = 0; y < dstH; y++)
                    {
                        int sy = (int)(yAcc >> 16);
                        if (sy >= srcH) sy = srcH - 1;
                        yAcc += yStep;
                        byte* srcRow = srcBase + (long)sy * srcStride;
                        uint* dstRow = (uint*)(dstBase + (long)y * dstStride);
                        for (int x = 0; x < dstW; x++)
                        {
                            dstRow[x] = *(uint*)(srcRow + xMap[x]);
                        }
                    }
                }
                finally
                {
                    destination.UnlockBits(dstData);
                }
            }
            finally
            {
                source.UnlockBits(srcData);
            }
        }

        private Bitmap RentBitmap(int width, int height)
        {
            // Discard pooled bitmaps that don't match the current size (window resized).
            while (_bitmapPool.TryTake(out var pooled))
            {
                if (pooled.Width == width && pooled.Height == height && pooled.PixelFormat == PixelFormat.Format32bppArgb)
                {
                    return pooled;
                }
                pooled.Dispose();
            }
            return new Bitmap(width, height, PixelFormat.Format32bppArgb);
        }

        private void ReturnBitmap(Bitmap bitmap)
        {
            // Cap pool size to avoid unbounded retention if the encoder briefly stalls.
            if (_bitmapPool.Count >= VideoQueueDepth + 2)
            {
                bitmap.Dispose();
                return;
            }
            _bitmapPool.Add(bitmap);
        }

        private async Task CaptureAudioLoopAsync(CancellationToken token)
        {
            var capture = _audioCapture;
            if (capture == null)
            {
                return;
            }

            try
            {
                await capture.CaptureAsync((buffer, frames) =>
                {
                    if (buffer.IsEmpty || frames <= 0) return;

                    // Record the wall-clock arrival time of the very first audio packet so the
                    // disposer can compute the (video_first - audio_first) delta and trim that
                    // many seconds of leading audio at mux time. Without this, the audio stream
                    // begins ~100-300ms before the first video frame is delivered (WGC cold-start
                    // latency) and is audibly ahead of the picture.
                    if (Interlocked.Read(ref _firstAudioPacketWallTicks) < 0)
                    {
                        long elapsed = Stopwatch.GetTimestamp() - _wallClockStartTicks;
                        Interlocked.CompareExchange(ref _firstAudioPacketWallTicks, elapsed, -1);
                    }

                    // Copy WASAPI's transient buffer into a pooled byte[] so the writer thread
                    // can drain at its own pace. Doing the WAV file write directly from this
                    // callback risks blocking the WASAPI capture client which causes audible
                    // "音割れ" (sample drops) under any disk / encoder contention.
                    var pooled = ArrayPool<byte>.Shared.Rent(buffer.Length);
                    buffer.CopyTo(pooled);
                    if (!_audioQueue.Writer.TryWrite(new AudioPacket(pooled, buffer.Length, frames)))
                    {
                        // Channel is unbounded, but be defensive.
                        ArrayPool<byte>.Shared.Return(pooled);
                    }
                }, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SetStopReason($"Audio capture error: {ex.Message}", true);
            }
            finally
            {
                _audioQueue.Writer.TryComplete();
            }
        }

        private async Task AudioWriteLoopAsync(CancellationToken token)
        {
            try
            {
                await foreach (var packet in _audioQueue.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    try
                    {
                        lock (_audioLock)
                        {
                            _writer.WriteAudioSample(packet.Buffer.AsSpan(0, packet.ByteCount), packet.FrameCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        SetStopReason($"Audio write error: {ex.Message}", true);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(packet.Buffer);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Drain remaining buffers.
                while (_audioQueue.Reader.TryRead(out var leftover))
                {
                    ArrayPool<byte>.Shared.Return(leftover.Buffer);
                }
            }
            finally
            {
                try
                {
                    lock (_audioLock)
                    {
                        _writer.CompleteAudio();
                    }
                }
                catch
                {
                }
            }
        }

        private void SetStopReason(string reason, bool error)
        {
            lock (_stateLock)
            {
                if (_stopReasonSet)
                {
                    if (error && !_hasError)
                    {
                        _hasError = true;
                    }

                    return;
                }

                _stopReason = reason;
                _stopReasonSet = true;
                if (error)
                {
                    _hasError = true;
                }
            }
        }

        public void Dispose()
        {
            bool shouldDispose;
            
            lock (_stateLock)
            {
                shouldDispose = !_disposed;
                if (shouldDispose)
                {
                    _disposed = true;
                }
            }

            if (!shouldDispose)
            {
                return;
            }

            try
            {
                Stop("Recorder disposed");
            }
            catch
            {
            }

            _cts.Dispose();
            _audioCapture?.Dispose();
            _writer.Dispose();
            _wgcCapture?.Dispose();
            _overlayCompositor?.Dispose();
            _overlayCompositor = null;

            // Release the zero-copy pool textures and the shared D3D11 device/context refs
            // we obtained from the writer. Order matters: textures must go before the device
            // they were created on.
            for (int i = 0; i < _zeroCopyTexturePool.Length; i++)
            {
                if (_zeroCopyTexturePool[i] != IntPtr.Zero)
                {
                    try { Marshal.Release(_zeroCopyTexturePool[i]); } catch { }
                    _zeroCopyTexturePool[i] = IntPtr.Zero;
                }
            }
            if (_zeroCopyD3DContext != IntPtr.Zero) { try { Marshal.Release(_zeroCopyD3DContext); } catch { } _zeroCopyD3DContext = IntPtr.Zero; }
            if (_zeroCopyD3DDevice != IntPtr.Zero) { try { Marshal.Release(_zeroCopyD3DDevice); } catch { } _zeroCopyD3DDevice = IntPtr.Zero; }

            if (_timerResolutionRaised)
            {
                try { timeEndPeriod(1); } catch { }
                _timerResolutionRaised = false;
            }
        }

        private static IntPtr FindTargetWindow(string hint)
        {
            var hints = SplitHints(hint).ToArray();
            var candidates = EnumerateWindows();
            if (candidates.Count == 0)
            {
                return IntPtr.Zero;
            }

            WindowCandidate? best = null;

            foreach (var candidate in candidates)
            {
                int score = ScoreCandidate(candidate, hints);
                if (score <= 0)
                {
                    continue;
                }

                if (best is null
                    || score > best.Value.Score
                    || (score == best.Value.Score && candidate.ZOrder < best.Value.ZOrder))
                {
                    best = new WindowCandidate(candidate, score);
                }
            }

            return best?.Info.Handle ?? IntPtr.Zero;
        }

        private static IEnumerable<string> SplitHints(string hint)
        {
            if (string.IsNullOrWhiteSpace(hint))
            {
                yield return "VRChat";
                yield break;
            }

            foreach (var part in hint.Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var value = part.Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    yield return value;
                }
            }
        }

        private static int ScoreCandidate(WindowInfo info, IReadOnlyList<string> hints)
        {
            int best = 0;

            if (hints.Count == 0)
            {
                return info.IsVisible ? 1 : 0;
            }

            foreach (var hint in hints)
            {
                int score = ScoreHint(info, hint);
                if (score > best)
                {
                    best = score;
                }
            }

            return best;
        }

        private static int ScoreHint(WindowInfo info, string hint)
        {
            if (string.IsNullOrWhiteSpace(hint))
            {
                return 0;
            }

            string value = hint;
            string? qualifier = null;

            int colonIndex = hint.IndexOf(':');
            if (colonIndex > 0)
            {
                qualifier = hint.Substring(0, colonIndex).Trim();
                value = hint.Substring(colonIndex + 1).Trim();
            }

            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            bool exact = false;
            bool partial = false;

            if (qualifier == null)
            {
                exact |= EqualsIgnoreCase(info.Title, value);
                partial |= ContainsIgnoreCase(info.Title, value);

                exact |= EqualsIgnoreCase(info.ProcessName, value);
                partial |= ContainsIgnoreCase(info.ProcessName, value);

                exact |= EqualsIgnoreCase(info.ClassName, value);
                partial |= ContainsIgnoreCase(info.ClassName, value);
            }
            else
            {
                switch (qualifier.ToLowerInvariant())
                {
                    case "title":
                        exact = EqualsIgnoreCase(info.Title, value);
                        partial = ContainsIgnoreCase(info.Title, value);
                        break;
                    case "process":
                        exact = EqualsIgnoreCase(info.ProcessName, value);
                        partial = ContainsIgnoreCase(info.ProcessName, value);
                        break;
                    case "class":
                        exact = EqualsIgnoreCase(info.ClassName, value);
                        partial = ContainsIgnoreCase(info.ClassName, value);
                        break;
                    default:
                        partial = ContainsIgnoreCase(info.Title, value) || ContainsIgnoreCase(info.ProcessName, value);
                        break;
                }
            }

            if (exact)
            {
                return info.IsVisible ? 200 : 150;
            }

            if (partial)
            {
                return info.IsVisible ? 120 : 90;
            }

            return 0;
        }

        private static bool EqualsIgnoreCase(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static bool ContainsIgnoreCase(string source, string value)
        {
            if (string.IsNullOrEmpty(source))
            {
                return false;
            }

            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<WindowInfo> EnumerateWindows()
        {
            var list = new List<WindowInfo>();
            int index = 0;
            EnumWindows((handle, _) =>
            {
                index++;
                try
                {
                    list.Add(CreateWindowInfo(handle, index));
                }
                catch
                {
                }

                return true;
            }, IntPtr.Zero);

            return list;
        }

        private static WindowInfo CreateWindowInfo(IntPtr handle, int zOrder)
        {
            string title = GetWindowTitle(handle);
            string className = GetWindowClass(handle);
            string processName = GetProcessName(handle);
            bool visible = IsWindowVisible(handle);

            return new WindowInfo(handle, title, className, processName, zOrder, visible);
        }

        private static string GetWindowTitle(IntPtr handle)
        {
            int length = GetWindowTextLength(handle);
            if (length <= 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(length + 1);
            if (GetWindowText(handle, builder, builder.Capacity) == 0)
            {
                return string.Empty;
            }

            return builder.ToString();
        }

        private static string GetWindowClass(IntPtr handle)
        {
            var builder = new StringBuilder(256);
            if (GetClassName(handle, builder, builder.Capacity) == 0)
            {
                return string.Empty;
            }

            return builder.ToString();
        }

        private static string GetProcessName(IntPtr handle)
        {
            try
            {
                _ = GetWindowThreadProcessId(handle, out uint processId);
                if (processId == 0)
                {
                    return string.Empty;
                }

                using var process = Process.GetProcessById((int)processId);
                return process.ProcessName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryGetWindowBounds(IntPtr handle, out RECT rect)
        {
            if (DwmGetWindowAttribute(handle, DWMWA_EXTENDED_FRAME_BOUNDS, out var extendedRect, Marshal.SizeOf<RECT>()) == 0)
            {
                rect = extendedRect;
                return true;
            }

            if (GetWindowRect(handle, out rect))
            {
                return true;
            }

            rect = default;
            return false;
        }

        private static bool TryCaptureWindow(IntPtr handle, RECT rect, Bitmap bitmap, bool includeOverlay)
        {
            if (bitmap == null)
            {
                return false;
            }

            // PrintWindow(PW_RENDERFULLCONTENT) is the only path that captures DirectX swap-chain
            // content for occluded / minimized windows, but on real DirectX games (VRChat etc.) on
            // Windows 11 24H2 it can take 800ms-1s per call because it forces a CPU-side
            // composite of the entire swap chain. That cripples the recorder to ~1 actual capture
            // per second, even though the encoder happily writes 30 duplicate frames around it
            // (the file ends up "30 fps but visually updates once per second").
            //
            // We measure PrintWindow on the first few frames; if it is ever slower than a
            // half-frame at 30 fps we permanently switch to CopyFromScreen (BitBlt under the
            // hood, ~1-3 ms even at 4K) for the rest of the recording. This loses the ability to
            // capture a hidden / occluded window but recovers the actual frame rate.
            if (_printWindowTooSlow)
            {
                if (TryCopyWindowFromScreen(rect, bitmap))
                {
                    return true;
                }

                return TryCaptureWithPrintWindow(handle, bitmap);
            }

            if (includeOverlay)
            {
                if (TryCopyWindowFromScreen(rect, bitmap))
                {
                    return true;
                }

                return TryCaptureWithPrintWindow(handle, bitmap);
            }

            // Default path: try PrintWindow first (preserves window content even when occluded),
            // but time it so we can detect the VRChat / DirectX slowdown.
            var sw = Stopwatch.StartNew();
            bool ok = TryCaptureWithPrintWindow(handle, bitmap);
            sw.Stop();

            if (ok)
            {
                // 30 fps -> 33 ms / frame. Anything past ~20 ms means we cannot keep up.
                if (sw.ElapsedMilliseconds > 20)
                {
                    _printWindowSlowSamples++;
                    if (_printWindowSlowSamples >= 3)
                    {
                        _printWindowTooSlow = true;
                    }
                }
                else
                {
                    _printWindowSlowSamples = 0;
                }

                return true;
            }

            return TryCopyWindowFromScreen(rect, bitmap);
        }

        // Static so the decision survives across capture calls in this process. The recorder is
        // single-instance per session, and the cost of one extra PrintWindow on a fresh start is
        // acceptable to re-detect after restart.
        private static int _printWindowSlowSamples;
        private static bool _printWindowTooSlow;

        private static bool TryCaptureWithPrintWindow(IntPtr handle, Bitmap bitmap)
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black);
            IntPtr hdc = graphics.GetHdc();
            try
            {
                return PrintWindow(handle, hdc, PW_RENDERFULLCONTENT);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        private static bool TryCopyWindowFromScreen(RECT rect, Bitmap bitmap)
        {
            var origin = new System.Drawing.Point(rect.Left, rect.Top);
            try
            {
                using var fallback = Graphics.FromImage(bitmap);
                fallback.CopyFromScreen(origin, System.Drawing.Point.Empty, bitmap.Size, CopyPixelOperation.SourceCopy);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private readonly struct WindowInfo
        {
            public WindowInfo(IntPtr handle, string title, string className, string processName, int zOrder, bool isVisible)
            {
                Handle = handle;
                Title = title;
                ClassName = className;
                ProcessName = processName;
                ZOrder = zOrder;
                IsVisible = isVisible;
            }

            public IntPtr Handle { get; }
            public string Title { get; }
            public string ClassName { get; }
            public string ProcessName { get; }
            public int ZOrder { get; }
            public bool IsVisible { get; }
        }

        private readonly struct WindowCandidate
        {
            public WindowCandidate(WindowInfo info, int score)
            {
                Info = info;
                Score = score;
            }

            public WindowInfo Info { get; }
            public int Score { get; }
            public int ZOrder => Info.ZOrder;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, uint dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint timeEndPeriod(uint uPeriod);

        private const uint PW_RENDERFULLCONTENT = 0x00000002;
        private const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
