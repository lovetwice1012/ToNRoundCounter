using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
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
        public string current_item { get; set; } = "None";
        public bool is_dead { get; set; }
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
        private const int MaxReconnectDelayMs = 30000; // 30 seconds
        private const int InitialReconnectDelayMs = 1000; // 1 second
        private const double BackoffMultiplier = 1.5;

        private Uri _uri;
        private ClientWebSocket? _socket;
        private CancellationTokenSource? _cts;
        private readonly IEventBus _bus;
        private readonly ICancellationProvider _cancellationProvider;
        private readonly IEventLogger _logger;
        private readonly Channel<string> _messageChannel;
        private Task? _processingTask;
        private int _connectionAttempts;
        private int _consecutiveFailures;
        private long _receivedMessages;
        private readonly Dictionary<string, TaskCompletionSource<CloudMessage>> _pendingRequests =
            new Dictionary<string, TaskCompletionSource<CloudMessage>>();
        private readonly object _pendingRequestsLock = new object();
        private string _sessionId = DEFAULT_SESSION_ID;
        private string? _userId;

        public event EventHandler<CloudMessage>? MessageReceived;
        public event EventHandler? Connected;
        public event EventHandler? Disconnected;
        public event EventHandler<Exception>? ErrorOccurred;

        public string SessionId => _sessionId;
        public string? UserId => _userId;
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
            IEventLogger logger)
        {
            _uri = new Uri(url);
            _bus = bus;
            _cancellationProvider = cancellation;
            _logger = logger;
            _messageChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });
        }

        public async Task StartAsync()
        {
            _logger.LogEvent("CloudWebSocket", () => $"Starting client for {_uri}.");
            _connectionAttempts = 0;
            _consecutiveFailures = 0;
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

                    try
                    {
                        _connectionAttempts++;
                        _logger.LogEvent("CloudWebSocket", () => $"Attempt {_connectionAttempts}: connecting to {_uri}.");

                        await _socket.ConnectAsync(_uri, token).ConfigureAwait(false);
                        _logger.LogEvent("CloudWebSocket", "Connection established.");

                        // Authenticate
                        await AuthenticateAsync(token).ConfigureAwait(false);

                        _consecutiveFailures = 0; // Reset on successful connection
                        Connected?.Invoke(this, EventArgs.Empty);
                        await ReceiveLoopAsync(token).ConfigureAwait(false);

                        _logger.LogEvent("CloudWebSocket", "Receive loop completed.");
                        Disconnected?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        _consecutiveFailures++;
                        _logger.LogEvent("CloudWebSocket", () => ex.Message, Serilog.Events.LogEventLevel.Error);
                        ErrorOccurred?.Invoke(this, ex);

                        if (!token.IsCancellationRequested)
                        {
                            var delayMs = CalculateBackoffDelay(_consecutiveFailures);
                            _logger.LogEvent("CloudWebSocket", $"Scheduling reconnect in {delayMs}ms (failure #{_consecutiveFailures}).");
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

        private int CalculateBackoffDelay(int failureCount)
        {
            // Exponential backoff: initialDelay * (multiplier ^ (failureCount - 1))
            // Capped at MaxReconnectDelayMs
            var delay = InitialReconnectDelayMs * Math.Pow(BackoffMultiplier, failureCount - 1);
            return (int)Math.Min(delay, MaxReconnectDelayMs);
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
            if (response.Result == null)
            {
                throw new InvalidOperationException("Authentication succeeded but no result data received");
            }

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
        /// </summary>
        public async Task<CloudMessage> SendRequestAsync(CloudMessage request, CancellationToken cancellationToken = default)
        {
            if (_socket?.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("WebSocket is not connected");
            }

            var tcs = new TaskCompletionSource<CloudMessage>();
            lock (_pendingRequestsLock)
            {
                _pendingRequests[request.Id] = tcs;
            }

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null, // Use explicit JsonPropertyName attributes
                    IgnoreNullValues = true
                };
                var json = JsonSerializer.Serialize(request, options);
                var bytes = Encoding.UTF8.GetBytes(json);

                await _socket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken
                ).ConfigureAwait(false);

                // Set timeout
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                {
                    return await tcs.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                lock (_pendingRequestsLock)
                {
                    _pendingRequests.Remove(request.Id);
                }
            }
        }

        /// <summary>
        /// Helper: Call a game round start RPC
        /// </summary>
        public async Task<string> GameRoundStartAsync(
            string? instanceId,
            string playerName, 
            string roundType, 
            string? mapName = null, 
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "game.roundStart",
                Params = new 
                { 
                    instanceId = instanceId,  // Backend expects camelCase for instanceId
                    playerName = playerName,
                    roundType = roundType,
                    mapName = mapName 
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to start round: {response.Error?.Message}");
            }

            if (response.Result == null)
            {
                throw new InvalidOperationException("Start round succeeded but no result data received");
            }

            var resultJson = JsonSerializer.Serialize(response.Result);
            using (var doc = JsonDocument.Parse(resultJson))
            {
                if (doc.RootElement.TryGetProperty("roundId", out var roundIdElem))
                {
                    return roundIdElem.GetString() ?? throw new InvalidOperationException("No roundId in response");
                }
            }

            throw new InvalidOperationException("Invalid response format");
        }

        /// <summary>
        /// Helper: Call a game round end RPC
        /// </summary>
        public async Task<Dictionary<string, object>> GameRoundEndAsync(
            string roundId,
            bool survived,
            int duration,
            double? damageDealt = null,
            string[]? itemsObtained = null,
            string? terrorName = null,
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "game.roundEnd",
                Params = new
                {
                    roundId = roundId,
                    survived = survived,
                    duration = duration,
                    damageDealt = damageDealt,
                    itemsObtained = itemsObtained,
                    terrorName = terrorName
                }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to end round: {response.Error?.Message}");
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

        #region Instance Management APIs

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
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "instance.update",
                Params = new
                {
                    instance_id = instanceId,
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
                        velocity,
                        afk_duration = afkDuration,
                        items = items ?? new List<string>(),
                        damage,
                        is_alive = isAlive
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
                Method = "player.state.getAll",
                Params = new { instance_id = instanceId }
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
                    if (doc.RootElement.TryGetProperty("states", out var statesElem) &&
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
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "monitoring.status",
                Params = new
                {
                    user_id = userId,
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
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "monitoring.errors",
                Params = new
                {
                    user_id = userId,
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
                    expires_at = expiresAt.ToString("O")
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
            string decision, // "Proceed" or "Cancel"
            CancellationToken cancellationToken = default)
        {
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "coordinated.voting.vote",
                Params = new
                {
                    campaign_id = campaignId,
                    player_id = playerId,
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
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "wished.terrors.update",
                Params = new
                {
                    player_id = playerId,
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
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "wished.terrors.get",
                Params = new { player_id = playerId }
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
                        foreach (var terror in terrors.EnumerateArray())
                        {
                            var value = terror.GetString();
                            if (value != null)
                            {
                                result.Add(value);
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
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "profile.get",
                Params = new { player_id = playerId }
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
            var updateData = new Dictionary<string, object>
            {
                ["player_id"] = playerId
            };

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
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "settings.get",
                Params = new { user_id = userId }
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
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "settings.update",
                Params = new
                {
                    user_id = userId,
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
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "settings.sync",
                Params = new
                {
                    user_id = userId,
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
                    start = startTime?.ToString("O"),
                    end = endTime?.ToString("O")
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
                    start = startTime?.ToString("O"),
                    end = endTime?.ToString("O")
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
            var request = new CloudMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = "request",
                Method = "backup.list",
                Params = new { user_id = userId }
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
                Params = new { backup_id = backupId }
            };

            var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Status == "error")
            {
                throw new InvalidOperationException($"Failed to delete backup: {response.Error?.Message}");
            }
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
                return;
            }

            var receiveBuffer = new byte[ReceiveBufferSize];
            byte[]? messageBuffer = null;
            int messageOffset = 0;

            try
            {
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var segment = new ArraySegment<byte>(receiveBuffer);
                    WebSocketReceiveResult result;

                    try
                    {
                        result = await socket.ReceiveAsync(segment, token).ConfigureAwait(false);
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

                    if (result.EndOfMessage && messageOffset == 0)
                    {
                        var message = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
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
                    IgnoreNullValues = true
                };

                await foreach (var rawMsg in _messageChannel.Reader.ReadAllAsync(token))
                {
                    try
                    {
                        var msg = JsonSerializer.Deserialize<CloudMessage>(rawMsg, options);
                        if (msg == null) continue;

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
                            TaskCompletionSource<CloudMessage>? errorTcs = null;
                            lock (_pendingRequestsLock)
                            {
                                if (!string.IsNullOrEmpty(msg.Id) && _pendingRequests.TryGetValue(msg.Id, out errorTcs))
                                {
                                    _pendingRequests.Remove(msg.Id);
                                }
                            }

                            if (errorTcs != null)
                            {
                                errorTcs.SetResult(msg);
                            }
                        }
                        else if (!string.IsNullOrEmpty(msg.Event) || msg.Data != null)
                        {
                            // Stream message
                            msg.Type = "stream";
                            MessageReceived?.Invoke(this, msg);
                            _bus.Publish(new CloudMessageReceived(msg));
                        }
                        else if (!string.IsNullOrEmpty(msg.Id) && (msg.Result != null || !string.IsNullOrEmpty(msg.Method)))
                        {
                            // Response message
                            msg.Type = "response";
                            msg.Status = "success";
                            
                            TaskCompletionSource<CloudMessage>? tcs = null;
                            lock (_pendingRequestsLock)
                            {
                                if (_pendingRequests.TryGetValue(msg.Id, out tcs))
                                {
                                    _pendingRequests.Remove(msg.Id);
                                }
                            }

                            if (tcs != null)
                            {
                                tcs.SetResult(msg);
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

                if (processingError != null)
                {
                    throw processingError;
                }
            }
            finally
            {
                _logger.LogEvent("CloudWebSocket", "StopAsync cleaning up resources.");
                cts?.Dispose();
                _socket?.Dispose();
                _socket = null;
            }
        }

        public void Dispose()
        {
            try
            {
                // Note: Prefer DisposeAsync() when possible. Synchronous disposal blocks on async cleanup.
                StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
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
            var existing = _processingTask;
            if (existing == null || existing.IsCompleted)
            {
                _processingTask = Task.Run(() => ProcessMessagesAsync(token), token);
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

