using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Serilog;
using ToNRoundCounter.Application;

namespace ToNRoundCounter.Infrastructure
{
    /// <summary>
    /// Represents a player state information
    /// </summary>
    public class PlayerStateInfo
    {
        public string player_id { get; set; } = string.Empty;
        public string player_name { get; set; } = string.Empty;
        public double damage { get; set; }
        public List<string> items { get; set; } = new List<string>();
        
        // Backing field for deserialized current_item from server
        private string _current_item = string.Empty;

        private static string NormalizeItemText(string? value)
        {
            string text = (value ?? string.Empty).Trim();
            if (string.Equals(text, "None", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return text;
        }
        
        // Prefer explicit current_item from server, then fall back to the latest non-empty item entry.
        public string current_item
        {
            get
            {
                var fromCurrentItem = NormalizeItemText(_current_item);
                if (!string.IsNullOrEmpty(fromCurrentItem))
                {
                    return fromCurrentItem;
                }

                if (items != null)
                {
                    for (int i = items.Count - 1; i >= 0; i--)
                    {
                        var candidate = NormalizeItemText(items[i]);
                        if (!string.IsNullOrEmpty(candidate))
                        {
                            return candidate;
                        }
                    }
                }

                return string.Empty;
            }
            set => _current_item = NormalizeItemText(value);
        }
        
        public bool is_alive { get; set; } = true;
        
        // Support deserialization from server which sends is_dead instead of is_alive
        public bool is_dead
        {
            get => !is_alive;
            set => is_alive = !value;
        }
        
        public double velocity { get; set; }
        public double afk_duration { get; set; }
    }

    /// <summary>
    /// Represents a desire player information
    /// </summary>
    public class DesirePlayerInfo
    {
        public string player_id { get; set; } = string.Empty;
        public string player_name { get; set; } = string.Empty;
    }

    public class CoordinatedAutoSuicideEntryInfo
    {
        public string id { get; set; } = string.Empty;
        public string terror_name { get; set; } = string.Empty;
        public string round_key { get; set; } = string.Empty;
        public string source { get; set; } = string.Empty;
        public string? created_at { get; set; }
        public string? created_by { get; set; }
    }

    public class CoordinatedAutoSuicidePresetEntryInfo
    {
        public string terror_name { get; set; } = string.Empty;
        public string round_key { get; set; } = string.Empty;
    }

    public class CoordinatedAutoSuicidePresetInfo
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public List<CoordinatedAutoSuicidePresetEntryInfo> entries { get; set; } = new List<CoordinatedAutoSuicidePresetEntryInfo>();
        public string? created_at { get; set; }
        public string? created_by { get; set; }
    }

    public class CoordinatedAutoSuicideStateInfo
    {
        public List<CoordinatedAutoSuicideEntryInfo> entries { get; set; } = new List<CoordinatedAutoSuicideEntryInfo>();
        public List<CoordinatedAutoSuicidePresetInfo> presets { get; set; } = new List<CoordinatedAutoSuicidePresetInfo>();
        public bool skip_all_without_survival_wish { get; set; }
        public string? updated_at { get; set; }
        public string? updated_by { get; set; }
    }

    /// <summary>
    /// Represents a request/response message envelope using the new API specification.
    /// </summary>
    public class CloudMessage
    {
        [System.Text.Json.Serialization.JsonPropertyName("version")]
        public string? Version { get; set; } = "1.0";
        
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [System.Text.Json.Serialization.JsonPropertyName("type")]
        public string Type { get; set; } = "request"; // "request", "response", "stream", "error"
        
        [System.Text.Json.Serialization.JsonPropertyName("rpc")]
        public string? Method { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("stream")]
        public string? Event { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("params")]
        public object? Params { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("result")]
        public object? Result { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string? Status { get; set; } // "success", "error"
        
        [System.Text.Json.Serialization.JsonPropertyName("data")]
        public object? Data { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("error")]
        public ErrorInfo? Error { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
        public string? Timestamp { get; set; } = DateTime.UtcNow.ToString("O");
        
        [System.Text.Json.Serialization.JsonPropertyName("session_id")]
        public string? SessionId { get; set; }
    }

    public class ErrorInfo
    {
        [System.Text.Json.Serialization.JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("details")]
        public Dictionary<string, object>? Details { get; set; }
    }

    /// <summary>
    /// Modern WebSocket client implementing the new Cloud API specification.
    /// Supports RPC-style request/response and stream subscriptions.
    /// </summary>
    public class CloudWebSocketClient : IDisposable, IAsyncDisposable
    {
        private const int ReceiveBufferSize = 8192;
        private const int MaxMessagePreviewLength = 200;
        private const string DEFAULT_SESSION_ID = "net-client";

        private Uri _uri;
        private ClientWebSocket? _socket;
        private readonly object _socketLock = new(); // Lock for state-check-and-send atomicity (BUG FIX #5)
        private CancellationTokenSource? _cts;
        private readonly IEventBus _bus;
        private readonly ICancellationProvider _cancellationProvider;
        private readonly IEventLogger _logger;
        private readonly Channel<string> _messageChannel;
        private readonly object _processingTaskSync = new();
        private Task? _processingTask;
        private int _connectionAttempts;
        private long _receivedMessages;
        private ConcurrentDictionary<string, TaskCompletionSource<CloudMessage>> _pendingRequests =
            new ConcurrentDictionary<string, TaskCompletionSource<CloudMessage>>();
        private string _sessionId = DEFAULT_SESSION_ID;
        private string? _userId;
        private string? _apiKey;  // Stored API key for authentication
        private readonly ConcurrentDictionary<string, RoundStartContext> _roundStartContexts =
            new ConcurrentDictionary<string, RoundStartContext>();

        private sealed class RoundStartContext
        {
            public string? InstanceId { get; set; }
            public string RoundType { get; set; } = "Unknown";
            public string? MapName { get; set; }
            public DateTime StartTimeUtc { get; set; }
        }

        public event EventHandler<CloudMessage>? MessageReceived;
        public event EventHandler? Connected;
        public event EventHandler? Disconnected;
        public event EventHandler<Exception>? ErrorOccurred;

        public string SessionId => _sessionId;
        public string? UserId => _userId;
        public string? ApiKey => _apiKey;
        public bool IsConnected => _socket?.State == WebSocketState.Open;

        /// <summary>
        /// Update the WebSocket endpoint URL
        /// </summary>
        public void UpdateEndpoint(string url)
        {
            _uri = new Uri(url);
            _logger.LogEvent("CloudWebSocket", $"Endpoint updated to: {url}");
        }

        public CloudWebSocketClient(
            string url,
            IEventBus bus,
            ICancellationProvider cancellation,
            IEventLogger logger,
            string? apiKey = null,
            string? userId = null)
        {
            _uri = new Uri(url);
            _bus = bus;
            _cancellationProvider = cancellation;
            _logger = logger;
            _apiKey = apiKey;
            _userId = userId;
            _messageChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });
            
            // Log initialization parameters for debugging
            _logger.LogEvent("CloudWebSocket", 
                $"Initialized with URL: {url}, HasApiKey: {!string.IsNullOrWhiteSpace(apiKey)}, HasUserId: {!string.IsNullOrWhiteSpace(userId)}");
        }

        public async Task StartAsync()
        {
            _logger.LogEvent("CloudWebSocket", () => $"Starting client for {_uri}.");
            _connectionAttempts = 0;
            _receivedMessages = 0;
            _cts?.Dispose();

            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationProvider.Token);
            _cts = cts;
            var token = cts.Token;

            EnsureProcessingTask(token);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    EnsureProcessingTask(token);
                    _socket = new ClientWebSocket();
                    
                    // Configure WebSocket options for stability
                    // KeepAlive every 30 seconds (server pings every 60 seconds)
                    _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

                    try
                    {
                        _connectionAttempts++;
                        _logger.LogEvent("CloudWebSocket", () => $"Attempt {_connectionAttempts}: connecting to {_uri}.");

                        await _socket.ConnectAsync(_uri, token).ConfigureAwait(false);
                        _logger.LogEvent("CloudWebSocket", "Connection established.");

                        // Start receive loop in background
                        // The receive loop must be running before we can send/receive messages
                        var receiveTask = Task.Run(() => ReceiveLoopAsync(token), token);

                        // Auto-authenticate if API key is available
                        if (!string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_userId))
                        {
                            try
                            {
                                _logger.LogEvent("CloudWebSocket", $"Auto-authenticating user: {_userId}, HasApiKey: {!string.IsNullOrWhiteSpace(_apiKey)}");
                                // Null-forgiving operators because we just checked above
                                await LoginWithApiKeyAsync(_userId!, _apiKey!, "1.0.0", token).ConfigureAwait(false);
                                _logger.LogEvent("CloudWebSocket", "Auto-authentication successful.");
                            }
                            catch (Exception authEx)
                            {
                                _logger.LogEvent("CloudWebSocket", $"Auto-authentication failed: {authEx.GetType().Name} - {authEx.Message}\nStackTrace: {authEx.StackTrace}", Serilog.Events.LogEventLevel.Error);
                            }
                        }
                        else
                        {
                            _logger.LogEvent("CloudWebSocket", $"Skipping auto-authentication. HasApiKey: {!string.IsNullOrWhiteSpace(_apiKey)}, HasUserId: {!string.IsNullOrWhiteSpace(_userId)}", Serilog.Events.LogEventLevel.Warning);
                        }
                        
                        Connected?.Invoke(this, EventArgs.Empty);
                        
                        _logger.LogEvent("CloudWebSocket", "Waiting for ReceiveLoopAsync to complete", Serilog.Events.LogEventLevel.Debug);
                        await receiveTask.ConfigureAwait(false);

                        _logger.LogEvent("CloudWebSocket", "Receive loop completed.");
                        Disconnected?.Invoke(this, EventArgs.Empty);

                        // Connection ended; fail in-flight RPC requests immediately.
                        CancelAllPendingRequests(new InvalidOperationException("Cloud WebSocket disconnected"));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogEvent("CloudWebSocket", () => $"Connection error (Attempt {_connectionAttempts}): {ex.GetType().Name} - {ex.Message}", Serilog.Events.LogEventLevel.Error);
                        ErrorOccurred?.Invoke(this, ex);

                        // Connection error means outstanding requests will never receive responses.
                        CancelAllPendingRequests(ex);

                        if (!token.IsCancellationRequested)
                        {
                            // Exponential backoff: 500ms, 1s, 2s, 4s, max 30s
                            int delayMs = Math.Min(500 * (int)Math.Pow(2, Math.Min(_connectionAttempts - 1, 5)), 30000);
                            _logger.LogEvent("CloudWebSocket", $"Scheduling reconnect in {delayMs}ms (Attempt {_connectionAttempts}).");
                            await Task.Delay(delayMs, token).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        _logger.LogEvent("CloudWebSocket", "Disposing socket instance.");
                        _socket?.Dispose();
                    }
                }
            }
            finally
            {
                _logger.LogEvent("CloudWebSocket", "StartAsync exiting and disposing cancellation token source.");
                if (ReferenceEquals(_cts, cts))
                {
                    _cts = null;
                }
                cts.Dispose();
            }
        }

        private async Task AuthenticateAsync(CancellationToken token)
        {
            var authMsg = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "auth.connect",
                Params = new
                {
                    clientId = DEFAULT_SESSION_ID,
                    clientVersion = "1.0.0",
                    capabilities = new[] { "game.roundStart", "game.roundEnd", "instance.join", "stats.query" }
                }
            };

            var response = await SendRequestAsync(authMsg, token).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Authentication failed: {response.Error?.Message}");
            }

            // Parse session info
            var resultJson = JsonSerializer.Serialize(response.Result);
            using (var doc = JsonDocument.Parse(resultJson))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("sessionId", out var sessionIdElem))
                {
                    _sessionId = sessionIdElem.GetString() ?? DEFAULT_SESSION_ID;
                }
                if (root.TryGetProperty("userId", out var userIdElem))
                {
                    _userId = userIdElem.GetString();
                }
            }

            _logger.LogEvent("CloudWebSocket", $"Authenticated as {_sessionId}");
        }

        /// <summary>
        /// Login with player ID to establish user session
        /// </summary>
        public async Task<Dictionary<string, object>> LoginAsync(
            string playerId,
            string clientVersion = "1.0.0",
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "auth.login",
                Params = new
                {
                    player_id = playerId,
                    client_version = clientVersion
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Login failed: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }

                    // Update internal user ID
                    if (doc.RootElement.TryGetProperty("player_id", out var playerIdElem))
                    {
                        _userId = playerIdElem.GetString();
                    }
                    else if (doc.RootElement.TryGetProperty("user_id", out var userIdElem))
                    {
                        _userId = userIdElem.GetString();
                    }
                }
            }

            _logger.LogEvent("CloudWebSocket", $"Logged in as user: {_userId ?? playerId}");
            return result;
        }

        /// <summary>
        /// Logout from the current session
        /// </summary>
        public async Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "auth.logout",
                Params = new { }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Logout failed: {response.Error?.Message}");
            }

            _logger.LogEvent("CloudWebSocket", "Logged out successfully");
        }

        /// <summary>
        /// Refresh the current session
        /// </summary>
        public async Task<Dictionary<string, object>> RefreshSessionAsync(CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "auth.refresh",
                Params = new { }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Session refresh failed: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            _logger.LogEvent("CloudWebSocket", "Session refreshed successfully");
            return result;
        }

        /// <summary>
        /// Send an RPC request and wait for response
        /// BUG FIX #5 (HIGH): Added lock to prevent TOCTOU race between state check and SendAsync.
        /// Previously: Between the state check and the actual SendAsync call, another thread could
        /// dispose _socket, causing ObjectDisposedException. Now: The check and send are atomic.
        /// </summary>
        public async Task<CloudMessage> SendRequestAsync(CloudMessage request, CancellationToken cancellationToken = default)
        {
            // SAFETY: Lock around socket state check and send to prevent disposal race
            lock (_socketLock)
            {
                if (_socket?.State != WebSocketState.Open)
                {
                    throw new InvalidOperationException("WebSocket is not connected");
                }
            }

            // Add session_id to request if available and not an auth request
            if (!string.IsNullOrEmpty(_sessionId) && 
                _sessionId != DEFAULT_SESSION_ID &&
                request.Method != "auth.register" && 
                request.Method != "auth.loginWithApiKey" &&
                request.Method != "auth.loginWithOneTimeToken")
            {
                request.SessionId = _sessionId;
                _logger.LogEvent("CloudWebSocket", $"Adding session_id to request: {request.Method}, SessionId: {_sessionId}", Serilog.Events.LogEventLevel.Debug);
            }
            else
            {
                _logger.LogEvent("CloudWebSocket", $"NOT adding session_id - Method: {request.Method}, SessionId: {_sessionId}, DEFAULT: {DEFAULT_SESSION_ID}", Serilog.Events.LogEventLevel.Debug);
            }

            var tcs = new TaskCompletionSource<CloudMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pendingRequests.TryAdd(request.Id, tcs))
            {
                // Request ID collision - this should never happen with GUIDs
                throw new InvalidOperationException($"Request ID collision: {request.Id}");
            }

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null, // Use explicit JsonPropertyName attributes
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                var json = JsonSerializer.Serialize(request, options);
                var bytes = Encoding.UTF8.GetBytes(json);

                _logger.LogEvent("CloudWebSocket", $"Sending request - Id: {request.Id}, Method: {request.Method}, SocketState: {_socket?.State}", Serilog.Events.LogEventLevel.Debug);
                
                // SAFETY: Check again within try-catch to handle disposal during serialization
                if (_socket?.State != WebSocketState.Open)
                {
                    throw new InvalidOperationException("WebSocket is no longer connected during send");
                }
                
                await _socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken
                ).ConfigureAwait(false);
                
                _logger.LogEvent("CloudWebSocket", $"Request sent successfully - Id: {request.Id}", Serilog.Events.LogEventLevel.Debug);

                // Apply timeout and caller cancellation while waiting for response.
                // BUG FIX #10 (MEDIUM): Replaced incorrect Task.Delay(Timeout.InfiniteTimeSpan) with finite delay.
                // Previously: Used InfiniteTimeSpan which would never timeout, defeating the 30s timeout.
                // Now: Use Task.Delay with cancellation token, which will be cancelled after 30s by timeoutCts.
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                {
                    // Wait for either the response (tcs.Task) or the timeout/cancellation
                    var completed = await Task.WhenAny(
                        tcs.Task,
                        Task.Delay(Timeout.Infinite, linkedCts.Token)  // Will be cancelled after 30s or by cancellationToken
                    ).ConfigureAwait(false);

                    if (completed == tcs.Task)
                    {
                        return await tcs.Task.ConfigureAwait(false);
                    }

                    // If we reach here, Task.Delay was cancelled (timeout or caller cancellation)
                    if (timeoutCts.IsCancellationRequested)
                    {
                        if (_pendingRequests.TryRemove(request.Id, out _))
                        {
                            _logger.LogEvent("CloudWebSocket", $"Request timeout: {request.Id} ({request.Method})", Serilog.Events.LogEventLevel.Warning);
                        }
                        throw new TimeoutException($"Request {request.Method} timed out after 30 seconds");
                    }

                    // Caller cancelled the request
                    throw new OperationCanceledException(cancellationToken);
                }
            }
            finally
            {
                // Only remove if we added it and it hasn't been removed already (by response handler)
                _pendingRequests.TryRemove(request.Id, out _);
            }
        }

        /// <summary>
        /// Register a new user and get API key
        /// </summary>
        public async Task<(string userId, string apiKey)> RegisterUserAsync(
            string playerId,
            string clientVersion = "1.0.0",
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "auth.register",
                Params = new
                {
                    player_id = playerId,
                    client_version = clientVersion
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to register user: {response.Error?.Message}");
            }

            var resultJson = JsonSerializer.Serialize(response.Result);
            using (var doc = JsonDocument.Parse(resultJson))
            {
                var userId = doc.RootElement.GetProperty("user_id").GetString() 
                    ?? throw new InvalidOperationException("No user_id in response");
                var apiKey = doc.RootElement.GetProperty("api_key").GetString()
                    ?? throw new InvalidOperationException("No api_key in response");

                _apiKey = apiKey;
                _userId = userId;

                _logger.LogEvent("CloudWebSocket", $"User registered: {userId}");
                return (userId, apiKey);
            }
        }

        /// <summary>
        /// Login with API key
        /// </summary>
        public async Task<string> LoginWithApiKeyAsync(
            string playerId,
            string apiKey,
            string clientVersion = "1.0.0",
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "auth.loginWithApiKey",
                Params = new
                {
                    player_id = playerId,
                    api_key = apiKey,
                    client_version = clientVersion
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to login: {response.Error?.Message}");
            }

            var resultJson = JsonSerializer.Serialize(response.Result);
            using (var doc = JsonDocument.Parse(resultJson))
            {
                var sessionToken = doc.RootElement.GetProperty("session_token").GetString()
                    ?? throw new InvalidOperationException("No session_token in response");

                _apiKey = apiKey;
                _userId = playerId;
                _sessionId = sessionToken; // Store session ID for future requests

                _logger.LogEvent("CloudWebSocket", $"Logged in with API key: {playerId}, SessionId: {sessionToken}");
                return sessionToken;
            }
        }

        /// <summary>
        /// Generate one-time login token
        /// </summary>
        public async Task<(string token, string loginUrl)> GenerateOneTimeTokenAsync(
            string playerId,
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "auth.generateOneTimeToken",
                Params = new
                {
                    player_id = playerId,
                    api_key = apiKey
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to generate token: {response.Error?.Message}");
            }

            var resultJson = JsonSerializer.Serialize(response.Result);
            using (var doc = JsonDocument.Parse(resultJson))
            {
                var token = doc.RootElement.GetProperty("token").GetString()
                    ?? throw new InvalidOperationException("No token in response");
                var loginUrl = doc.RootElement.GetProperty("login_url").GetString()
                    ?? throw new InvalidOperationException("No login_url in response");

                _logger.LogEvent("CloudWebSocket", $"One-time token generated for: {playerId}");
                return (token, loginUrl);
            }
        }

        /// <summary>
        /// Compatibility shim for legacy game.roundStart callers.
        /// Current backend persists rounds via round.report, so this method stores start context locally.
        /// </summary>
        public async Task<string> GameRoundStartAsync(
            string? instanceId,
            string playerName, 
            string roundType, 
            string? mapName = null, 
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            var roundId = Guid.NewGuid().ToString("N");
            _roundStartContexts[roundId] = new RoundStartContext
            {
                InstanceId = instanceId,
                RoundType = string.IsNullOrWhiteSpace(roundType) ? "Unknown" : roundType,
                MapName = mapName,
                StartTimeUtc = DateTime.UtcNow,
            };

            _logger.LogEvent("CloudWebSocket", $"Round start context stored: {roundId} ({roundType})");
            return roundId;
        }

        /// <summary>
        /// Compatibility shim for legacy game.roundEnd callers.
        /// Converts start/end state into round.report payload for the current backend.
        /// </summary>
        public async Task<Dictionary<string, object>> GameRoundEndAsync(
            string roundId,
            bool survived,
            int duration,
            double? damageDealt = null,
            string[]? itemsObtained = null,
            string? terrorName = null,
            int? initialPlayerCount = null,
            CancellationToken cancellationToken = default)
        {
            _roundStartContexts.TryRemove(roundId, out var context);

            var startTime = context?.StartTimeUtc ?? DateTime.UtcNow.AddSeconds(-Math.Max(0, duration));
            var startTimeIso = startTime.ToString("O");
            var endTimeIso = DateTime.UtcNow.ToString("O");
            var normalizedInitialCount = Math.Max(0, initialPlayerCount ?? 0);
            var survivorCount = normalizedInitialCount > 0
                ? (survived ? normalizedInitialCount : Math.Max(0, normalizedInitialCount - 1))
                : (survived ? 1 : 0);

            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "round.report",
                Params = new
                {
                    instance_id = context?.InstanceId,
                    round_type = context?.RoundType ?? "Unknown",
                    terror_name = terrorName,
                    terror_key = terrorName,
                    start_time = startTimeIso,
                    end_time = endTimeIso,
                    initial_player_count = normalizedInitialCount,
                    survivor_count = survivorCount,
                    status = survived ? "COMPLETED" : "FAILED",
                    // Preserve legacy fields for future backend compatibility/analytics.
                    legacy_round_id = roundId,
                    duration_seconds = duration,
                    damage_dealt = damageDealt,
                    items_obtained = itemsObtained
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to end round: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            var resultJson = JsonSerializer.Serialize(response.Result);
            using (var doc = JsonDocument.Parse(resultJson))
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    result[prop.Name] = prop.Value;
                }
            }

            return result;
        }

        #region Instance Management APIs

        /// <summary>
        /// [DEPRECATED] Join instance - DISABLED (VRChat constraint violation)
        /// </summary>
        [Obsolete("Remote instance joining has been disabled due to VRChat platform constraints")]
        public Task<string> InstanceJoinAsync(string instanceId, string playerName, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Remote instance joining is not supported. VRChat platform does not allow external applications to control world joining. Players must manually join worlds through the VRChat client.");
        }

        /// <summary>
        /// [DEPRECATED] Leave instance - DISABLED (VRChat constraint violation)
        /// </summary>
        [Obsolete("Remote instance leaving has been disabled due to VRChat platform constraints")]
        public Task InstanceLeaveAsync(string instanceId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Remote instance leaving is not supported. VRChat platform does not allow external applications to control world joining/leaving. Players must manually leave worlds through the VRChat client.");
        }

        /// <summary>
        /// Create a new instance
        /// </summary>
        public async Task<Dictionary<string, object>> InstanceCreateAsync(
            int maxPlayers = 10,
            Dictionary<string, object>? settings = null,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "instance.create",
                Params = new
                {
                    max_players = maxPlayers,
                    settings = settings ?? new Dictionary<string, object>
                    {
                        ["auto_suicide_mode"] = "Individual",
                        ["voting_timeout"] = 30
                    }
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to create instance: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// List available instances
        /// </summary>
        public async Task<List<Dictionary<string, object>>> InstanceListAsync(
            string filter = "available",
            int limit = 50,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "instance.list",
                Params = new { filter, limit, offset }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to list instances: {response.Error?.Message}");
            }

            var result = new List<Dictionary<string, object>>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    if (doc.RootElement.TryGetProperty("instances", out var instancesElem) &&
                        instancesElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var instance in instancesElem.EnumerateArray())
                        {
                            var instanceDict = new Dictionary<string, object>();
                            foreach (var prop in instance.EnumerateObject())
                            {
                                instanceDict[prop.Name] = prop.Value;
                            }
                            result.Add(instanceDict);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Update instance settings
        /// </summary>
        public async Task InstanceUpdateAsync(
            string instanceId,
            Dictionary<string, object> updates,
            CancellationToken cancellationToken = default)
        {
            updates ??= new Dictionary<string, object>();

            // Backend expects max_players/settings at top-level params, not nested "updates".
            updates.TryGetValue("max_players", out var maxPlayersValue);
            updates.TryGetValue("settings", out var settingsValue);

            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "instance.update",
                Params = new
                {
                    instance_id = instanceId,
                    max_players = maxPlayersValue,
                    settings = settingsValue,
                    // Keep legacy nested payload for backward compatibility with any older backend variants.
                    updates
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to update instance: {response.Error?.Message}");
            }
        }

        /// <summary>
        /// Delete an instance
        /// </summary>
        public async Task InstanceDeleteAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "instance.delete",
                Params = new { instance_id = instanceId }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to delete instance: {response.Error?.Message}");
            }
        }

        /// <summary>
        /// Get instance details
        /// </summary>
        public async Task<Dictionary<string, object>> InstanceGetAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "instance.get",
                Params = new { instance_id = instanceId }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to get instance: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Send alert to instance members
        /// </summary>
        public async Task<Dictionary<string, object>> InstanceAlertAsync(
            string instanceId,
            string alertType,
            string? message = null,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "instance.alert",
                Params = new
                {
                    instanceId = instanceId,
                    alertType = alertType,
                    message = message
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to send alert: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get player states for an instance
        /// </summary>
        public async Task<List<PlayerStateInfo>> GetPlayerStatesAsync(string instanceId, CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "player.states.get",
                Params = new { instanceId = instanceId }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to get player states: {response.Error?.Message}");
            }

            var resultJson = JsonSerializer.Serialize(response.Result);
            using (var doc = JsonDocument.Parse(resultJson))
            {
                if (doc.RootElement.TryGetProperty("player_states", out var statesElem))
                {
                    var states = JsonSerializer.Deserialize<List<PlayerStateInfo>>(statesElem.GetRawText());
                    return states ?? new List<PlayerStateInfo>();
                }
            }

            return new List<PlayerStateInfo>();
        }

        /// <summary>
        /// Join an instance
        /// </summary>
        public async Task JoinInstanceAsync(string instanceId, string playerId, CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "instance.join",
                Params = new
                {
                    instance_id = instanceId,
                    player_id = playerId,
                    player_name = playerId
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to join instance: {response.Error?.Message}");
            }

            _logger.LogEvent("CloudWebSocket", $"Joined instance: {instanceId}");
        }

        /// <summary>
        /// Leave an instance
        /// </summary>
        public async Task LeaveInstanceAsync(string instanceId, string playerId, CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "instance.leave",
                Params = new
                {
                    instance_id = instanceId,
                    player_id = playerId
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to leave instance: {response.Error?.Message}");
            }

            _logger.LogEvent("CloudWebSocket", $"Left instance: {instanceId}");
        }

        /// <summary>
        /// Find desire players for a terror in an instance
        /// </summary>
        public async Task<List<DesirePlayerInfo>> FindDesirePlayersAsync(string instanceId, string terrorName, string roundKey, CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "wished.terrors.findDesirePlayers",
                Params = new { instance_id = instanceId, terror_name = terrorName, round_key = roundKey }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to find desire players: {response.Error?.Message}");
            }

            var resultJson = JsonSerializer.Serialize(response.Result);
            using (var doc = JsonDocument.Parse(resultJson))
            {
                if (doc.RootElement.TryGetProperty("desire_players", out var playersElem))
                {
                    var players = JsonSerializer.Deserialize<List<DesirePlayerInfo>>(playersElem.GetRawText());
                    return players ?? new List<DesirePlayerInfo>();
                }
            }

            return new List<DesirePlayerInfo>();
        }

        /// <summary>
        /// Update player state
        /// </summary>
        public async Task UpdatePlayerStateAsync(
            string instanceId, 
            string playerId, 
            double velocity = 0, 
            double afkDuration = 0, 
            List<string>? items = null, 
            double damage = 0, 
            bool isAlive = true,
            string? playerName = null,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "player.state.update",
                Params = new
                {
                    instance_id = instanceId,
                    player_state = new
                    {
                        player_id = playerId,
                        player_name = playerName ?? playerId,  // プレイヤー名を追加
                        velocity,
                        afk_duration = afkDuration,
                        items = items ?? new List<string>(),
                        damage,
                        is_alive = isAlive,
                        // 送信時刻を必ず含める。これが無いとサーバ/UI 側で
                        // 「更新: 不明」とされ、表示が振れる原因になる。
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to update player state: {response.Error?.Message}");
            }
        }

        /// <summary>
        /// Report round completion
        /// </summary>
        public async Task ReportRoundAsync(
            string instanceId,
            string roundType,
            string? terrorName = null,
            string? terrorKey = null,
            DateTime? startTime = null,
            DateTime? endTime = null,
            int? initialPlayerCount = null,
            int? survivorCount = null,
            string status = "COMPLETED",
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "round.report",
                Params = new
                {
                    instance_id = instanceId,
                    round_type = roundType,
                    terror_name = terrorName,
                    terror_key = terrorKey,
                    start_time = (startTime ?? DateTime.Now).ToUniversalTime().ToString("o"),
                    end_time = (endTime ?? DateTime.Now).ToUniversalTime().ToString("o"),
                    initial_player_count = initialPlayerCount ?? 0,
                    survivor_count = survivorCount ?? 0,
                    status
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to report round: {response.Error?.Message}");
            }
        }

        /// <summary>
        /// Announce threat/terror to instance
        /// </summary>
        public async Task AnnounceThreatAsync(
            string instanceId,
            string terrorName,
            string roundKey,
            List<DesirePlayerInfo>? desirePlayers = null,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "threat.announce",
                Params = new
                {
                    instance_id = instanceId,
                    terror_name = terrorName,
                    round_key = roundKey,
                    desire_players = desirePlayers ?? new List<DesirePlayerInfo>()
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to announce threat: {response.Error?.Message}");
            }
        }

        /// <summary>
        /// Record threat response (player decision)
        /// </summary>
        public async Task RecordThreatResponseAsync(
            string threatId,
            string playerId,
            string decision,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "threat.response",
                Params = new
                {
                    threat_id = threatId,
                    player_id = playerId,
                    decision
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to record threat response: {response.Error?.Message}");
            }
        }

        /// <summary>
        /// Get single player state
        /// </summary>
        public async Task<Dictionary<string, object>> GetPlayerStateAsync(
            string instanceId,
            string? playerId = null,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "player.state.get",
                Params = new
                {
                    instance_id = instanceId,
                    player_id = playerId ?? _sessionId
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to get player state: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get all player states for instance
        /// </summary>
        public async Task<List<Dictionary<string, object>>> GetAllPlayerStatesAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "player.states.get",
                Params = new { instanceId = instanceId }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to get all player states: {response.Error?.Message}");
            }

            var result = new List<Dictionary<string, object>>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    if (doc.RootElement.TryGetProperty("player_states", out var statesElem) &&
                        statesElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var state in statesElem.EnumerateArray())
                        {
                            var stateDict = new Dictionary<string, object>();
                            foreach (var prop in state.EnumerateObject())
                            {
                                stateDict[prop.Name] = prop.Value;
                            }
                            result.Add(stateDict);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Report monitoring status
        /// </summary>
        public async Task ReportMonitoringStatusAsync(
            string? instanceId,
            string applicationStatus,
            string applicationVersion,
            int uptime,
            double memoryUsage,
            double cpuUsage,
            string? oscStatus = null,
            double? oscLatency = null,
            string? vrchatStatus = null,
            string? vrchatWorldId = null,
            string? vrchatInstanceId = null,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "monitoring.report",
                Params = new
                {
                    instance_id = instanceId,
                    status_data = new
                    {
                        application_status = applicationStatus,
                        application_version = applicationVersion,
                        uptime,
                        memory_usage = memoryUsage,
                        cpu_usage = cpuUsage,
                        osc_status = oscStatus,
                        osc_latency = oscLatency,
                        vrchat_status = vrchatStatus,
                        vrchat_world_id = vrchatWorldId,
                        vrchat_instance_id = vrchatInstanceId
                    }
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to report monitoring status: {response.Error?.Message}");
            }
        }

        /// <summary>
        /// Get monitoring status history
        /// </summary>
        public async Task<List<Dictionary<string, object>>> GetMonitoringStatusHistoryAsync(
            string userId,
            int limit = 10,
            CancellationToken cancellationToken = default)
        {
            // Note: userId is retained for API compatibility; backend authenticates via session.
            _ = userId;
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "monitoring.status",
                Params = new
                {
                    limit
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to get monitoring status: {response.Error?.Message}");
            }

            var result = new List<Dictionary<string, object>>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    if (doc.RootElement.TryGetProperty("statuses", out var statusesElem) &&
                        statusesElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var status in statusesElem.EnumerateArray())
                        {
                            var statusDict = new Dictionary<string, object>();
                            foreach (var prop in status.EnumerateObject())
                            {
                                statusDict[prop.Name] = prop.Value;
                            }
                            result.Add(statusDict);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get monitoring errors
        /// </summary>
        public async Task<List<Dictionary<string, object>>> GetMonitoringErrorsAsync(
            string userId,
            string? severity = null,
            int limit = 50,
            CancellationToken cancellationToken = default)
        {
            // Note: userId is retained for API compatibility; backend authenticates via session.
            _ = userId;
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "monitoring.errors",
                Params = new
                {
                    severity,
                    limit
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to get monitoring errors: {response.Error?.Message}");
            }

            var result = new List<Dictionary<string, object>>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    if (doc.RootElement.TryGetProperty("errors", out var errorsElem) &&
                        errorsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var error in errorsElem.EnumerateArray())
                        {
                            var errorDict = new Dictionary<string, object>();
                            foreach (var prop in error.EnumerateObject())
                            {
                                errorDict[prop.Name] = prop.Value;
                            }
                            result.Add(errorDict);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Log error to Cloud
        /// </summary>
        public async Task LogErrorAsync(
            string? instanceId,
            string severity,
            string message,
            string? stack = null,
            Dictionary<string, object>? context = null,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "monitoring.logError",
                Params = new
                {
                    instance_id = instanceId,
                    severity,
                    message,
                    stack,
                    context
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                // Don't throw on error logging failure to avoid recursive errors
                System.Diagnostics.Debug.WriteLine($"Failed to log error to cloud: {response.Error?.Message}");
            }
        }

        #region Voting System APIs

        /// <summary>
        /// Start a coordinated voting campaign
        /// </summary>
        public async Task<Dictionary<string, object>> StartVotingAsync(
            string instanceId,
            string terrorName,
            DateTime expiresAt,
            string? roundKey = null,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "coordinated.voting.start",
                Params = new
                {
                    instance_id = instanceId,
                    terror_name = terrorName,
                    round_key = string.IsNullOrWhiteSpace(roundKey) ? null : roundKey.Trim(),
                    // Always send UTC. DateTime.ToString("O") on a local/Unspecified
                    // value omits the offset, which makes the server's clamp logic
                    // misinterpret the expiry across time zones.
                    expires_at = expiresAt.ToUniversalTime().ToString("o")
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to start voting: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Submit a vote in a voting campaign
        /// </summary>
        public async Task SubmitVoteAsync(
            string campaignId,
            string playerId,
            string decision,
            CancellationToken cancellationToken = default)
        {
            // The server resolves voter identity from the authenticated session.
            // Keep playerId for API compatibility, but do not send it.
            _ = playerId;
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "coordinated.voting.vote",
                Params = new
                {
                    campaign_id = campaignId,
                    decision
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to submit vote: {response.Error?.Message}");
            }
        }

        /// <summary>
        /// Get voting campaign details
        /// </summary>
        public async Task<Dictionary<string, object>> GetVotingCampaignAsync(
            string campaignId,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "coordinated.voting.getCampaign",
                Params = new { campaign_id = campaignId }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to get campaign: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        public async Task<Dictionary<string, object>?> GetActiveVotingCampaignAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "coordinated.voting.getActive",
                Params = new { instance_id = instanceId }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to get active campaign: {response.Error?.Message}");
            }

            if (response.Result == null)
            {
                return null;
            }

            var resultJson = JsonSerializer.Serialize(response.Result);
            using (var doc = JsonDocument.Parse(resultJson))
            {
                if (!doc.RootElement.TryGetProperty("campaign", out var campaignElement) || campaignElement.ValueKind == JsonValueKind.Null)
                {
                    return null;
                }

                var result = new Dictionary<string, object>();
                foreach (var prop in campaignElement.EnumerateObject())
                {
                    result[prop.Name] = prop.Value;
                }

                return result;
            }
        }

        public async Task<CoordinatedAutoSuicideStateInfo> GetCoordinatedAutoSuicideStateAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "coordinated.autoSuicide.get",
                Params = new { instance_id = instanceId }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to get coordinated auto suicide state: {response.Error?.Message}");
            }

            if (response.Result == null)
            {
                return new CoordinatedAutoSuicideStateInfo();
            }

            var resultJson = JsonSerializer.Serialize(response.Result);
            var state = JsonSerializer.Deserialize<CoordinatedAutoSuicideStateInfo>(resultJson);
            return state ?? new CoordinatedAutoSuicideStateInfo();
        }

        #endregion

        #region Wished Terrors APIs

        /// <summary>
        /// Update wished terrors for a player
        /// </summary>
        public async Task UpdateWishedTerrorsAsync(
            string playerId,
            List<string> wishedTerrors,
            CancellationToken cancellationToken = default)
        {
            // The server keys wished terrors off the authenticated session
            // (ws.userId), so passing player_id from the client is both ignored
            // and a spoofing surface. Only send the actual payload.
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "wished.terrors.update",
                Params = new
                {
                    wished_terrors = wishedTerrors
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to update wished terrors: {response.Error?.Message}");
            }
        }

        /// <summary>
        /// Get wished terrors for a player
        /// </summary>
        public async Task<List<string>> GetWishedTerrorsAsync(
            string playerId,
            CancellationToken cancellationToken = default)
        {
            // Server uses ws.userId to scope the lookup; player_id is ignored.
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "wished.terrors.get",
                Params = new { }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to get wished terrors: {response.Error?.Message}");
            }

            var result = new List<string>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    if (doc.RootElement.TryGetProperty("wished_terrors", out var terrors) && terrors.ValueKind == JsonValueKind.Array)
                    {
                        // Server returns array of WishedTerror objects:
                        //   { id, player_id, terror_name, round_key, created_at }
                        // Older revisions of this client called terror.GetString()
                        // on each element which silently returned null for objects,
                        // so the wished list always looked empty. Tolerate both
                        // shapes (legacy string elements and current objects).
                        foreach (var terror in terrors.EnumerateArray())
                        {
                            if (terror.ValueKind == JsonValueKind.String)
                            {
                                var value = terror.GetString();
                                if (!string.IsNullOrEmpty(value))
                                {
                                    result.Add(value);
                                }
                            }
                            else if (terror.ValueKind == JsonValueKind.Object &&
                                     terror.TryGetProperty("terror_name", out var name) &&
                                     name.ValueKind == JsonValueKind.String)
                            {
                                var value = name.GetString();
                                if (!string.IsNullOrEmpty(value))
                                {
                                    result.Add(value);
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        #endregion

        #region Profile APIs

        /// <summary>
        /// Get player profile
        /// </summary>
        public async Task<Dictionary<string, object>> GetProfileAsync(
            string playerId,
            CancellationToken cancellationToken = default)
        {
            // profile.get is scoped by the authenticated session; do not send
            // player_id (the server already ignores it and accepting it would
            // imply a cross-user lookup is possible).
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "profile.get",
                Params = new { }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to get profile: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Update player profile
        /// </summary>
        public async Task<Dictionary<string, object>> UpdateProfileAsync(
            string playerId,
            string? playerName = null,
            int? skillLevel = null,
            Dictionary<string, object>? terrorStats = null,
            int? totalRounds = null,
            int? totalSurvived = null,
            CancellationToken cancellationToken = default)
        {
            // profile.update is scoped to ws.userId; the server-side handler
            // does NOT honor a client-supplied player_id, so omit it to avoid
            // implying cross-user updates are possible.
            var updateData = new Dictionary<string, object>();

            if (playerName != null) updateData["player_name"] = playerName;
            if (skillLevel.HasValue) updateData["skill_level"] = skillLevel.Value;
            if (terrorStats != null) updateData["terror_stats"] = terrorStats;
            if (totalRounds.HasValue) updateData["total_rounds"] = totalRounds.Value;
            if (totalSurvived.HasValue) updateData["total_survived"] = totalSurvived.Value;

            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "profile.update",
                Params = updateData
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to update profile: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        #endregion

        #region Settings APIs

        /// <summary>
        /// Get settings from cloud
        /// </summary>
        public async Task<Dictionary<string, object>> GetSettingsAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            // Note: userId is retained for API compatibility; backend authenticates via session.
            _ = userId;
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "settings.get",
                Params = new { }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to get settings: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Update settings to cloud
        /// </summary>
        public async Task<Dictionary<string, object>> UpdateSettingsAsync(
            string userId,
            Dictionary<string, object> settings,
            CancellationToken cancellationToken = default)
        {
            // Note: userId is retained for API compatibility; backend authenticates via session.
            _ = userId;
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "settings.update",
                Params = new
                {
                    settings
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to update settings: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Sync settings with cloud
        /// </summary>
        public async Task<Dictionary<string, object>> SyncSettingsAsync(
            string userId,
            Dictionary<string, object> localSettings,
            int localVersion,
            CancellationToken cancellationToken = default)
        {
            // Note: userId is retained for API compatibility; backend authenticates via session.
            _ = userId;
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "settings.sync",
                Params = new
                {
                    local_settings = localSettings,
                    local_version = localVersion
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to sync settings: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        #endregion

        #region Analytics APIs

        /// <summary>
        /// Get player analytics
        /// </summary>
        public async Task<Dictionary<string, object>> GetPlayerAnalyticsAsync(
            string playerId,
            DateTime? startTime = null,
            DateTime? endTime = null,
            CancellationToken cancellationToken = default)
        {
            var requestParams = new Dictionary<string, object>
            {
                ["player_id"] = playerId
            };

            if (startTime.HasValue || endTime.HasValue)
            {
                requestParams["time_range"] = new
                {
                    // Always serialize as UTC. Local-kind DateTime.ToString("O")
                    // omits the offset and silently shifts the window on the
                    // server side.
                    start = startTime?.ToUniversalTime().ToString("o"),
                    end = endTime?.ToUniversalTime().ToString("o")
                };
            }

            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "analytics.player",
                Params = requestParams
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to get player analytics: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get terror analytics
        /// </summary>
        public async Task<Dictionary<string, object>> GetTerrorAnalyticsAsync(
            string? terrorName = null,
            DateTime? startTime = null,
            DateTime? endTime = null,
            CancellationToken cancellationToken = default)
        {
            var requestParams = new Dictionary<string, object>();

            if (terrorName != null) requestParams["terror_name"] = terrorName;

            if (startTime.HasValue || endTime.HasValue)
            {
                requestParams["time_range"] = new
                {
                    // Same UTC normalization as analytics.player.
                    start = startTime?.ToUniversalTime().ToString("o"),
                    end = endTime?.ToUniversalTime().ToString("o")
                };
            }

            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "analytics.terror",
                Params = requestParams
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to get terror analytics: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get instance analytics
        /// </summary>
        public async Task<Dictionary<string, object>> GetInstanceAnalyticsAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "analytics.instance",
                Params = new { instance_id = instanceId }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to get instance analytics: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get voting analytics
        /// </summary>
        public async Task<Dictionary<string, object>> GetVotingAnalyticsAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "analytics.voting",
                Params = new { instance_id = instanceId }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to get voting analytics: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Export analytics data
        /// </summary>
        public async Task<Dictionary<string, object>> ExportAnalyticsAsync(
            string format,
            string dataType,
            Dictionary<string, object>? filters = null,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "analytics.export",
                Params = new
                {
                    format,
                    data_type = dataType,
                    filters = filters ?? new Dictionary<string, object>()
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to export analytics: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        #endregion

        #region Backup APIs

        /// <summary>
        /// Create backup
        /// </summary>
        public async Task<Dictionary<string, object>> CreateBackupAsync(
            string type = "FULL",
            bool compress = true,
            bool encrypt = false,
            string? description = null,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "backup.create",
                Params = new
                {
                    type,
                    compress,
                    encrypt,
                    description
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to create backup: {response.Error?.Message}");
            }

            var result = new Dictionary<string, object>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        result[prop.Name] = prop.Value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Restore from backup
        /// </summary>
        public async Task RestoreBackupAsync(
            string backupId,
            bool validateBeforeRestore = true,
            bool createBackupBeforeRestore = true,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "backup.restore",
                Params = new
                {
                    backup_id = backupId,
                    validate_before_restore = validateBeforeRestore,
                    create_backup_before_restore = createBackupBeforeRestore
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to restore backup: {response.Error?.Message}");
            }
        }

        /// <summary>
        /// List available backups
        /// </summary>
        public async Task<List<Dictionary<string, object>>> ListBackupsAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            // Note: userId is retained for API compatibility; backend authenticates via session.
            _ = userId;
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "backup.list",
                Params = new { }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to list backups: {response.Error?.Message}");
            }

            var result = new List<Dictionary<string, object>>();
            if (response.Result != null)
            {
                var resultJson = JsonSerializer.Serialize(response.Result);
                using (var doc = JsonDocument.Parse(resultJson))
                {
                    if (doc.RootElement.TryGetProperty("backups", out var backupsElem) && 
                        backupsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var backup in backupsElem.EnumerateArray())
                        {
                            var backupDict = new Dictionary<string, object>();
                            foreach (var prop in backup.EnumerateObject())
                            {
                                backupDict[prop.Name] = prop.Value;
                            }
                            result.Add(backupDict);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Delete a backup
        /// </summary>
        public async Task DeleteBackupAsync(
            string backupId,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "backup.delete",
                Params = new
                {
                    backup_id = backupId
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to delete backup: {response.Error?.Message}");
            }
        }

        #endregion

        #region Remote Control APIs - REMOVED FOR SECURITY

        // SECURITY WARNING: Remote control functionality has been permanently disabled
        // These methods allowed remote command execution which posed critical security risks:
        // - Unauthorized access to application controls
        // - Potential for malicious command injection
        // - No way to properly authenticate/authorize remote commands
        // 
        // All remote.command.* endpoints have been removed from the server as well

        /// <summary>
        /// [DEPRECATED] Create remote command - DISABLED FOR SECURITY
        /// </summary>
        [Obsolete("Remote control functionality has been disabled for security reasons")]
        public Task<Dictionary<string, object>> CreateRemoteCommandAsync(
            string instanceId,
            string commandType,
            string action,
            Dictionary<string, object>? parameters = null,
            int priority = 0,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Remote control functionality has been permanently disabled for security reasons. This feature allowed unauthorized remote command execution.");
        }

        /// <summary>
        /// [DEPRECATED] Execute remote command - DISABLED FOR SECURITY
        /// </summary>
        [Obsolete("Remote control functionality has been disabled for security reasons")]
        public Task<Dictionary<string, object>> ExecuteRemoteCommandAsync(
            string commandId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Remote control functionality has been permanently disabled for security reasons. This feature allowed unauthorized remote command execution.");
        }

        /// <summary>
        /// [DEPRECATED] Get remote command status - DISABLED FOR SECURITY
        /// </summary>
        [Obsolete("Remote control functionality has been disabled for security reasons")]
        public Task<Dictionary<string, object>> GetRemoteCommandStatusAsync(
            string commandId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Remote control functionality has been permanently disabled for security reasons. This feature allowed unauthorized remote command execution.");
        }

        #endregion

        #region Subscription APIs

        /// <summary>
        /// Subscribe to a channel for real-time events
        /// </summary>
        public async Task<string> SubscribeAsync(
            string channel,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "subscribe",
                Params = new { channel }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to subscribe: {response.Error?.Message}");
            }

            var resultJson = JsonSerializer.Serialize(response.Result);
            using (var doc = JsonDocument.Parse(resultJson))
            {
                if (doc.RootElement.TryGetProperty("subscriptionId", out var subIdElem))
                {
                    return subIdElem.GetString() ?? throw new InvalidOperationException("No subscriptionId in response");
                }
            }

            throw new InvalidOperationException("Invalid response format");
        }

        /// <summary>
        /// Unsubscribe from a channel
        /// </summary>
        public async Task UnsubscribeAsync(
            string subscriptionId,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "unsubscribe",
                Params = new { subscriptionId }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to unsubscribe: {response.Error?.Message}");
            }
        }

        #endregion

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var socket = _socket;
            if (socket == null)
            {
                _logger.LogEvent("CloudWebSocket", "ReceiveLoopAsync: socket is null, exiting", Serilog.Events.LogEventLevel.Warning);
                return;
            }

            _logger.LogEvent("CloudWebSocket", $"ReceiveLoopAsync started - SocketState: {socket.State}", Serilog.Events.LogEventLevel.Debug);

            var receiveBuffer = new byte[ReceiveBufferSize];
            byte[]? messageBuffer = null;
            int messageOffset = 0;

            try
            {
                _logger.LogEvent("CloudWebSocket", "ReceiveLoopAsync: entering receive loop", Serilog.Events.LogEventLevel.Debug);
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(receiveBuffer);
                    WebSocketReceiveResult result;

                    try
                    {
                        result = await socket.ReceiveAsync(segment, token).ConfigureAwait(false);
                        _logger.LogEvent("CloudWebSocket", $"Received {result.Count} bytes, MessageType: {result.MessageType}, EndOfMessage: {result.EndOfMessage}", Serilog.Events.LogEventLevel.Debug);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogEvent("CloudWebSocket", $"Close message received: {result.CloseStatus} {result.CloseStatusDescription}");
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", token).ConfigureAwait(false);
                        break;
                    }

                    // Handle Ping frames - automatically respond with Pong
                    // This is critical for keeping the connection alive
                    if (result.MessageType == WebSocketMessageType.Binary && result.Count == 0)
                    {
                        // Some servers send ping as empty binary frames
                        // ClientWebSocket automatically handles ping/pong, but log it for debugging
                        _logger.LogEvent("CloudWebSocket", "Received ping (auto-pong sent)", Serilog.Events.LogEventLevel.Debug);
                        continue;
                    }

                    if (result.EndOfMessage && messageOffset == 0)
                    {
                        var message = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
                        _logger.LogEvent("CloudWebSocket", $"Dispatching single-frame message: {message.Substring(0, Math.Min(100, message.Length))}...", Serilog.Events.LogEventLevel.Debug);
                        await DispatchMessageAsync(message, token).ConfigureAwait(false);
                        continue;
                    }

                    messageBuffer ??= new byte[Math.Max(ReceiveBufferSize * 4, result.Count)];
                    EnsureBufferCapacity(ref messageBuffer, messageOffset + result.Count, messageOffset);
                    Buffer.BlockCopy(receiveBuffer, 0, messageBuffer, messageOffset, result.Count);
                    messageOffset += result.Count;

                    if (result.EndOfMessage)
                    {
                        var message = Encoding.UTF8.GetString(messageBuffer, 0, messageOffset);
                        messageOffset = 0;
                        _logger.LogEvent("CloudWebSocket", $"Dispatching multi-frame message: {message.Substring(0, Math.Min(100, message.Length))}...", Serilog.Events.LogEventLevel.Debug);
                        await DispatchMessageAsync(message, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogEvent("CloudWebSocketReceive", () => ex.Message, Serilog.Events.LogEventLevel.Error);
            }
            finally
            {
                if (messageBuffer != null)
                {
                    Array.Clear(messageBuffer, 0, messageBuffer.Length);
                }
            }
        }

        private async ValueTask DispatchMessageAsync(string rawMessage, CancellationToken token)
        {
            await _messageChannel.Writer.WriteAsync(rawMessage, token).ConfigureAwait(false);
            var messageNumber = Interlocked.Increment(ref _receivedMessages);

            var debugLoggingEnabled = _logger.IsEnabled(Serilog.Events.LogEventLevel.Debug);
            if (debugLoggingEnabled && ShouldLogSample(messageNumber))
            {
                _logger.LogEvent(
                    "CloudWebSocket",
                    () => $"Received message #{messageNumber}: {Truncate(rawMessage, MaxMessagePreviewLength)}",
                    Serilog.Events.LogEventLevel.Debug
                );
            }
        }

        private async Task ProcessMessagesAsync(CancellationToken token)
        {
            try
            {
                long dispatched = 0;
                var debugLoggingEnabled = _logger.IsEnabled(Serilog.Events.LogEventLevel.Debug);
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                await foreach (var rawMsg in _messageChannel.Reader.ReadAllAsync(token))
                {
                    try
                    {
                        if (debugLoggingEnabled && ShouldLogSample(dispatched + 1))
                        {
                            _logger.LogEvent("CloudWebSocket", () => $"Raw message received: {Truncate(rawMsg, MaxMessagePreviewLength)}", Serilog.Events.LogEventLevel.Debug);
                        }
                        
                        var msg = JsonSerializer.Deserialize<CloudMessage>(rawMsg, options);
                        if (msg == null) continue;

                        if (debugLoggingEnabled && ShouldLogSample(dispatched + 1))
                        {
                            _logger.LogEvent("CloudWebSocket", 
                                $"Deserialized - Id: {msg.Id}, Method: {msg.Method}, Result: {msg.Result != null}, Error: {msg.Error != null}", 
                                Serilog.Events.LogEventLevel.Debug);
                        }

                        // Determine message type based on content
                        // Backend sends: { id, rpc, result } for responses
                        // Backend sends: { stream, data, timestamp } for streams
                        // Backend sends: { id, error } for errors
                        
                        if (msg.Error != null)
                        {
                            // Error message
                            msg.Type = "error";
                            msg.Status = "error";
                            _logger.LogEvent("CloudWebSocket", $"Error message: {msg.Error?.Message}", Serilog.Events.LogEventLevel.Warning);
                            
                            // If this is a response to a request, fail the pending task
                            if (!string.IsNullOrEmpty(msg.Id) && _pendingRequests.TryRemove(msg.Id, out var errorTcs))
                            {
                                errorTcs.TrySetResult(msg);
                            }
                        }
                        else if (!string.IsNullOrEmpty(msg.Event) || msg.Data != null)
                        {
                            // Stream message
                            msg.Type = "stream";
                            MessageReceived?.Invoke(this, msg);
                            _bus.Publish(new CloudMessageReceived(msg));
                        }
                        else if (!string.IsNullOrEmpty(msg.Id))
                        {
                            // Response message - any message bearing an Id without an explicit
                            // stream/error envelope is treated as a response so empty/void RPC
                            // results don't leak as orphaned pending requests.
                            msg.Type = "response";
                            msg.Status = "success";

                            if (debugLoggingEnabled && ShouldLogSample(dispatched + 1))
                            {
                                _logger.LogEvent("CloudWebSocket",
                                    $"Response message detected - Id: {msg.Id}, Method: {msg.Method}, HasResult: {msg.Result != null}, Pending requests: {_pendingRequests.Count}",
                                    Serilog.Events.LogEventLevel.Debug);
                            }

                            if (_pendingRequests.TryRemove(msg.Id, out var tcs))
                            {
                                if (debugLoggingEnabled && ShouldLogSample(dispatched + 1))
                                {
                                    _logger.LogEvent("CloudWebSocket",
                                        $"Completing pending request {msg.Id}",
                                        Serilog.Events.LogEventLevel.Debug);
                                }

                                tcs.TrySetResult(msg);
                            }
                            else
                            {
                                _logger.LogEvent("CloudWebSocket",
                                    $"No pending request found for Id: {msg.Id}",
                                    Serilog.Events.LogEventLevel.Warning);
                            }
                        }

                        dispatched++;
                        if (debugLoggingEnabled && ShouldLogSample(dispatched))
                        {
                            _logger.LogEvent(
                                "CloudWebSocket",
                                () => $"Dispatched message #{dispatched}",
                                Serilog.Events.LogEventLevel.Debug
                            );
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogEvent("CloudWebSocket", $"Failed to deserialize message: {ex.Message}", Serilog.Events.LogEventLevel.Error);
                        // Don't let malformed messages leave orphaned pending requests hanging
                    }
                    catch (Exception ex)
                    {
                        _logger.LogEvent("CloudWebSocket", $"Unexpected error processing message: {ex.GetType().Name} - {ex.Message}", Serilog.Events.LogEventLevel.Error);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _logger.LogEvent("CloudWebSocket", "ProcessMessagesAsync completed.");
            }
        }

        /// <summary>
        /// Properly clean up all pending requests when stopping
        /// </summary>
        private void CancelAllPendingRequests(Exception? reason = null)
        {
            var pendingRequests = _pendingRequests.ToArray();
            foreach (var kvp in pendingRequests)
            {
                if (_pendingRequests.TryRemove(kvp.Key, out var tcs))
                {
                    if (!tcs.Task.IsCompleted)
                    {
                        if (reason != null)
                            tcs.SetException(reason);
                        else
                            tcs.SetCanceled();
                    }
                }
            }
        }

        public async Task StopAsync()
        {
            _logger.LogEvent("CloudWebSocket", "StopAsync invoked.");

            var cts = Interlocked.Exchange(ref _cts, null);
            Task? processingTask = Interlocked.Exchange(ref _processingTask, null);

            try
            {
                try
                {
                    cts?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                Exception? processingError = null;
                if (processingTask != null)
                {
                    try
                    {
                        await processingTask.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        processingError = ex;
                        _logger.LogEvent("CloudWebSocket", () => $"Stop processing error: {ex.Message}", Serilog.Events.LogEventLevel.Error);
                    }
                }

                while (_messageChannel.Reader.TryRead(out _))
                {
                }
                
                // Cancel all pending requests to prevent hanging
                CancelAllPendingRequests(new OperationCanceledException("WebSocket client stopped"));

                if (processingError != null)
                {
                    throw processingError;
                }
            }
            finally
            {
                _logger.LogEvent("CloudWebSocket", "StopAsync cleaning up resources.");
                cts?.Dispose();
                
                // BUG FIX #9 (MEDIUM): Use lock to prevent concurrent Dispose() race condition.
                // Previously: Both Dispose() and StopAsync() could dispose _socket without synchronization,
                // causing double-dispose or ObjectDisposedException. Now: Use _socketLock for atomicity.
                lock (_socketLock)
                {
                    _socket?.Dispose();
                    _socket = null;
                }
            }
        }

        // BUG FIX #9 (MEDIUM): Use lock around socket disposal to prevent race with StopAsync().
        public void Dispose()
        {
            // Avoid blocking the thread - schedule async cleanup
            try
            {
                var cts = Interlocked.Exchange(ref _cts, null);
                if (cts != null)
                {
                    try
                    {
                        cts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
                
                // Don't block on async operations - just release resources synchronously
                // Use lock to prevent concurrent disposal with StopAsync()
                lock (_socketLock)
                {
                    _socket?.Dispose();
                    _socket = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogEvent("CloudWebSocket", () => $"Dispose error: {ex.Message}", Serilog.Events.LogEventLevel.Error);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
        }

        private void EnsureProcessingTask(CancellationToken token)
        {
            lock (_processingTaskSync)
            {
                var existing = _processingTask;
                if (existing == null || existing.IsCompleted)
                {
                    _processingTask = Task.Run(() => ProcessMessagesAsync(token), token);
                }
            }
        }

        private static void EnsureBufferCapacity(ref byte[] buffer, int requiredLength, int preservedLength)
        {
            if (buffer.Length >= requiredLength)
            {
                return;
            }

            var newBuffer = new byte[Math.Max(buffer.Length * 2, requiredLength)];
            if (preservedLength > 0)
            {
                Buffer.BlockCopy(buffer, 0, newBuffer, 0, preservedLength);
            }

            buffer = newBuffer;
        }

        private static bool ShouldLogSample(long count)
        {
            return count <= 5 || count % 50 == 0;
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength) + "…";
        }

        #endregion
    }

    /// <summary>
    /// Event for broadcasting cloud messages
    /// </summary>
    public class CloudMessageReceived
    {
        public CloudMessage Message { get; set; }

        public CloudMessageReceived(CloudMessage message)
        {
            Message = message;
        }
    }
}

