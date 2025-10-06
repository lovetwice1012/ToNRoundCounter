using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ToNRoundCounter.Domain;

namespace ToNRoundCounter.Application
{
    public sealed class AutoRecordingService : IDisposable
    {
        private readonly StateService _stateService;
        private readonly IAppSettings _settings;
        private readonly IEventLogger _logger;
        private readonly object _sync = new object();
        private InternalScreenRecorder? _recorder;
        private bool _disposed;
        private string? _currentTriggerDescription;

        public AutoRecordingService(StateService stateService, IAppSettings settings, IEventLogger logger)
        {
            _stateService = stateService;
            _settings = settings;
            _logger = logger;
            _stateService.StateChanged += HandleStateChanged;
        }

        private void HandleStateChanged()
        {
            EvaluateRecordingState("StateChanged");
        }

        public void EvaluateRecordingState(string reason)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                if (!_settings.AutoRecordingEnabled)
                {
                    StopRecording("Disabled");
                    return;
                }

                var round = _stateService.CurrentRound;
                if (round == null)
                {
                    StopRecording("No active round");
                    return;
                }

                var roundTriggers = NormalizeTriggers(_settings.AutoRecordingRoundTypes);
                var terrorTriggers = NormalizeTriggers(_settings.AutoRecordingTerrors);
                bool hasRoundTriggers = roundTriggers.Count > 0;
                bool hasTerrorTriggers = terrorTriggers.Count > 0;

                bool roundTriggered = false;
                if (hasRoundTriggers && !string.IsNullOrWhiteSpace(round.RoundType))
                {
                    roundTriggered = TriggerMatches(round.RoundType!, roundTriggers);
                }

                bool terrorTriggered = false;
                if (hasTerrorTriggers && !string.IsNullOrWhiteSpace(round.TerrorKey))
                {
                    var terrors = SplitTerrorNames(round.TerrorKey);
                    terrorTriggered = terrors.Any(t => TriggerMatches(t, terrorTriggers));
                }

                bool shouldRecord = hasRoundTriggers || hasTerrorTriggers
                    ? roundTriggered || terrorTriggered
                    : false;

                if (!shouldRecord)
                {
                    StopRecording("No matching triggers");
                    return;
                }

                if (_recorder != null)
                {
                    return;
                }

                var triggerDetails = BuildTriggerDescription(round.RoundType, roundTriggered, round.TerrorKey, terrorTriggered);
                StartRecording(triggerDetails);
            }
        }

        private void StartRecording(string triggerDetails)
        {
            string outputDirectory = ResolveOutputDirectory();
            try
            {
                Directory.CreateDirectory(outputDirectory);
            }
            catch (Exception ex)
            {
                _logger.LogEvent("AutoRecording", () => $"Failed to create output directory '{outputDirectory}': {ex.Message}", Serilog.Events.LogEventLevel.Error);
                return;
            }

            string extension = NormalizeExtension(_settings.AutoRecordingOutputExtension);
            string fileName = GenerateOutputFileName(triggerDetails, extension);
            string outputPath = Path.Combine(outputDirectory, fileName);
            int frameRate = NormalizeFrameRate(_settings.AutoRecordingFrameRate);
            string windowHint = string.IsNullOrWhiteSpace(_settings.AutoRecordingWindowTitle)
                ? "VRChat"
                : _settings.AutoRecordingWindowTitle.Trim();

            if (!InternalScreenRecorder.TryCreate(windowHint, frameRate, outputPath, extension, out var recorder, out var error))
            {
                _logger.LogEvent("AutoRecording", () => $"Failed to start built-in recorder: {error}", Serilog.Events.LogEventLevel.Error);
                return;
            }

            _recorder = recorder;
            _currentTriggerDescription = triggerDetails;
            recorder.Completion.ContinueWith(_ => HandleRecorderCompleted(recorder), TaskScheduler.Default);
            _logger.LogEvent("AutoRecording", () => $"Recording started. Output: {outputPath}. Trigger: {triggerDetails}");
        }

        private void HandleRecorderCompleted(InternalScreenRecorder recorder)
        {
            string? trigger = null;
            string? reason = null;
            Serilog.Events.LogEventLevel level = Serilog.Events.LogEventLevel.Information;
            bool shouldLog = false;

            lock (_sync)
            {
                if (_recorder != recorder)
                {
                    return;
                }

                trigger = _currentTriggerDescription ?? "<unknown>";
                _currentTriggerDescription = null;
                _recorder = null;

                if (!recorder.StoppedByOwner)
                {
                    shouldLog = true;
                    reason = recorder.StopReason;
                    if (recorder.HasError)
                    {
                        level = Serilog.Events.LogEventLevel.Warning;
                    }
                }
                else
                {
                    reason = recorder.StopReason;
                }
            }

            try
            {
                recorder.Dispose();
            }
            catch
            {
            }

            if (shouldLog)
            {
                string triggerText = trigger ?? "<unknown>";
                string reasonText = reason ?? "<unknown>";
                _logger.LogEvent("AutoRecording", () => $"Recording stopped automatically. Reason: {reasonText}. Last trigger: {triggerText}.", level);
            }
        }

        private void StopRecording(string reason)
        {
            if (_recorder == null)
            {
                return;
            }

            var recorder = _recorder;
            _recorder = null;
            var trigger = _currentTriggerDescription ?? "<unknown>";
            _currentTriggerDescription = null;

            Serilog.Events.LogEventLevel level = Serilog.Events.LogEventLevel.Information;

            try
            {
                recorder.Stop(reason);
                if (recorder.HasError)
                {
                    level = Serilog.Events.LogEventLevel.Warning;
                }
            }
            catch (Exception ex)
            {
                level = Serilog.Events.LogEventLevel.Warning;
                _logger.LogEvent("AutoRecording", () => $"Failed to stop recorder cleanly: {ex.Message}", Serilog.Events.LogEventLevel.Warning);
            }
            finally
            {
                try
                {
                    recorder.Dispose();
                }
                catch
                {
                }

                _logger.LogEvent("AutoRecording", () => $"Recording stopped. Reason: {recorder.StopReason}. Last trigger: {trigger}.", level);
            }
        }

        private static List<string> NormalizeTriggers(IEnumerable<string>? values)
        {
            if (values == null)
            {
                return new List<string>();
            }

            return values
                .Select(v => (v ?? string.Empty).Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
        }

        private static IEnumerable<string> SplitTerrorNames(string? terrorKey)
        {
            if (string.IsNullOrWhiteSpace(terrorKey))
            {
                yield break;
            }

            foreach (var part in terrorKey!.Split(new[] { '&', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var value = part.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }

        private static bool TriggerMatches(string value, IReadOnlyCollection<string> triggers)
        {
            if (triggers.Count == 0)
            {
                return false;
            }

            foreach (var trigger in triggers)
            {
                if (string.Equals(trigger, "*", StringComparison.Ordinal))
                {
                    return true;
                }

                if (string.Equals(trigger, value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildTriggerDescription(string? roundType, bool roundTriggered, string? terrorKey, bool terrorTriggered)
        {
            var builder = new StringBuilder();
            if (roundTriggered && !string.IsNullOrWhiteSpace(roundType))
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, "Round='{0}'", roundType);
            }

            if (terrorTriggered && !string.IsNullOrWhiteSpace(terrorKey))
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.AppendFormat(CultureInfo.InvariantCulture, "Terror='{0}'", terrorKey);
            }

            if (builder.Length == 0)
            {
                builder.Append("Manual");
            }

            return builder.ToString();
        }

        private static string NormalizeExtension(string? extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return SupportedExtensions[0];
            }

            var trimmed = extension.Trim().TrimStart('.');
            foreach (var candidate in SupportedExtensions)
            {
                if (trimmed.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return SupportedExtensions[0];
        }

        internal static readonly string[] SupportedExtensions = new[]
        {
            "avi",
            "mp4",
            "mov",
            "wmv",
            "mpg",
            "mkv",
            "flv",
            "asf",
            "vob",
            "gif",
        };

        private static int NormalizeFrameRate(int frameRate)
        {
            if (frameRate < 5)
            {
                return 5;
            }

            if (frameRate > 60)
            {
                return 60;
            }

            return frameRate;
        }

        private string ResolveOutputDirectory()
        {
            string configured = (_settings.AutoRecordingOutputDirectory ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(configured))
            {
                configured = "recordings";
            }

            if (!Path.IsPathRooted(configured))
            {
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
                return Path.GetFullPath(Path.Combine(baseDirectory, configured));
            }

            return configured;
        }

        private static string GenerateOutputFileName(string triggerDetails, string extension)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string sanitizedTrigger = SanitizeForFileName(triggerDetails ?? string.Empty);
            if (string.IsNullOrWhiteSpace(sanitizedTrigger))
            {
                sanitizedTrigger = "recording";
            }

            string extWithDot = extension.StartsWith(".", StringComparison.Ordinal) ? extension : $".{extension}";
            return $"{timestamp}_{sanitizedTrigger}{extWithDot}";
        }

        private static string SanitizeForFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (invalidChars.Contains(c) || char.IsWhiteSpace(c))
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(c);
                }
            }

            string sanitized = builder.ToString();
            while (sanitized.Contains("__"))
            {
                sanitized = sanitized.Replace("__", "_");
            }

            return sanitized.Trim('_');
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _stateService.StateChanged -= HandleStateChanged;
                StopRecording("Service disposed");
            }
        }

        private sealed class InternalScreenRecorder : IDisposable
        {
            private readonly IntPtr _windowHandle;
            private readonly Rectangle _bounds;
            private readonly int _frameRate;
            private readonly IFrameWriter _writer;
            private readonly CancellationTokenSource _cts;
            private readonly Task _captureTask;
            private readonly object _stateLock = new object();
            private string _stopReason = "Completed";
            private bool _stopReasonSet;
            private bool _hasError;
            private bool _stopRequested;
            private bool _disposed;

            private InternalScreenRecorder(IntPtr windowHandle, Rectangle bounds, int frameRate, IFrameWriter writer)
            {
                _windowHandle = windowHandle;
                _bounds = bounds;
                _frameRate = frameRate;
                _cts = new CancellationTokenSource();
                _writer = writer;
                _captureTask = Task.Run(() => CaptureLoopAsync(_cts.Token));
            }

            public Task Completion => _captureTask;

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

            public static bool TryCreate(string windowHint, int frameRate, string outputPath, string extension, out InternalScreenRecorder? recorder, out string? failureReason)
            {
                recorder = null;
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

                var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);

                IFrameWriter? writer = null;

                try
                {
                    writer = CreateWriter(extension, outputPath, bounds.Width, bounds.Height, frameRate);
                    recorder = new InternalScreenRecorder(handle, bounds, frameRate, writer);
                    return true;
                }
                catch (Exception ex)
                {
                    failureReason = ex.Message;
                    writer?.Dispose();
                    recorder?.Dispose();
                    recorder = null;
                    return false;
                }
            }

            private static IFrameWriter CreateWriter(string extension, string outputPath, int width, int height, int frameRate)
            {
                switch ((extension ?? string.Empty).ToLowerInvariant())
                {
                    case "gif":
                        return new GifFrameWriter(outputPath, width, height, frameRate);
                    case "avi":
                        return new SimpleAviWriter(outputPath, width, height, frameRate);
                    default:
                        return MediaFoundationFrameWriter.Create(extension!, outputPath, width, height, frameRate);
                }
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
                    _captureTask.Wait();
                }
                catch (AggregateException ex)
                {
                    ex.Handle(e => e is OperationCanceledException);
                }
                catch (OperationCanceledException)
                {
                }
            }

            private async Task CaptureLoopAsync(CancellationToken token)
            {
                var frameInterval = TimeSpan.FromSeconds(1d / Math.Max(1, _frameRate));
                var nextFrame = DateTime.UtcNow;
                var targetSize = _bounds.Size;
                using var outputFrame = new Bitmap(targetSize.Width, targetSize.Height, PixelFormat.Format32bppArgb);
                Bitmap? captureFrame = null;

                try
                {
                    while (!token.IsCancellationRequested)
                    {
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

                    bool captured = TryCaptureWindow(_windowHandle, rect, captureFrame);

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

                        _writer.WriteFrame(outputFrame);
                    }
                    catch (Exception ex)
                    {
                        SetStopReason($"Capture error: {ex.Message}", true);
                        break;
                    }

                        nextFrame += frameInterval;
                        var delay = nextFrame - DateTime.UtcNow;
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
                        else
                        {
                            nextFrame = DateTime.UtcNow;
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
                try
                {
                    Stop("Recorder disposed");
                }
                catch
                {
                }

                lock (_stateLock)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                }

                _cts.Dispose();
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

                    if (best == null || score > best.Score || (score == best.Score && candidate.ZOrder < best.ZOrder))
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

            private static bool TryCaptureWindow(IntPtr handle, RECT rect, Bitmap bitmap)
            {
                if (bitmap == null)
                {
                    return false;
                }

                bool captured = false;

                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Black);
                    IntPtr hdc = graphics.GetHdc();
                    try
                    {
                        captured = PrintWindow(handle, hdc, PW_RENDERFULLCONTENT);
                    }
                    finally
                    {
                        graphics.ReleaseHdc(hdc);
                    }
                }

                if (!captured)
                {
                    var origin = new System.Drawing.Point(rect.Left, rect.Top);
                    try
                    {
                        using var fallback = Graphics.FromImage(bitmap);
                        fallback.CopyFromScreen(origin, System.Drawing.Point.Empty, bitmap.Size, CopyPixelOperation.SourceCopy);
                        captured = true;
                    }
                    catch
                    {
                        captured = false;
                    }
                }

                return captured;
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
        }

        private interface IFrameWriter : IDisposable
        {
            void WriteFrame(Bitmap frame);
        }

        private sealed class SimpleAviWriter : IFrameWriter
        {
            private readonly int _width;
            private readonly int _height;
            private readonly int _frameSize;
            private IntPtr _fileHandle;
            private IntPtr _streamHandle;
            private int _frameIndex;
            private bool _disposed;
            private byte[]? _copyBuffer;

            private const int OF_WRITE = 0x00000001;
            private const int OF_CREATE = 0x00001000;
            private const uint StreamTypeVIDEO = 0x73646976; // 'vids'
            private const uint BI_RGB = 0;

            public SimpleAviWriter(string path, int width, int height, int frameRate)
            {
                if (width <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(width));
                }

                if (height <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(height));
                }

                if (frameRate <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(frameRate));
                }

                _width = width;
                _height = height;
                _frameSize = width * height * 4;

                AVIFileInit();

                try
                {
                    int result = AVIFileOpen(out _fileHandle, path, OF_WRITE | OF_CREATE, IntPtr.Zero);
                    if (result != 0)
                    {
                        throw new InvalidOperationException($"AVIFileOpen failed with code {result}.");
                    }

                    var info = new AVISTREAMINFO
                    {
                        fccType = StreamTypeVIDEO,
                        fccHandler = 0,
                        dwScale = 1,
                        dwRate = (uint)frameRate,
                        dwSuggestedBufferSize = (uint)_frameSize,
                        rcFrame = new RECT { Left = 0, Top = 0, Right = width, Bottom = height },
                        szName = new ushort[64]
                    };

                    result = AVIFileCreateStream(_fileHandle, out _streamHandle, ref info);
                    if (result != 0)
                    {
                        throw new InvalidOperationException($"AVIFileCreateStream failed with code {result}.");
                    }

                    var format = new BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                        biWidth = width,
                        biHeight = -height,
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = BI_RGB,
                        biSizeImage = (uint)_frameSize
                    };

                    result = AVIStreamSetFormat(_streamHandle, 0, ref format, Marshal.SizeOf<BITMAPINFOHEADER>());
                    if (result != 0)
                    {
                        throw new InvalidOperationException($"AVIStreamSetFormat failed with code {result}.");
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void WriteFrame(Bitmap frame)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(SimpleAviWriter));
                }

                if (frame.Width != _width || frame.Height != _height)
                {
                    throw new InvalidOperationException("Frame size does not match recorder dimensions.");
                }

                var rect = new Rectangle(0, 0, _width, _height);
                var data = frame.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    WriteFrameInternal(data.Scan0, data.Stride);
                }
                finally
                {
                    frame.UnlockBits(data);
                }
            }

            private void WriteFrameInternal(IntPtr buffer, int stride)
            {
                int expectedStride = _width * 4;
                IntPtr dataPointer = buffer;
                int dataSize = _frameSize;
                GCHandle handle = default;

                try
                {
                    if (stride != expectedStride)
                    {
                        EnsureCopyBuffer();
                        for (int y = 0; y < _height; y++)
                        {
                            IntPtr sourceRow = IntPtr.Add(buffer, y * stride);
                            Marshal.Copy(sourceRow, _copyBuffer!, y * expectedStride, expectedStride);
                        }

                        handle = GCHandle.Alloc(_copyBuffer!, GCHandleType.Pinned);
                        dataPointer = handle.AddrOfPinnedObject();
                        dataSize = _copyBuffer!.Length;
                    }

                    int result = AVIStreamWrite(_streamHandle, _frameIndex, 1, dataPointer, dataSize, 0, IntPtr.Zero, IntPtr.Zero);
                    if (result != 0)
                    {
                        throw new InvalidOperationException($"AVIStreamWrite failed with code {result}.");
                    }

                    _frameIndex++;
                }
                finally
                {
                    if (handle.IsAllocated)
                    {
                        handle.Free();
                    }
                }
            }

            private void EnsureCopyBuffer()
            {
                int expectedStride = _width * 4;
                if (_copyBuffer == null || _copyBuffer.Length != expectedStride * _height)
                {
                    _copyBuffer = new byte[expectedStride * _height];
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                if (_streamHandle != IntPtr.Zero)
                {
                    AVIStreamRelease(_streamHandle);
                    _streamHandle = IntPtr.Zero;
                }

                if (_fileHandle != IntPtr.Zero)
                {
                    AVIFileRelease(_fileHandle);
                    _fileHandle = IntPtr.Zero;
                }

                AVIFileExit();
                _disposed = true;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct AVISTREAMINFO
            {
                public uint fccType;
                public uint fccHandler;
                public uint dwFlags;
                public uint dwCaps;
                public ushort wPriority;
                public ushort wLanguage;
                public uint dwScale;
                public uint dwRate;
                public uint dwStart;
                public uint dwLength;
                public uint dwInitialFrames;
                public uint dwSuggestedBufferSize;
                public uint dwQuality;
                public uint dwSampleSize;
                public RECT rcFrame;
                public uint dwEditCount;
                public uint dwFormatChangeCount;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
                public ushort[] szName;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct BITMAPINFOHEADER
            {
                public uint biSize;
                public int biWidth;
                public int biHeight;
                public ushort biPlanes;
                public ushort biBitCount;
                public uint biCompression;
                public uint biSizeImage;
                public int biXPelsPerMeter;
                public int biYPelsPerMeter;
                public uint biClrUsed;
                public uint biClrImportant;
            }

            [DllImport("avifil32.dll")]
            private static extern void AVIFileInit();

            [DllImport("avifil32.dll")]
            private static extern int AVIFileOpen(out IntPtr ppfile, string szFile, int mode, IntPtr pclsidHandler);

            [DllImport("avifil32.dll")]
            private static extern int AVIFileCreateStream(IntPtr pfile, out IntPtr ppavi, ref AVISTREAMINFO psi);

            [DllImport("avifil32.dll")]
            private static extern int AVIStreamSetFormat(IntPtr pavi, int lPos, ref BITMAPINFOHEADER lpFormat, int cbFormat);

            [DllImport("avifil32.dll")]
            private static extern int AVIStreamWrite(IntPtr pavi, int lStart, int lSamples, IntPtr lpBuffer, int cbBuffer, int dwFlags, IntPtr plSampWritten, IntPtr plBytesWritten);

            [DllImport("avifil32.dll")]
            private static extern int AVIStreamRelease(IntPtr pavi);

            [DllImport("avifil32.dll")]
            private static extern int AVIFileRelease(IntPtr pfile);

            [DllImport("avifil32.dll")]
            private static extern void AVIFileExit();
        }

        private sealed class MediaFoundationFrameWriter : IFrameWriter
        {
            private static readonly Dictionary<string, FormatDescriptor> FormatMap = new Dictionary<string, FormatDescriptor>(StringComparer.OrdinalIgnoreCase)
            {
                { "mp4", new FormatDescriptor(MediaFoundationInterop.MFVideoFormat_H264, MediaFoundationInterop.MFTranscodeContainerType_MPEG4) },
                { "mov", new FormatDescriptor(MediaFoundationInterop.MFVideoFormat_H264, MediaFoundationInterop.MFTranscodeContainerType_MPEG4) },
                { "mkv", new FormatDescriptor(MediaFoundationInterop.MFVideoFormat_H264, MediaFoundationInterop.MFTranscodeContainerType_MPEG4) },
                { "flv", new FormatDescriptor(MediaFoundationInterop.MFVideoFormat_H264, MediaFoundationInterop.MFTranscodeContainerType_MPEG4) },
                { "wmv", new FormatDescriptor(MediaFoundationInterop.MFVideoFormat_WMV3, MediaFoundationInterop.MFTranscodeContainerType_ASF) },
                { "asf", new FormatDescriptor(MediaFoundationInterop.MFVideoFormat_WMV3, MediaFoundationInterop.MFTranscodeContainerType_ASF) },
                { "mpg", new FormatDescriptor(MediaFoundationInterop.MFVideoFormat_MPEG2, MediaFoundationInterop.MFTranscodeContainerType_MPEG2) },
                { "vob", new FormatDescriptor(MediaFoundationInterop.MFVideoFormat_MPEG2, MediaFoundationInterop.MFTranscodeContainerType_MPEG2) },
            };

            private readonly MediaFoundationInterop.IMFSinkWriter _sinkWriter = null!;
            private readonly int _streamIndex;
            private readonly int _width;
            private readonly int _height;
            private readonly int _targetStride;
            private readonly long _frameRate;
            private readonly long _baseFrameDuration;
            private readonly long _durationRemainder;
            private long _timestamp;
            private long _durationAccumulator;
            private bool _disposed;

            private MediaFoundationFrameWriter(string extension, string path, int width, int height, int frameRate, FormatDescriptor descriptor)
            {
                if (width <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(width));
                }

                if (height <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(height));
                }

                if (frameRate <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(frameRate));
                }

                _width = width;
                _height = height;
                _targetStride = width * 4;
                _frameRate = Math.Max(1, frameRate);
                _baseFrameDuration = 10_000_000L / _frameRate;
                _durationRemainder = 10_000_000L % _frameRate;

                MediaFoundationInterop.AddRef();

                bool initialized = false;
                MediaFoundationInterop.IMFAttributes? attributes = null;
                MediaFoundationInterop.IMFMediaType? outputType = null;
                MediaFoundationInterop.IMFMediaType? inputType = null;
                MediaFoundationInterop.IMFSinkWriter? writer = null;

                try
                {
                    if (descriptor.ContainerType.HasValue)
                    {
                        attributes = MediaFoundationInterop.CreateAttributes(1);
                        MediaFoundationInterop.CheckHr(attributes.SetGUID(MediaFoundationInterop.MF_TRANSCODE_CONTAINERTYPE, descriptor.ContainerType.Value), "IMFAttributes.SetGUID");
                    }

                    writer = MediaFoundationInterop.CreateSinkWriter(path, attributes);

                    outputType = MediaFoundationInterop.CreateMediaType();
                    MediaFoundationInterop.CheckHr(outputType.SetGUID(MediaFoundationInterop.MF_MT_MAJOR_TYPE, MediaFoundationInterop.MFMediaType_Video), "MF_MT_MAJOR_TYPE");
                    MediaFoundationInterop.CheckHr(outputType.SetGUID(MediaFoundationInterop.MF_MT_SUBTYPE, descriptor.VideoSubtype), "MF_MT_SUBTYPE");
                    MediaFoundationInterop.SetAttributeSize(outputType, MediaFoundationInterop.MF_MT_FRAME_SIZE, width, height);
                    MediaFoundationInterop.SetAttributeRatio(outputType, MediaFoundationInterop.MF_MT_FRAME_RATE, frameRate, 1);
                    MediaFoundationInterop.SetAttributeRatio(outputType, MediaFoundationInterop.MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
                    MediaFoundationInterop.CheckHr(outputType.SetUINT32(MediaFoundationInterop.MF_MT_INTERLACE_MODE, (int)MediaFoundationInterop.MFVideoInterlaceMode.Progressive), "MF_MT_INTERLACE_MODE");
                    MediaFoundationInterop.CheckHr(outputType.SetUINT32(MediaFoundationInterop.MF_MT_AVG_BITRATE, CalculateBitrate(width, height, frameRate)), "MF_MT_AVG_BITRATE");
                    MediaFoundationInterop.CheckHr(outputType.SetUINT32(MediaFoundationInterop.MF_MT_ALL_SAMPLES_INDEPENDENT, 1), "MF_MT_ALL_SAMPLES_INDEPENDENT");

                    MediaFoundationInterop.CheckHr(writer.AddStream(outputType, out _streamIndex), "IMFSinkWriter.AddStream");

                    inputType = MediaFoundationInterop.CreateMediaType();
                    MediaFoundationInterop.CheckHr(inputType.SetGUID(MediaFoundationInterop.MF_MT_MAJOR_TYPE, MediaFoundationInterop.MFMediaType_Video), "Input MF_MT_MAJOR_TYPE");
                    MediaFoundationInterop.CheckHr(inputType.SetGUID(MediaFoundationInterop.MF_MT_SUBTYPE, MediaFoundationInterop.MFVideoFormat_RGB32), "Input MF_MT_SUBTYPE");
                    MediaFoundationInterop.SetAttributeSize(inputType, MediaFoundationInterop.MF_MT_FRAME_SIZE, width, height);
                    MediaFoundationInterop.SetAttributeRatio(inputType, MediaFoundationInterop.MF_MT_FRAME_RATE, frameRate, 1);
                    MediaFoundationInterop.SetAttributeRatio(inputType, MediaFoundationInterop.MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
                    MediaFoundationInterop.CheckHr(inputType.SetUINT32(MediaFoundationInterop.MF_MT_INTERLACE_MODE, (int)MediaFoundationInterop.MFVideoInterlaceMode.Progressive), "Input MF_MT_INTERLACE_MODE");

                    MediaFoundationInterop.CheckHr(writer.SetInputMediaType(_streamIndex, inputType, null), "IMFSinkWriter.SetInputMediaType");
                    MediaFoundationInterop.CheckHr(writer.BeginWriting(), "IMFSinkWriter.BeginWriting");

                    _sinkWriter = writer;
                    writer = null;
                    initialized = true;
                }
                catch
                {
                    if (writer != null)
                    {
                        Marshal.ReleaseComObject(writer);
                    }

                    throw;
                }
                finally
                {
                    if (attributes != null)
                    {
                        Marshal.ReleaseComObject(attributes);
                    }

                    if (outputType != null)
                    {
                        Marshal.ReleaseComObject(outputType);
                    }

                    if (inputType != null)
                    {
                        Marshal.ReleaseComObject(inputType);
                    }

                    if (!initialized)
                    {
                        MediaFoundationInterop.Release();
                    }
                }
            }

            public static MediaFoundationFrameWriter Create(string extension, string path, int width, int height, int frameRate)
            {
                if (string.IsNullOrWhiteSpace(extension))
                {
                    throw new ArgumentException("Extension is required.", nameof(extension));
                }

                if (!FormatMap.TryGetValue(extension, out var descriptor))
                {
                    throw new NotSupportedException($"Recording format '{extension}' is not supported.");
                }

                return new MediaFoundationFrameWriter(extension, path, width, height, frameRate, descriptor);
            }

            public void WriteFrame(Bitmap frame)
            {
                if (frame == null)
                {
                    throw new ArgumentNullException(nameof(frame));
                }

                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(MediaFoundationFrameWriter));
                }

                if (frame.Width != _width || frame.Height != _height)
                {
                    throw new InvalidOperationException("Frame size does not match recorder dimensions.");
                }

                var rect = new Rectangle(0, 0, _width, _height);
                var data = frame.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                try
                {
                    WriteFrameInternal(data.Scan0, data.Stride);
                }
                finally
                {
                    frame.UnlockBits(data);
                }
            }

            private void WriteFrameInternal(IntPtr scan0, int stride)
            {
                MediaFoundationInterop.IMFMediaBuffer? buffer = null;
                MediaFoundationInterop.IMFSample? sample = null;
                IntPtr destination = IntPtr.Zero;

                try
                {
                    buffer = MediaFoundationInterop.CreateMemoryBuffer(_targetStride * _height);
                    MediaFoundationInterop.CheckHr(buffer.Lock(out destination, out var maxLength, out _), "IMFMediaBuffer.Lock");

                    int required = _targetStride * _height;
                    if (required > maxLength)
                    {
                        throw new InvalidOperationException("Allocated Media Foundation buffer is smaller than the frame size.");
                    }

                    CopyFrame(scan0, stride, destination, _targetStride, _height);

                    MediaFoundationInterop.CheckHr(buffer.Unlock(), "IMFMediaBuffer.Unlock");
                    destination = IntPtr.Zero;

                    MediaFoundationInterop.CheckHr(buffer.SetCurrentLength(required), "IMFMediaBuffer.SetCurrentLength");

                    sample = MediaFoundationInterop.CreateSample();
                    MediaFoundationInterop.CheckHr(sample.AddBuffer(buffer), "IMFSample.AddBuffer");

                    long duration = _baseFrameDuration;
                    if (_durationRemainder > 0)
                    {
                        _durationAccumulator += _durationRemainder;
                        if (_durationAccumulator >= _frameRate)
                        {
                            duration += 1;
                            _durationAccumulator -= _frameRate;
                        }
                    }

                    MediaFoundationInterop.CheckHr(sample.SetSampleTime(_timestamp), "IMFSample.SetSampleTime");
                    MediaFoundationInterop.CheckHr(sample.SetSampleDuration(duration), "IMFSample.SetSampleDuration");

                    _timestamp += duration;

                    MediaFoundationInterop.CheckHr(_sinkWriter.WriteSample(_streamIndex, sample), "IMFSinkWriter.WriteSample");
                }
                finally
                {
                    if (destination != IntPtr.Zero && buffer != null)
                    {
                        buffer.Unlock();
                    }

                    if (sample != null)
                    {
                        Marshal.ReleaseComObject(sample);
                    }

                    if (buffer != null)
                    {
                        Marshal.ReleaseComObject(buffer);
                    }
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                try
                {
                    if (_sinkWriter != null)
                    {
                        try
                        {
                            MediaFoundationInterop.CheckHr(_sinkWriter.Finalize(), "IMFSinkWriter.Finalize");
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(_sinkWriter);
                        }
                    }
                }
                finally
                {
                    MediaFoundationInterop.Release();
                }
            }

            private static int CalculateBitrate(int width, int height, int frameRate)
            {
                long pixelsPerSecond = (long)Math.Max(1, width) * Math.Max(1, height) * Math.Max(1, frameRate);
                long bitRate = pixelsPerSecond * 8L;
                if (bitRate < 1_000_000L)
                {
                    bitRate = 1_000_000L;
                }

                if (bitRate > int.MaxValue)
                {
                    bitRate = int.MaxValue;
                }

                return (int)bitRate;
            }

            private static unsafe void CopyFrame(IntPtr source, int sourceStride, IntPtr destination, int destinationStride, int height)
            {
                byte* src = (byte*)source.ToPointer();
                if (sourceStride < 0)
                {
                    src += (long)(height - 1) * (-sourceStride);
                    sourceStride = -sourceStride;
                }

                byte* dst = (byte*)destination.ToPointer();
                int rowLength = Math.Min(sourceStride, destinationStride);

                for (int y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(src + (long)y * sourceStride, dst + (long)y * destinationStride, destinationStride, rowLength);
                }
            }

            private readonly struct FormatDescriptor
            {
                public FormatDescriptor(Guid videoSubtype, Guid? containerType)
                {
                    VideoSubtype = videoSubtype;
                    ContainerType = containerType;
                }

                public Guid VideoSubtype { get; }

                public Guid? ContainerType { get; }
            }

            private static class MediaFoundationInterop
            {
                private const int MF_VERSION = 0x00020070;
                private const int MFSTARTUP_FULL = 0;

                private static readonly object Sync = new object();
                private static int _refCount;

                public static readonly Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
                public static readonly Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
                public static readonly Guid MFVideoFormat_WMV3 = new Guid("33564D57-0000-0010-8000-00AA00389B71");
                public static readonly Guid MFVideoFormat_MPEG2 = new Guid("E06D8026-DB46-11CF-B4D1-00805F6CBBEA");
                public static readonly Guid MFVideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
                public static readonly Guid MFTranscodeContainerType_ASF = new Guid("430F6F6E-B6BF-4FC1-A0BD-9EE46EEE2AFB");
                public static readonly Guid MFTranscodeContainerType_MPEG4 = new Guid("DC6CD05D-B9D0-40EF-BD35-FA622C1AB28A");
                public static readonly Guid MFTranscodeContainerType_MPEG2 = new Guid("BFC2DBF9-7BB4-4F8F-AFDE-E112C44BA882");
                public static readonly Guid MF_TRANSCODE_CONTAINERTYPE = new Guid("150FF23F-4ABC-478B-AC4F-E1916FBA1CCA");
                public static readonly Guid MF_MT_MAJOR_TYPE = new Guid("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
                public static readonly Guid MF_MT_SUBTYPE = new Guid("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
                public static readonly Guid MF_MT_FRAME_SIZE = new Guid("1652C33D-D6B2-4012-B834-72030849A37D");
                public static readonly Guid MF_MT_FRAME_RATE = new Guid("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
                public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new Guid("C6376A1E-8D0A-4027-BE45-6D9A0AD39BB6");
                public static readonly Guid MF_MT_INTERLACE_MODE = new Guid("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");
                public static readonly Guid MF_MT_AVG_BITRATE = new Guid("20332624-FB0D-4D9E-BD0D-CBF6786C102E");
                public static readonly Guid MF_MT_ALL_SAMPLES_INDEPENDENT = new Guid("C9173739-5E56-461C-B713-46FB995CB95F");

                public static void AddRef()
                {
                    lock (Sync)
                    {
                        if (_refCount == 0)
                        {
                            CheckHr(MFStartup(MF_VERSION, MFSTARTUP_FULL), nameof(MFStartup));
                        }

                        _refCount++;
                    }
                }

                public static void Release()
                {
                    lock (Sync)
                    {
                        if (_refCount == 0)
                        {
                            return;
                        }

                        _refCount--;
                        if (_refCount == 0)
                        {
                            MFShutdown();
                        }
                    }
                }

                public static void CheckHr(int hr, string operation)
                {
                    if (hr < 0)
                    {
                        try
                        {
                            Marshal.ThrowExceptionForHR(hr);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException($"{operation} failed with HRESULT 0x{hr:X8}.", ex);
                        }
                    }
                }

                public static IMFAttributes CreateAttributes(int size)
                {
                    CheckHr(MFCreateAttributes(out var attributes, size), nameof(MFCreateAttributes));
                    return attributes;
                }

                public static IMFMediaType CreateMediaType()
                {
                    CheckHr(MFCreateMediaType(out var type), nameof(MFCreateMediaType));
                    return type;
                }

                public static IMFSinkWriter CreateSinkWriter(string path, IMFAttributes? attributes)
                {
                    CheckHr(MFCreateSinkWriterFromURL(path, IntPtr.Zero, attributes, out var writer), nameof(MFCreateSinkWriterFromURL));
                    return writer;
                }

                public static IMFSample CreateSample()
                {
                    CheckHr(MFCreateSample(out var sample), nameof(MFCreateSample));
                    return sample;
                }

                public static IMFMediaBuffer CreateMemoryBuffer(int size)
                {
                    CheckHr(MFCreateMemoryBuffer(size, out var buffer), nameof(MFCreateMemoryBuffer));
                    return buffer;
                }

                public static void SetAttributeSize(IMFMediaType type, Guid key, int width, int height)
                {
                    CheckHr(MFSetAttributeSize(type, key, (uint)width, (uint)height), nameof(MFSetAttributeSize));
                }

                public static void SetAttributeRatio(IMFMediaType type, Guid key, int numerator, int denominator)
                {
                    CheckHr(MFSetAttributeRatio(type, key, (uint)numerator, (uint)denominator), nameof(MFSetAttributeRatio));
                }

                [DllImport("mfplat.dll")]
                private static extern int MFStartup(int version, int dwFlags);

                [DllImport("mfplat.dll")]
                private static extern int MFShutdown();

                [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
                private static extern int MFCreateSinkWriterFromURL(string? pwszOutputURL, IntPtr pUnkSink, IMFAttributes? pAttributes, out IMFSinkWriter ppSinkWriter);

                [DllImport("mfplat.dll")]
                private static extern int MFCreateMediaType(out IMFMediaType ppMFType);

                [DllImport("mfplat.dll")]
                private static extern int MFCreateAttributes(out IMFAttributes ppMFAttributes, int cInitialSize);

                [DllImport("mfplat.dll")]
                private static extern int MFSetAttributeSize(IMFAttributes pAttributes, Guid guidKey, uint unWidth, uint unHeight);

                [DllImport("mfplat.dll")]
                private static extern int MFSetAttributeRatio(IMFAttributes pAttributes, Guid guidKey, uint unNumerator, uint unDenominator);

                [DllImport("mfplat.dll")]
                private static extern int MFCreateSample(out IMFSample ppIMFSample);

                [DllImport("mfplat.dll")]
                private static extern int MFCreateMemoryBuffer(int cbMaxLength, out IMFMediaBuffer ppBuffer);

                [ComImport]
                [Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
                [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IMFAttributes
                {
                    [PreserveSig] int GetItem([In] ref Guid guidKey, IntPtr pValue);
                    [PreserveSig] int GetItemType([In] ref Guid guidKey, out MF_ATTRIBUTE_TYPE pType);
                    [PreserveSig] int CompareItem([In] ref Guid guidKey, [In] ref PropVariant value, out bool result);
                    [PreserveSig] int Compare(IMFAttributes pTheirs, MF_ATTRIBUTES_MATCH_TYPE matchType, out bool result);
                    [PreserveSig] int GetUINT32([In] ref Guid guidKey, out int value);
                    [PreserveSig] int GetUINT64([In] ref Guid guidKey, out long value);
                    [PreserveSig] int GetDouble([In] ref Guid guidKey, out double value);
                    [PreserveSig] int GetGUID([In] ref Guid guidKey, out Guid value);
                    [PreserveSig] int GetStringLength([In] ref Guid guidKey, out int length);
                    [PreserveSig] int GetString([In] ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int size, out int length);
                    [PreserveSig] int GetAllocatedString([In] ref Guid guidKey, out IntPtr value, out int length);
                    [PreserveSig] int GetBlobSize([In] ref Guid guidKey, out int size);
                    [PreserveSig] int GetBlob([In] ref Guid guidKey, [Out] byte[] buffer, int bufferSize, out int size);
                    [PreserveSig] int GetAllocatedBlob([In] ref Guid guidKey, out IntPtr buffer, out int size);
                    [PreserveSig] int GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, out IntPtr ppv);
                    [PreserveSig] int SetItem([In] ref Guid guidKey, [In] ref PropVariant value);
                    [PreserveSig] int DeleteItem([In] ref Guid guidKey);
                    [PreserveSig] int DeleteAllItems();
                    [PreserveSig] int SetUINT32([In] ref Guid guidKey, int value);
                    [PreserveSig] int SetUINT64([In] ref Guid guidKey, long value);
                    [PreserveSig] int SetDouble([In] ref Guid guidKey, double value);
                    [PreserveSig] int SetGUID([In] ref Guid guidKey, [In] ref Guid value);
                    [PreserveSig] int SetString([In] ref Guid guidKey, [In, MarshalAs(UnmanagedType.LPWStr)] string value);
                    [PreserveSig] int SetBlob([In] ref Guid guidKey, [In] byte[] buffer, int size);
                    [PreserveSig] int SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object value);
                    [PreserveSig] int LockStore();
                    [PreserveSig] int UnlockStore();
                    [PreserveSig] int GetCount(out int count);
                    [PreserveSig] int GetItemByIndex(int index, out Guid guidKey, IntPtr value);
                    [PreserveSig] int CopyAllItems(IMFAttributes destination);
                }

                [ComImport]
                [Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
                [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IMFMediaType : IMFAttributes
                {
                    [PreserveSig] int GetMajorType(out Guid guid);
                    [PreserveSig] int IsCompressedFormat([MarshalAs(UnmanagedType.Bool)] out bool compressed);
                    [PreserveSig] int IsEqual(IMFMediaType type, out MF_MEDIATYPE_EQUAL matchFlags);
                    [PreserveSig] int GetRepresentation(Guid guid, out IntPtr representation);
                    [PreserveSig] int FreeRepresentation(Guid guid, IntPtr representation);
                }

                [ComImport]
                [Guid("ad4c1b00-4bf7-422f-9175-756693d9130d")]
                [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IMFSinkWriter
                {
                    [PreserveSig] int AddStream(IMFMediaType targetMediaType, out int streamIndex);
                    [PreserveSig] int SetInputMediaType(int streamIndex, IMFMediaType inputMediaType, IMFAttributes? encodingParameters);
                    [PreserveSig] int BeginWriting();
                    [PreserveSig] int WriteSample(int streamIndex, IMFSample sample);
                    [PreserveSig] int SendStreamTick(int streamIndex, long timestamp);
                    [PreserveSig] int PlaceMarker(int streamIndex, IntPtr context);
                    [PreserveSig] int NotifyEndOfSegment(int streamIndex);
                    [PreserveSig] int Flush(int streamIndex);
                    [PreserveSig] int Finalize();
                    [PreserveSig] int GetServiceForStream(int streamIndex, ref Guid guidService, ref Guid riid, out IntPtr service);
                    [PreserveSig] int GetStatistics(int streamIndex, out MF_SINK_WRITER_STATISTICS statistics);
                }

                [ComImport]
                [Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
                [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IMFSample : IMFAttributes
                {
                    [PreserveSig] int GetSampleFlags(out int sampleFlags);
                    [PreserveSig] int SetSampleFlags(int sampleFlags);
                    [PreserveSig] int GetSampleTime(out long sampleTime);
                    [PreserveSig] int SetSampleTime(long sampleTime);
                    [PreserveSig] int GetSampleDuration(out long sampleDuration);
                    [PreserveSig] int SetSampleDuration(long sampleDuration);
                    [PreserveSig] int GetBufferCount(out int count);
                    [PreserveSig] int GetBufferByIndex(int index, out IMFMediaBuffer buffer);
                    [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
                    [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
                    [PreserveSig] int RemoveBufferByIndex(int index);
                    [PreserveSig] int RemoveAllBuffers();
                    [PreserveSig] int GetTotalLength(out int totalLength);
                    [PreserveSig] int CopyToBuffer(IMFMediaBuffer buffer);
                }

                [ComImport]
                [Guid("045fa593-8799-42b8-9737-8464f7cbfc8d")]
                [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
                public interface IMFMediaBuffer
                {
                    [PreserveSig] int Lock(out IntPtr buffer, out int maxLength, out int currentLength);
                    [PreserveSig] int Unlock();
                    [PreserveSig] int GetCurrentLength(out int length);
                    [PreserveSig] int SetCurrentLength(int length);
                    [PreserveSig] int GetMaxLength(out int maxLength);
                }

                [StructLayout(LayoutKind.Sequential)]
                public struct MF_SINK_WRITER_STATISTICS
                {
                    public int cb;
                    public long llLastTimestampReceived;
                    public long llLastTimestampEncoded;
                    public long llLastTimestampProcessed;
                    public long llLastStreamTickReceived;
                    public long llLastSinkSampleTimestamp;
                    public long llLastSinkSampleDuration;
                    public int dwNumSamplesReceived;
                    public int dwNumSamplesEncoded;
                    public int dwNumSamplesProcessed;
                    public int dwNumStreamTicksReceived;
                }

                public enum MFVideoInterlaceMode
                {
                    Progressive = 2,
                }

                public enum MF_ATTRIBUTE_TYPE
                {
                    MF_ATTRIBUTE_UINT32 = 19,
                    MF_ATTRIBUTE_UINT64 = 21,
                    MF_ATTRIBUTE_DOUBLE = 5,
                    MF_ATTRIBUTE_GUID = 72,
                    MF_ATTRIBUTE_STRING = 31,
                    MF_ATTRIBUTE_BLOB = 4113,
                    MF_ATTRIBUTE_IUNKNOWN = 13,
                }

                public enum MF_ATTRIBUTES_MATCH_TYPE
                {
                    OurItems = 0,
                    TheirItems = 1,
                    AllItems = 2,
                    Intersection = 3,
                }

                [Flags]
                public enum MF_MEDIATYPE_EQUAL
                {
                    None = 0,
                    MajorTypes = 0x1,
                    FormatTypes = 0x2,
                    AllFields = 0x4,
                }

                [StructLayout(LayoutKind.Explicit)]
                public struct PropVariant
                {
                    [FieldOffset(0)] public ushort vt;
                    [FieldOffset(8)] public IntPtr pointerValue;
                    [FieldOffset(8)] public int intValue;
                    [FieldOffset(8)] public long longValue;
                }
            }
        }

        private sealed class GifFrameWriter : IFrameWriter
        {
            private readonly GifBitmapEncoder _encoder = new GifBitmapEncoder();
            private readonly string _path;
            private readonly object _sync = new object();
            private readonly ushort _frameDelay;
            private readonly int _width;
            private readonly int _height;
            private bool _disposed;

            public GifFrameWriter(string path, int width, int height, int frameRate)
            {
                if (width <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(width));
                }

                if (height <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(height));
                }

                if (frameRate <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(frameRate));
                }

                _path = path;
                _width = width;
                _height = height;
                _frameDelay = (ushort)Math.Max(1, Math.Round(100.0 / Math.Max(1, frameRate)));
            }

            public void WriteFrame(Bitmap frame)
            {
                lock (_sync)
                {
                    if (_disposed)
                    {
                        throw new ObjectDisposedException(nameof(GifFrameWriter));
                    }

                    if (frame.Width != _width || frame.Height != _height)
                    {
                        throw new InvalidOperationException("Frame size does not match recorder dimensions.");
                    }

                    using var clone = (Bitmap)frame.Clone();
                    var bitmapFrame = CreateBitmapFrame(clone, _frameDelay);
                    _encoder.Frames.Add(bitmapFrame);
                }
            }

            public void Dispose()
            {
                lock (_sync)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    if (_encoder.Frames.Count == 0)
                    {
                        using var fallback = new Bitmap(_width, _height);
                        using (var graphics = Graphics.FromImage(fallback))
                        {
                            graphics.Clear(Color.Black);
                        }

                        var fallbackFrame = CreateBitmapFrame(fallback, _frameDelay);
                        _encoder.Frames.Add(fallbackFrame);
                    }

                    using (var stream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        _encoder.Save(stream);
                    }

                    _disposed = true;
                }
            }

            private static BitmapFrame CreateBitmapFrame(Bitmap bitmap, ushort frameDelay)
            {
                var source = CreateBitmapSource(bitmap);
                var metadata = new BitmapMetadata("gif");
                metadata.SetQuery("/grctlext/Delay", frameDelay);
                metadata.SetQuery("/grctlext/Disposal", (byte)2);
                metadata.SetQuery("/imgdesc/Left", (ushort)0);
                metadata.SetQuery("/imgdesc/Top", (ushort)0);
                metadata.SetQuery("/imgdesc/Width", (ushort)bitmap.Width);
                metadata.SetQuery("/imgdesc/Height", (ushort)bitmap.Height);
                return BitmapFrame.Create(source, null, metadata, null);
            }

            private static BitmapSource CreateBitmapSource(Bitmap bitmap)
            {
                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }

            [DllImport("gdi32.dll")]
            private static extern bool DeleteObject(IntPtr hObject);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
