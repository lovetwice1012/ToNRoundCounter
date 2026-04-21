#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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

        private InternalScreenRecorder(IntPtr windowHandle, int frameRate, Size targetSize, bool includeOverlay, IMediaWriter writer, WasapiAudioCapture? audioCapture)
        {
            _windowHandle = windowHandle;
            _frameRate = frameRate;
            _targetSize = targetSize;
            _includeOverlay = includeOverlay;
            _cts = new CancellationTokenSource();
            _writer = writer;
            _audioCapture = audioCapture;
            _isHardwareAccelerated = writer.IsHardwareAccelerated;
            _videoTask = Task.Run(() => CaptureVideoLoopAsync(_cts.Token));

            if (_audioCapture != null)
            {
                _audioTask = Task.Run(() => CaptureAudioLoopAsync(_cts.Token));
            }

            _completionTask = _audioTask != null ? Task.WhenAll(_videoTask, _audioTask) : _videoTask;
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

        public static bool TryCreate(string windowHint, int requestedFrameRate, string resolutionOptionId, string outputPath, string extension, string codecId, bool includeOverlay, int videoBitrate, int audioBitrate, HardwareEncoderSelection hardwareSelection, bool captureAudio, out InternalScreenRecorder? recorder, out int actualFrameRate, out Size targetResolution, out string? failureReason)
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
                    if (!WasapiAudioCapture.TryCreateForWindow(handle, out audioCapture, out var audioError))
                    {
                        // Do NOT abort the whole recording when audio capture init fails. Common
                        // causes are non-44100/48000 device sample rates and exclusive-mode usage.
                        // Falling back to video-only matches the SetInputMediaType(Audio) fallback
                        // path below and keeps the recording usable. The reason is surfaced via
                        // AudioFallbackReason so the caller can log it.
                        wasapiFailureReason = string.IsNullOrEmpty(audioError)
                            ? "Failed to initialize audio capture for the target window."
                            : audioError;
                        audioCapture?.Dispose();
                        audioCapture = null;
                        audioFormat = null;
                    }
                    else
                    {
                        audioFormat = audioCapture?.Format;
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
                recorder = new InternalScreenRecorder(handle, frameRate, targetResolution, includeOverlay, writer, audioCaptureToTransfer);
                recorder.AudioFallbackReason = audioFallbackReason;
                if (writer is MediaFoundationFrameWriter mfWriter)
                {
                    recorder.HardwareFallbackReason = mfWriter.HardwareFallbackReason;
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
                        try
                        {
                            return MediaFoundationFrameWriter.Create(normalizedExtension, codecId, outputPath, width, height, frameRate, audioFormat, videoBitrate, audioBitrate, hardwareSelection);
                        }
                        catch (Exception ex) when (IsAudioMediaTypeNegotiationFailure(ex))
                        {
                            // Some Windows audio endpoint formats cannot be connected to encoder MFTs
                            // through SinkWriter's automatic topology. Fall back to video-only capture
                            // instead of aborting the entire recording start sequence -- but remember
                            // the original error message so the caller can log why the recording is
                            // running without audio.
                            audioFallbackReason = ExtractFirstMessage(ex);
                            return MediaFoundationFrameWriter.Create(normalizedExtension, codecId, outputPath, width, height, frameRate, null, videoBitrate, audioBitrate, hardwareSelection);
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
                    message.IndexOf("0xC00D36B2", StringComparison.OrdinalIgnoreCase) >= 0)
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
        }

        private async Task CaptureVideoLoopAsync(CancellationToken token)
        {
            var frameInterval = TimeSpan.FromSeconds(1d / Math.Max(1, _frameRate));
            long frameIntervalTicks = Math.Max(1L, frameInterval.Ticks);
            var captureStopwatch = Stopwatch.StartNew();
            long framesProduced = 0;
            var targetSize = _targetSize;
            using var outputFrame = new Bitmap(targetSize.Width, targetSize.Height, PixelFormat.Format32bppArgb);
            Bitmap? captureFrame = null;
            Bitmap? duplicateFrame = null;
            bool duplicateFrameValid = false;

            try
            {
                duplicateFrame = new Bitmap(targetSize.Width, targetSize.Height, PixelFormat.Format32bppArgb);

                while (!token.IsCancellationRequested)
                {
                    if (duplicateFrameValid)
                    {
                        var elapsedBeforeCapture = captureStopwatch.Elapsed;
                        long expectedFramesBeforeCapture = (elapsedBeforeCapture.Ticks / frameIntervalTicks) + 1;
                        if (duplicateFrame is null)
                        {
                            throw new InvalidOperationException("Duplicate frame buffer is unavailable.");
                        }

                        while (framesProduced < expectedFramesBeforeCapture - 1)
                        {
                            _writer.WriteVideoFrame(duplicateFrame);
                            framesProduced++;
                        }
                    }

                    var elapsed = captureStopwatch.Elapsed;
                    var targetTimestamp = TimeSpan.FromTicks(framesProduced * frameIntervalTicks);
                    if (elapsed < targetTimestamp)
                    {
                        var delay = targetTimestamp - elapsed;
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

                    if (captureFrame == null || captureFrame.Width != width || captureFrame.Height != height)
                    {
                        captureFrame?.Dispose();
                        captureFrame = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    }

                    bool captured = TryCaptureWindow(_windowHandle, rect, captureFrame, _includeOverlay);

                    try
                    {
                        using (var graphics = Graphics.FromImage(outputFrame))
                        {
                            graphics.Clear(Color.Black);
                            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                            if (captured)
                            {
                                graphics.DrawImage(captureFrame, new Rectangle(System.Drawing.Point.Empty, targetSize),
                                    new Rectangle(System.Drawing.Point.Empty, captureFrame.Size), GraphicsUnit.Pixel);
                            }
                        }

                        _writer.WriteVideoFrame(outputFrame);
                        framesProduced++;

                        if (duplicateFrame != null)
                        {
                            using var duplicateGraphics = Graphics.FromImage(duplicateFrame);
                            duplicateGraphics.DrawImageUnscaled(outputFrame, 0, 0);
                            duplicateFrameValid = true;
                        }

                        if (duplicateFrameValid)
                        {
                            var elapsedAfterCapture = captureStopwatch.Elapsed;
                            long expectedFramesAfterCapture = (elapsedAfterCapture.Ticks / frameIntervalTicks) + 1;
                            if (duplicateFrame is null)
                            {
                                throw new InvalidOperationException("Duplicate frame buffer is unavailable.");
                            }

                            while (framesProduced < expectedFramesAfterCapture)
                            {
                                _writer.WriteVideoFrame(duplicateFrame);
                                framesProduced++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        SetStopReason($"Capture error: {ex.Message}", true);
                        break;
                    }

                }

                if (!_stopReasonSet)
                {
                    SetStopReason("Recording completed", false);
                }
            }
            finally
            {
                captureFrame?.Dispose();
                duplicateFrame?.Dispose();
            }
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
                    try
                    {
                        lock (_audioLock)
                        {
                            _writer.WriteAudioSample(buffer, frames);
                        }
                    }
                    catch (Exception ex)
                    {
                        SetStopReason($"Audio capture error: {ex.Message}", true);
                        throw;
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

            if (includeOverlay)
            {
                if (TryCopyWindowFromScreen(rect, bitmap))
                {
                    return true;
                }

                return TryCaptureWithPrintWindow(handle, bitmap);
            }

            if (TryCaptureWithPrintWindow(handle, bitmap))
            {
                return true;
            }

            return TryCopyWindowFromScreen(rect, bitmap);
        }

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
