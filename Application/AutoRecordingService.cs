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

        private static IEnumerable<string> SplitTerrorNames(string terrorKey)
        {
            if (string.IsNullOrWhiteSpace(terrorKey))
            {
                yield break;
            }

            foreach (var part in terrorKey.Split(new[] { '&', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
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

        internal static readonly string[] SupportedExtensions = new[] { "avi", "gif" };

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
            while (sanitized.Contains("__", StringComparison.Ordinal))
            {
                sanitized = sanitized.Replace("__", "_", StringComparison.Ordinal);
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
                    default:
                        return new SimpleAviWriter(outputPath, width, height, frameRate);
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
                var size = _bounds.Size;
                using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);

                while (!token.IsCancellationRequested)
                {
                    if (!IsWindow(_windowHandle))
                    {
                        SetStopReason("Target window is no longer available.", true);
                        break;
                    }

                    if (!GetWindowRect(_windowHandle, out var rect))
                    {
                        SetStopReason("Failed to retrieve window bounds.", true);
                        break;
                    }

                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;
                    if (width != size.Width || height != size.Height)
                    {
                        SetStopReason("Target window size changed during recording.", true);
                        break;
                    }

                    var origin = new Point(rect.Left, rect.Top);

                    try
                    {
                        using (var graphics = Graphics.FromImage(bitmap))
                        {
                            graphics.CopyFromScreen(origin, Point.Empty, size, CopyPixelOperation.SourceCopy);
                        }

                        _writer.WriteFrame(bitmap);
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
                IntPtr found = IntPtr.Zero;
                EnumWindows((handle, _) =>
                {
                    if (!IsWindowVisible(handle))
                    {
                        return true;
                    }

                    int length = GetWindowTextLength(handle);
                    if (length <= 0)
                    {
                        return true;
                    }

                    var builder = new StringBuilder(length + 1);
                    if (GetWindowText(handle, builder, builder.Capacity) == 0)
                    {
                        return true;
                    }

                    var title = builder.ToString();
                    if (title.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found = handle;
                        return false;
                    }

                    return true;
                }, IntPtr.Zero);

                if (found != IntPtr.Zero)
                {
                    return found;
                }

                foreach (var process in Process.GetProcesses())
                {
                    using (process)
                    {
                        try
                        {
                            if (process.MainWindowHandle == IntPtr.Zero)
                            {
                                continue;
                            }

                            string title = process.MainWindowTitle ?? string.Empty;
                            if (title.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                process.ProcessName.Equals(hint, StringComparison.OrdinalIgnoreCase))
                            {
                                return process.MainWindowHandle;
                            }
                        }
                        catch
                        {
                        }
                    }
                }

                return IntPtr.Zero;
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
