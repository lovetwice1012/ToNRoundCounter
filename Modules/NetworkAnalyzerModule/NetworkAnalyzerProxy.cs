using System;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog.Events;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;
using Titanium.Web.Proxy.StreamExtended.Network;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Modules.NetworkAnalyzer
{
    internal sealed class NetworkAnalyzerProxy : IDisposable
    {
        private readonly ProxyServer _proxyServer;
        private readonly IEventLogger _logger;
        private readonly object _sync = new object();
        private ExplicitProxyEndPoint? _explicitEndPoint;
        private TextWriter? _logWriter;
        private string? _logFilePath;
        private bool _disposed;
        private bool _isRunning;
        private int _currentPort;
        private readonly string _logDirectory;

        // Async logging pipeline. Producer threads (proxy callbacks) only enqueue strings via
        // the channel's writer; a single background task drains the channel and writes them to
        // the log file. This removes per-frame/per-header lock contention and offloads
        // synchronous file I/O from the high-throughput network event handlers.
        private Channel<string>? _logChannel;
        private Task? _logWriterTask;
        private CancellationTokenSource? _logWriterCts;

        public NetworkAnalyzerProxy(IEventLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _proxyServer = new ProxyServer();
            _proxyServer.ExceptionFunc = exception =>
            {
                _logger.LogEvent("NetworkAnalyzer.Proxy", $"Proxy exception: {exception}", LogEventLevel.Error);
            };

            _proxyServer.BeforeRequest += OnBeforeRequest;
            _proxyServer.BeforeResponse += OnBeforeResponse;
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(baseDirectory))
            {
                baseDirectory = AppContext.BaseDirectory;
            }

            _logDirectory = Path.Combine(baseDirectory ?? string.Empty, "logs", "network");
            Directory.CreateDirectory(_logDirectory);
        }

        public event Func<SessionEventArgs, Task>? RequestManipulation;

        public event Func<SessionEventArgs, Task>? ResponseManipulation;

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    return _isRunning;
                }
            }
        }

        public int CurrentPort
        {
            get
            {
                lock (_sync)
                {
                    return _currentPort;
                }
            }
        }

        public string LogDirectory => _logDirectory;

        public string? CurrentLogFilePath
        {
            get
            {
                lock (_sync)
                {
                    return _logFilePath;
                }
            }
        }

        public Task<bool> EnsureRunningAsync(IAppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            return Task.Run(() =>
            {
                lock (_sync)
                {
                    ThrowIfDisposed();

                    int normalizedPort = NormalizePort(settings.NetworkAnalyzerProxyPort);
                    if (_isRunning && _currentPort == normalizedPort)
                    {
                        return true;
                    }

                    try
                    {
                        StopInternal();
                        EnsureRootCertificate();
                        StartInternal(normalizedPort);
                        _logger.LogEvent("NetworkAnalyzer", $"Network analyzer proxy is listening on 127.0.0.1:{normalizedPort}. Logs are written to {_logFilePath}.");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogEvent("NetworkAnalyzer", $"Failed to start proxy: {ex}", LogEventLevel.Error);
                        StopInternal();
                        return false;
                    }
                }
            });
        }

        public void Stop()
        {
            lock (_sync)
            {
                StopInternal();
            }
        }

        private void EnsureRootCertificate()
        {
            try
            {
                var certificateManager = _proxyServer.CertificateManager;
                var rootCertificate = certificateManager.RootCertificate;

                if (rootCertificate == null)
                {
                    rootCertificate = certificateManager.LoadRootCertificate();
                    if (rootCertificate != null)
                    {
                        certificateManager.RootCertificate = rootCertificate;
                    }
                    else
                    {
                        certificateManager.CreateRootCertificate(true);
                        _logger.LogEvent("NetworkAnalyzer", "Created a dedicated root certificate for the network analyzer proxy.", LogEventLevel.Warning);
                    }
                }

                certificateManager.EnsureRootCertificate(true, true);
                _logger.LogEvent("NetworkAnalyzer", "Attempted to install the network analyzer root certificate.", LogEventLevel.Warning);
            }
            catch (Exception ex)
            {
                _logger.LogEvent("NetworkAnalyzer", $"Failed to prepare root certificate: {ex}", LogEventLevel.Error);
                throw;
            }
        }

        private void StartInternal(int port)
        {
            _explicitEndPoint = new ExplicitProxyEndPoint(IPAddress.Loopback, port, true);
            _explicitEndPoint.BeforeTunnelConnectRequest += OnBeforeTunnelConnect;

            _proxyServer.AddEndPoint(_explicitEndPoint);
            _proxyServer.Start();

            _logFilePath = Path.Combine(_logDirectory, $"network-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");
            // No need for TextWriter.Synchronized: only the background writer task touches _logWriter.
            _logWriter = new StreamWriter(new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
            {
                AutoFlush = false
            };

            // Bounded channel with DropOldest avoids unbounded memory growth if the writer falls
            // behind during traffic spikes; this matches the behaviour we want (drop the oldest
            // diagnostic line rather than back-pressure the proxy callback chain).
            _logChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(8192)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
            _logWriterCts = new CancellationTokenSource();
            var channel = _logChannel;
            var writer = _logWriter;
            var token = _logWriterCts.Token;
            _logWriterTask = Task.Run(() => RunLogWriterAsync(channel, writer, token));

            _currentPort = port;
            _isRunning = true;
        }

        private void StopInternal()
        {
            if (_explicitEndPoint != null)
            {
                _explicitEndPoint.BeforeTunnelConnectRequest -= OnBeforeTunnelConnect;
                try
                {
                    _proxyServer.RemoveEndPoint(_explicitEndPoint);
                }
                catch
                {
                    // Ignore removal errors during shutdown.
                }

                _explicitEndPoint = null;
            }

            if (_proxyServer.ProxyRunning)
            {
                try
                {
                    _proxyServer.Stop();
                }
                catch
                {
                    // Suppress shutdown exceptions.
                }
            }

            if (_logWriter != null)
            {
                try
                {
                    if (_logChannel != null)
                    {
                        _logChannel.Writer.TryComplete();
                    }
                    if (_logWriterTask != null)
                    {
                        // Give the writer a brief grace period to drain queued lines.
                        if (!_logWriterTask.Wait(TimeSpan.FromSeconds(2)))
                        {
                            _logWriterCts?.Cancel();
                            try { _logWriterTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
                        }
                    }
                }
                catch
                {
                    // Ignore drain errors during shutdown.
                }

                try
                {
                    _logWriter.Dispose();
                }
                catch
                {
                    // Ignore flushing exceptions.
                }
                _logWriter = null;
            }

            _logChannel = null;
            _logWriterTask = null;
            try { _logWriterCts?.Dispose(); } catch { }
            _logWriterCts = null;

            _logFilePath = null;
            _currentPort = 0;
            _isRunning = false;
        }

        private async Task OnBeforeRequest(object sender, SessionEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            try
            {
                var request = e.WebSession.Request;
                LogLine($"[REQUEST] {request.Method} {request.Url}");
                foreach (var header in request.Headers)
                {
                    LogLine($"  > {header.Name}: {header.Value}");
                }

                if (request.HasBody)
                {
                    if (IsTextContent(request.ContentType))
                    {
                        var body = await e.GetRequestBodyAsString().ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(body))
                        {
                            LogMultiline("  >> ", body);
                        }
                    }
                    else
                    {
                        var buffer = await e.GetRequestBody().ConfigureAwait(false);
                        if (buffer != null && buffer.Length > 0)
                        {
                            LogLine($"  > [body length: {buffer.Length} bytes]");
                        }
                    }
                }

                if (RequestManipulation != null)
                {
                    await RequestManipulation.Invoke(e).ConfigureAwait(false);
                }

                if (request.UpgradeToWebSocket)
                {
                    AttachWebSocketListeners(e);
                }
            }
            catch (Exception ex)
            {
                _logger.LogEvent("NetworkAnalyzer", $"Error while capturing request: {ex}", LogEventLevel.Error);
            }
        }

        private async Task OnBeforeResponse(object sender, SessionEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            try
            {
                var response = e.WebSession.Response;
                LogLine($"[RESPONSE] {response.HttpVersion} {(int)response.StatusCode} {response.StatusDescription} for {e.WebSession.Request?.Url}");
                foreach (var header in response.Headers)
                {
                    LogLine($"  < {header.Name}: {header.Value}");
                }

                if (response.HasBody)
                {
                    if (IsTextContent(response.ContentType))
                    {
                        var body = await e.GetResponseBodyAsString().ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(body))
                        {
                            LogMultiline("  << ", body);
                        }
                    }
                    else
                    {
                        var buffer = await e.GetResponseBody().ConfigureAwait(false);
                        if (buffer != null && buffer.Length > 0)
                        {
                            LogLine($"  < [body length: {buffer.Length} bytes]");
                        }
                    }
                }

                if (ResponseManipulation != null)
                {
                    await ResponseManipulation.Invoke(e).ConfigureAwait(false);
                }

                if (response.StatusCode == (int)HttpStatusCode.SwitchingProtocols &&
                    e.HttpClient.Request.UpgradeToWebSocket)
                {
                    var state = GetOrCreateWebSocketState(e);
                    if (!state.OpenLogged)
                    {
                        LogLine($"[WEBSOCKET] Opened {e.HttpClient.Request.Url}");
                        state.OpenLogged = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogEvent("NetworkAnalyzer", $"Error while capturing response: {ex}", LogEventLevel.Error);
            }
        }

        private void AttachWebSocketListeners(SessionEventArgs session)
        {
            var state = GetOrCreateWebSocketState(session);
            if (state.ListenersAttached)
            {
                return;
            }

            session.DataReceived += OnWebSocketDataReceived;
            session.DataSent += OnWebSocketDataSent;
            state.ListenersAttached = true;
        }

        private void OnWebSocketDataSent(object? sender, DataEventArgs e)
        {
            if (sender is SessionEventArgs session)
            {
                HandleWebSocketFrames(session, e, false);
            }
        }

        private void OnWebSocketDataReceived(object? sender, DataEventArgs e)
        {
            if (sender is SessionEventArgs session)
            {
                HandleWebSocketFrames(session, e, true);
            }
        }

        private void HandleWebSocketFrames(SessionEventArgs session, DataEventArgs e, bool isIncoming)
        {
            try
            {
                var state = GetOrCreateWebSocketState(session);
                var decoder = isIncoming ? session.WebSocketDecoderReceive : session.WebSocketDecoderSend;
                var direction = isIncoming ? "<=" : "=>";

                foreach (var frame in decoder.Decode(e.Buffer, e.Offset, e.Count))
                {
                    switch (frame.OpCode)
                    {
                        case WebsocketOpCode.Text:
                            HandleTextFrame(session, state, frame, direction, isIncoming);
                            break;
                        case WebsocketOpCode.Binary:
                            HandleBinaryFrame(session, state, frame, direction, isIncoming);
                            break;
                        case WebsocketOpCode.Continuation:
                            HandleContinuationFrame(session, state, frame, direction, isIncoming);
                            break;
                        case WebsocketOpCode.ConnectionClose:
                            HandleCloseFrame(session, state);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogEvent("NetworkAnalyzer", $"Failed to process WebSocket frame: {ex}", LogEventLevel.Error);
            }
        }

        private Task OnBeforeTunnelConnect(object sender, TunnelConnectSessionEventArgs e)
        {
            LogLine($"[TUNNEL] {e.WebSession.Request.Host}");
            return Task.CompletedTask;
        }

        private void HandleTextFrame(SessionEventArgs session, WebSocketSessionState state, WebSocketFrame frame,
            string direction, bool isIncoming)
        {
            var message = frame.GetText();
            var accumulator = isIncoming ? state.IncomingTextBuilder : state.OutgoingTextBuilder;

            if (frame.IsFinal)
            {
                if (accumulator != null)
                {
                    accumulator.Append(message);
                    message = accumulator.ToString();
                    if (isIncoming)
                    {
                        state.IncomingTextBuilder = null;
                    }
                    else
                    {
                        state.OutgoingTextBuilder = null;
                    }
                }

                LogLine($"[WEBSOCKET][TEXT] {session.HttpClient.Request.Url} {direction} {message}");
                ClearContinuationState(state, isIncoming);
            }
            else
            {
                accumulator ??= new StringBuilder();
                accumulator.Append(message);
                if (isIncoming)
                {
                    state.IncomingTextBuilder = accumulator;
                    state.IncomingContinuationType = WebsocketOpCode.Text;
                }
                else
                {
                    state.OutgoingTextBuilder = accumulator;
                    state.OutgoingContinuationType = WebsocketOpCode.Text;
                }
            }
        }

        private void HandleBinaryFrame(SessionEventArgs session, WebSocketSessionState state, WebSocketFrame frame,
            string direction, bool isIncoming)
        {
            if (frame.IsFinal)
            {
                var totalLength = frame.Data.Length + GetAccumulatedBinaryLength(state, isIncoming, reset: true);
                LogLine($"[WEBSOCKET][BINARY] {session.HttpClient.Request.Url} {direction} {totalLength} bytes");
                ClearContinuationState(state, isIncoming);
            }
            else
            {
                SetAccumulatedBinaryLength(state, isIncoming, frame.Data.Length);
                SetContinuationType(state, isIncoming, WebsocketOpCode.Binary);
            }
        }

        private void HandleContinuationFrame(SessionEventArgs session, WebSocketSessionState state, WebSocketFrame frame,
            string direction, bool isIncoming)
        {
            var continuationType = GetContinuationType(state, isIncoming);
            if (continuationType == WebsocketOpCode.Binary)
            {
                var accumulated = AddAccumulatedBinaryLength(state, isIncoming, frame.Data.Length);
                if (frame.IsFinal)
                {
                    LogLine($"[WEBSOCKET][BINARY] {session.HttpClient.Request.Url} {direction} {accumulated} bytes");
                    ClearContinuationState(state, isIncoming);
                }
            }
            else
            {
                var accumulator = isIncoming ? state.IncomingTextBuilder : state.OutgoingTextBuilder;
                accumulator ??= new StringBuilder();
                accumulator.Append(frame.GetText());
                if (frame.IsFinal)
                {
                    LogLine($"[WEBSOCKET][TEXT] {session.HttpClient.Request.Url} {direction} {accumulator}");
                    if (isIncoming)
                    {
                        state.IncomingTextBuilder = null;
                    }
                    else
                    {
                        state.OutgoingTextBuilder = null;
                    }
                    ClearContinuationState(state, isIncoming);
                }
                else
                {
                    if (isIncoming)
                    {
                        state.IncomingTextBuilder = accumulator;
                        state.IncomingContinuationType = WebsocketOpCode.Text;
                    }
                    else
                    {
                        state.OutgoingTextBuilder = accumulator;
                        state.OutgoingContinuationType = WebsocketOpCode.Text;
                    }
                }
            }
        }

        private void HandleCloseFrame(SessionEventArgs session, WebSocketSessionState state)
        {
            if (!state.ClosedLogged)
            {
                LogLine($"[WEBSOCKET] Closed {session.HttpClient.Request.Url}");
                state.ClosedLogged = true;

                if (state.ListenersAttached)
                {
                    session.DataReceived -= OnWebSocketDataReceived;
                    session.DataSent -= OnWebSocketDataSent;
                    state.ListenersAttached = false;
                }
            }
        }

        private static WebSocketSessionState GetOrCreateWebSocketState(SessionEventArgs session)
        {
            return WebSocketStates.GetValue(session, _ => new WebSocketSessionState());
        }

        private static WebsocketOpCode? GetContinuationType(WebSocketSessionState state, bool isIncoming)
        {
            return isIncoming ? state.IncomingContinuationType : state.OutgoingContinuationType;
        }

        private static void SetContinuationType(WebSocketSessionState state, bool isIncoming, WebsocketOpCode opCode)
        {
            if (isIncoming)
            {
                state.IncomingContinuationType = opCode;
            }
            else
            {
                state.OutgoingContinuationType = opCode;
            }
        }

        private static void ClearContinuationState(WebSocketSessionState state, bool isIncoming)
        {
            if (isIncoming)
            {
                state.IncomingContinuationType = null;
                state.IncomingBinaryLength = 0;
                state.IncomingTextBuilder = null;
            }
            else
            {
                state.OutgoingContinuationType = null;
                state.OutgoingBinaryLength = 0;
                state.OutgoingTextBuilder = null;
            }
        }

        private static int GetAccumulatedBinaryLength(WebSocketSessionState state, bool isIncoming, bool reset)
        {
            var length = isIncoming ? state.IncomingBinaryLength : state.OutgoingBinaryLength;
            if (reset)
            {
                if (isIncoming)
                {
                    state.IncomingBinaryLength = 0;
                }
                else
                {
                    state.OutgoingBinaryLength = 0;
                }
            }

            return length;
        }

        private static void SetAccumulatedBinaryLength(WebSocketSessionState state, bool isIncoming, int length)
        {
            if (isIncoming)
            {
                state.IncomingBinaryLength = length;
            }
            else
            {
                state.OutgoingBinaryLength = length;
            }
        }

        private static int AddAccumulatedBinaryLength(WebSocketSessionState state, bool isIncoming, int length)
        {
            if (isIncoming)
            {
                state.IncomingBinaryLength += length;
                return state.IncomingBinaryLength;
            }

            state.OutgoingBinaryLength += length;
            return state.OutgoingBinaryLength;
        }

        private static readonly ConditionalWeakTable<SessionEventArgs, WebSocketSessionState> WebSocketStates = new();

        private sealed class WebSocketSessionState
        {
            public bool ListenersAttached;
            public bool OpenLogged;
            public bool ClosedLogged;
            public StringBuilder? IncomingTextBuilder;
            public StringBuilder? OutgoingTextBuilder;
            public int IncomingBinaryLength;
            public int OutgoingBinaryLength;
            public WebsocketOpCode? IncomingContinuationType;
            public WebsocketOpCode? OutgoingContinuationType;
        }

        private void LogLine(string message)
        {
            // Snapshot the writer reference under lock-free read; channel is created/cleared
            // only inside StartInternal/StopInternal which run on the lock-protected start/stop path.
            var channel = Volatile.Read(ref _logChannel);
            if (channel == null)
            {
                return;
            }

            // Pre-format with timestamp on the calling thread so the writer task only does I/O.
            var line = string.Concat(DateTimeOffset.Now.ToString("O"), " ", message);
            channel.Writer.TryWrite(line);
        }

        private void LogMultiline(string prefix, string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return;
            }

            var channel = Volatile.Read(ref _logChannel);
            if (channel == null)
            {
                return;
            }

            string timestamp = DateTimeOffset.Now.ToString("O");
            using var reader = new StringReader(content);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                channel.Writer.TryWrite(string.Concat(timestamp, " ", prefix, line));
            }
        }

        private async Task RunLogWriterAsync(Channel<string> channel, TextWriter writer, CancellationToken token)
        {
            try
            {
                var reader = channel.Reader;
                int sinceLastFlush = 0;
                while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var line))
                    {
                        try
                        {
                            writer.WriteLine(line);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogEvent("NetworkAnalyzer", $"Failed to write to log file: {ex}", LogEventLevel.Error);
                        }

                        if (++sinceLastFlush >= 64)
                        {
                            try { writer.Flush(); } catch { }
                            sinceLastFlush = 0;
                        }
                    }

                    if (sinceLastFlush > 0)
                    {
                        try { writer.Flush(); } catch { }
                        sinceLastFlush = 0;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogEvent("NetworkAnalyzer", $"Log writer task failed: {ex}", LogEventLevel.Error);
            }
            finally
            {
                try { writer.Flush(); } catch { }
            }
        }

        private static int NormalizePort(int port)
        {
            if (port < 1025 || port > 65535)
            {
                return 8890;
            }

            return port;
        }

        private static bool IsTextContent(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
            {
                return false;
            }

            var value = contentType.Trim();
            return value.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                || value.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("javascript", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("form-urlencoded", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NetworkAnalyzerProxy));
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

                StopInternal();
                _proxyServer.BeforeRequest -= OnBeforeRequest;
                _proxyServer.BeforeResponse -= OnBeforeResponse;

                _proxyServer.Dispose();
                _disposed = true;
            }
        }
    }
}
