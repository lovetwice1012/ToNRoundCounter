/**
 * Protocol ZERO Cloud Client
 * Hybrid binary protocol: Binary headers + MessagePack payloads
 *
 * Enterprise-grade implementation with full error handling,
 * thread safety, and resource management.
 */

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using Serilog;

namespace ToNRoundCounter.Infrastructure.Cloud
{
    /// <summary>
    /// Opcodes for Protocol ZERO
    /// </summary>
    public enum Opcode : byte
    {
        // Auth
        Login = 0x01,
        Logout = 0x02,
        RefreshSession = 0x03,

        // Round
        RoundStart = 0x10,
        RoundEnd = 0x11,

        // Instance
        InstanceCreate = 0x20,
        InstanceList = 0x21,
        InstanceGet = 0x22,
        InstanceUpdate = 0x23,
        InstanceDelete = 0x24,
        InstanceAlert = 0x25,

        // Player State
        UpdatePlayerState = 0x30,
        GetPlayerState = 0x31,
        GetAllPlayerStates = 0x32,

        // Threat
        AnnounceThreat = 0x40,
        RecordThreatResponse = 0x41,
        FindDesirePlayers = 0x42,

        // Voting
        StartVoting = 0x50,
        SubmitVote = 0x51,
        GetVotingCampaign = 0x52,

        // Profile
        GetProfile = 0x60,
        UpdateProfile = 0x61,

        // Settings
        GetSettings = 0x70,
        UpdateSettings = 0x71,
        SyncSettings = 0x72,

        // Monitoring
        ReportMonitoringStatus = 0x80,
        GetMonitoringHistory = 0x81,
        GetMonitoringErrors = 0x82,
        LogError = 0x83,

        // Analytics
        GetPlayerAnalytics = 0x90,
        GetTerrorAnalytics = 0x91,
        GetInstanceAnalytics = 0x92,
        GetVotingAnalytics = 0x93,
        ExportAnalytics = 0x94,

        // Backup
        CreateBackup = 0xA0,
        RestoreBackup = 0xA1,
        ListBackups = 0xA2,

        // Wished Terrors
        UpdateWishedTerrors = 0xB0,
        GetWishedTerrors = 0xB1,

        // Response
        Success = 0xC0,
        Error = 0xFF,

        // Stream Events
        PlayerStateEvent = 0xD0,
        RoundStartedEvent = 0xD1,
        RoundEndedEvent = 0xD2,
        VotingStartedEvent = 0xD3,
        VotingUpdatedEvent = 0xD4,
        VotingResolvedEvent = 0xD5,
        SettingsUpdatedEvent = 0xD6,

        // Control
        Ping = 0xFE,
        Pong = 0xFD,
    }

    /// <summary>
    /// Ultra-minimal cloud client (Protocol ZERO with MessagePack payloads)
    /// Thread-safe, enterprise-grade implementation.
    /// </summary>
    public sealed class CloudClientZero : IDisposable
    {
        // Constants
        private const int DefaultReceiveBufferSize = 65536; // 64KB
        private const int DefaultRequestTimeoutMs = 30000; // 30 seconds
        private const int DefaultStopTimeoutMs = 5000; // 5 seconds
        private const byte FireAndForgetRequestId = 0xFF;
        private const byte MinValidRequestId = 1;
        private const int MaxPayloadSize = 10 * 1024 * 1024; // 10MB

        // Connection state
        private Uri _uri;
        private ClientWebSocket? _ws;
        private readonly byte[] _receiveBuffer = new byte[DefaultReceiveBufferSize];
        private readonly ConcurrentDictionary<byte, TaskCompletionSource<byte[]>> _pending = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private CancellationTokenSource? _cts;
        private byte _nextReqId = MinValidRequestId;
        private readonly object _reqIdLock = new();
        private bool _disposed;
        private volatile bool _isConnected;

        /// <summary>
        /// Gets the current session ID
        /// </summary>
        public string? SessionId { get; private set; }

        /// <summary>
        /// Gets whether the client is connected
        /// </summary>
        public bool IsConnected => _isConnected && _ws?.State == WebSocketState.Open;

        /// <summary>
        /// Initializes a new instance of CloudClientZero
        /// </summary>
        /// <param name="url">WebSocket endpoint URL</param>
        /// <exception cref="ArgumentNullException">Thrown when url is null</exception>
        /// <exception cref="UriFormatException">Thrown when url is invalid</exception>
        public CloudClientZero(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentNullException(nameof(url));

            _uri = new Uri(url);
        }

        /// <summary>
        /// Update endpoint URL
        /// </summary>
        /// <param name="url">New WebSocket endpoint URL</param>
        /// <exception cref="ArgumentNullException">Thrown when url is null</exception>
        /// <exception cref="UriFormatException">Thrown when url is invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public void UpdateEndpoint(string url)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentNullException(nameof(url));

            _uri = new Uri(url);
        }

        /// <summary>
        /// Start connection (compatibility method)
        /// </summary>
        public async Task StartAsync()
        {
            await ConnectAsync();
        }

        /// <summary>
        /// Stop connection gracefully
        /// </summary>
        public async Task StopAsync()
        {
            if (_disposed)
                return;

            // Cancel receive loop
            _cts?.Cancel();

            // Wait briefly for graceful shutdown
            await Task.Delay(100).ConfigureAwait(false);

            // Close WebSocket with timeout
            if (_ws?.State == WebSocketState.Open || _ws?.State == WebSocketState.Connecting)
            {
                try
                {
                    using var cts = new CancellationTokenSource(DefaultStopTimeoutMs);
                    await _ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Client stopping",
                        cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error closing WebSocket");
                }
            }

            // Cancel all pending requests
            foreach (var kvp in _pending)
            {
                kvp.Value.TrySetCanceled();
            }
            _pending.Clear();

            _isConnected = false;

            Dispose();
        }

        /// <summary>
        /// Connect to server
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task ConnectAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            // Clean up existing resources
            _ws?.Dispose();
            _cts?.Dispose();
            _pending.Clear();

            _ws = new ClientWebSocket();

            try
            {
                await _ws.ConnectAsync(_uri, ct).ConfigureAwait(false);
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _isConnected = true;

                // Start receive loop in background
                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);

                Log.Information("Connected to {Uri}", _uri);
            }
            catch (Exception ex)
            {
                _isConnected = false;
                Log.Error(ex, "Failed to connect to {Uri}", _uri);
                throw;
            }
        }

        // ====================================================================
        // Auth APIs
        // ====================================================================

        /// <summary>
        /// Login to the server
        /// </summary>
        /// <param name="playerId">Player identifier</param>
        /// <param name="version">Client version</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Login response containing session_id, expires_at, etc.</returns>
        /// <exception cref="ArgumentException">Thrown when playerId or version is invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> LoginAsync(
            string playerId,
            string version,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(playerId, nameof(playerId));
            ValidateStringParameter(version, nameof(version));

            var result = await SendMessagePackRequestAsync(Opcode.Login, new
            {
                player_id = playerId,
                version = version
            }, cancellationToken).ConfigureAwait(false);

            if (result.TryGetValue("session_id", out var sid))
                SessionId = sid?.ToString();

            return result;
        }

        /// <summary>
        /// Logout from the server
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await SendMessagePackRequestAsync(Opcode.Logout, new { }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Refresh the current session
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Refresh response containing new expires_at</returns>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> RefreshSessionAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return await SendMessagePackRequestAsync(Opcode.RefreshSession, new { }, cancellationToken).ConfigureAwait(false);
        }

        // ====================================================================
        // Round APIs
        // ====================================================================

        /// <summary>
        /// Start a game round
        /// </summary>
        /// <param name="instanceId">Instance identifier (optional)</param>
        /// <param name="playerName">Player name (optional)</param>
        /// <param name="roundType">Round type</param>
        /// <param name="mapName">Map name (optional)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Round ID</returns>
        /// <exception cref="ArgumentException">Thrown when roundType is invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<string> GameRoundStartAsync(
            string? instanceId,
            string? playerName,
            string roundType,
            string? mapName = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(roundType, nameof(roundType));

            var result = await SendMessagePackRequestAsync(Opcode.RoundStart, new
            {
                instance_id = instanceId ?? "default",
                player_name = playerName,
                round_type = roundType,
                map_name = mapName
            }, cancellationToken).ConfigureAwait(false);

            return result.TryGetValue("round_id", out var roundId)
                ? roundId?.ToString() ?? string.Empty
                : string.Empty;
        }

        /// <summary>
        /// End a game round
        /// </summary>
        /// <param name="roundId">Round identifier</param>
        /// <param name="survived">Whether player survived</param>
        /// <param name="roundDuration">Round duration in seconds</param>
        /// <param name="damageDealt">Total damage dealt</param>
        /// <param name="itemsObtained">Items obtained during round</param>
        /// <param name="terrorName">Terror name (optional)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Round end response</returns>
        /// <exception cref="ArgumentException">Thrown when roundId is invalid</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when roundDuration or damageDealt is negative</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> GameRoundEndAsync(
            string roundId,
            bool survived,
            int roundDuration,
            double damageDealt,
            string[]? itemsObtained,
            string? terrorName = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(roundId, nameof(roundId));
            if (roundDuration < 0)
                throw new ArgumentOutOfRangeException(nameof(roundDuration), "Round duration cannot be negative");
            if (damageDealt < 0)
                throw new ArgumentOutOfRangeException(nameof(damageDealt), "Damage dealt cannot be negative");

            return await SendMessagePackRequestAsync(Opcode.RoundEnd, new
            {
                round_id = roundId,
                survived = survived,
                duration = roundDuration,
                damage_dealt = damageDealt,
                items_obtained = itemsObtained?.ToList() ?? new List<string>(),
                terror_name = terrorName
            }, cancellationToken).ConfigureAwait(false);
        }

        // ====================================================================
        // Instance APIs
        // ====================================================================

        /// <summary>
        /// Create a new instance
        /// </summary>
        /// <param name="maxPlayers">Maximum number of players</param>
        /// <param name="settings">Instance settings</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Instance creation response</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when maxPlayers is invalid</exception>
        /// <exception cref="ArgumentNullException">Thrown when settings is null</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> InstanceCreateAsync(
            int maxPlayers,
            Dictionary<string, object> settings,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (maxPlayers <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPlayers), "Max players must be positive");
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            return await SendMessagePackRequestAsync(Opcode.InstanceCreate, new
            {
                max_players = maxPlayers,
                settings = settings
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// List instances
        /// </summary>
        /// <param name="filter">Optional filter string</param>
        /// <param name="limit">Maximum number of results</param>
        /// <param name="offset">Result offset for pagination</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of instances</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when limit or offset is negative</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<List<Dictionary<string, object>>> InstanceListAsync(
            string? filter = null,
            int limit = 20,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (limit < 0)
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit cannot be negative");
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative");

            var result = await SendMessagePackRequestAsync(Opcode.InstanceList, new
            {
                filter = filter,
                limit = limit,
                offset = offset
            }, cancellationToken).ConfigureAwait(false);

            if (result.TryGetValue("instances", out var instances) && instances is IEnumerable<object> list)
            {
                return list
                    .OfType<Dictionary<string, object>>()
                    .ToList();
            }

            return new List<Dictionary<string, object>>();
        }

        /// <summary>
        /// Get instance details
        /// </summary>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Instance details</returns>
        /// <exception cref="ArgumentException">Thrown when instanceId is invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> InstanceGetAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(instanceId, nameof(instanceId));

            return await SendMessagePackRequestAsync(Opcode.InstanceGet, new
            {
                instance_id = instanceId
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Update instance settings
        /// </summary>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="updates">Settings to update</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="ArgumentException">Thrown when instanceId is invalid</exception>
        /// <exception cref="ArgumentNullException">Thrown when updates is null</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task InstanceUpdateAsync(
            string instanceId,
            Dictionary<string, object> updates,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(instanceId, nameof(instanceId));
            if (updates == null)
                throw new ArgumentNullException(nameof(updates));

            await SendMessagePackRequestAsync(Opcode.InstanceUpdate, new
            {
                instance_id = instanceId,
                updates = updates
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete an instance
        /// </summary>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="ArgumentException">Thrown when instanceId is invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task InstanceDeleteAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(instanceId, nameof(instanceId));

            await SendMessagePackRequestAsync(Opcode.InstanceDelete, new
            {
                instance_id = instanceId
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Send an alert to an instance
        /// </summary>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="alertType">Alert type</param>
        /// <param name="message">Alert message</param>
        /// <param name="metadata">Optional metadata</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Alert response</returns>
        /// <exception cref="ArgumentException">Thrown when parameters are invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> InstanceAlertAsync(
            string instanceId,
            string alertType,
            string message,
            Dictionary<string, object>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(instanceId, nameof(instanceId));
            ValidateStringParameter(alertType, nameof(alertType));
            ValidateStringParameter(message, nameof(message));

            return await SendMessagePackRequestAsync(Opcode.InstanceAlert, new
            {
                instance_id = instanceId,
                alert_type = alertType,
                message = message,
                metadata = metadata ?? new Dictionary<string, object>()
            }, cancellationToken).ConfigureAwait(false);
        }

        // ====================================================================
        // Player State APIs
        // ====================================================================

        /// <summary>
        /// Update player state (fire-and-forget)
        /// </summary>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="playerId">Player identifier</param>
        /// <param name="velocity">Current velocity</param>
        /// <param name="afkDuration">AFK duration in seconds</param>
        /// <param name="items">Current items</param>
        /// <param name="damage">Total damage</param>
        /// <param name="isAlive">Whether player is alive</param>
        /// <param name="ct">Cancellation token</param>
        /// <exception cref="ArgumentException">Thrown when parameters are invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task UpdatePlayerStateAsync(
            string instanceId,
            string playerId,
            float velocity,
            float afkDuration,
            List<string> items,
            double damage,
            bool isAlive,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(instanceId, nameof(instanceId));
            ValidateStringParameter(playerId, nameof(playerId));
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            // Fire-and-forget
            await SendFireAndForgetAsync(Opcode.UpdatePlayerState, MessagePackSerializer.Serialize(new
            {
                instance_id = instanceId,
                player_id = playerId,
                velocity = velocity,
                afk_duration = afkDuration,
                items = items,
                damage = damage,
                is_alive = isAlive
            }), ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Get player state
        /// </summary>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="playerId">Player identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Player state</returns>
        /// <exception cref="ArgumentException">Thrown when parameters are invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> GetPlayerStateAsync(
            string instanceId,
            string playerId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(instanceId, nameof(instanceId));
            ValidateStringParameter(playerId, nameof(playerId));

            return await SendMessagePackRequestAsync(Opcode.GetPlayerState, new
            {
                instance_id = instanceId,
                player_id = playerId
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Get all player states in an instance
        /// </summary>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of player states</returns>
        /// <exception cref="ArgumentException">Thrown when instanceId is invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<List<Dictionary<string, object>>> GetAllPlayerStatesAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(instanceId, nameof(instanceId));

            var result = await SendMessagePackRequestAsync(Opcode.GetAllPlayerStates, new
            {
                instance_id = instanceId
            }, cancellationToken).ConfigureAwait(false);

            if (result.TryGetValue("states", out var states) && states is IEnumerable<object> list)
            {
                return list
                    .OfType<Dictionary<string, object>>()
                    .ToList();
            }

            return new List<Dictionary<string, object>>();
        }

        /// <summary>
        /// Get player states with strongly typed conversion
        /// </summary>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of strongly-typed player states</returns>
        /// <exception cref="ArgumentException">Thrown when instanceId is invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<List<Infrastructure.PlayerStateInfo>> GetPlayerStatesAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(instanceId, nameof(instanceId));

            var states = await GetAllPlayerStatesAsync(instanceId, cancellationToken).ConfigureAwait(false);
            return states.Select(s => new Infrastructure.PlayerStateInfo
            {
                player_id = GetStringValue(s, "player_id", ""),
                player_name = GetStringValue(s, "player_name", ""),
                damage = GetDoubleValue(s, "damage", 0.0),
                current_item = GetStringValue(s, "current_item", "None"),
                is_dead = GetBoolValue(s, "is_dead", false),
                velocity = GetDoubleValue(s, "velocity", 0.0),
                afk_duration = GetDoubleValue(s, "afk_duration", 0.0)
            }).ToList();
        }

        // ====================================================================
        // Threat APIs
        // ====================================================================

        /// <summary>
        /// Announce a threat to the instance
        /// </summary>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="terrorKey">Terror key/identifier</param>
        /// <param name="roundType">Round type</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="ArgumentException">Thrown when parameters are invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task AnnounceThreatAsync(
            string instanceId,
            string terrorKey,
            string roundType,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(instanceId, nameof(instanceId));
            ValidateStringParameter(terrorKey, nameof(terrorKey));
            ValidateStringParameter(roundType, nameof(roundType));

            await SendMessagePackRequestAsync(Opcode.AnnounceThreat, new
            {
                instance_id = instanceId,
                terror_key = terrorKey,
                round_type = roundType
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Record threat response
        /// </summary>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="playerId">Player identifier</param>
        /// <param name="terrorName">Terror name</param>
        /// <param name="roundKey">Round key</param>
        /// <param name="response">Response type</param>
        /// <param name="responseTime">Response time in milliseconds</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="ArgumentException">Thrown when parameters are invalid</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when responseTime is negative</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task RecordThreatResponseAsync(
            string instanceId,
            string playerId,
            string terrorName,
            string roundKey,
            string response,
            double responseTime,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(instanceId, nameof(instanceId));
            ValidateStringParameter(playerId, nameof(playerId));
            ValidateStringParameter(terrorName, nameof(terrorName));
            ValidateStringParameter(roundKey, nameof(roundKey));
            ValidateStringParameter(response, nameof(response));
            if (responseTime < 0)
                throw new ArgumentOutOfRangeException(nameof(responseTime), "Response time cannot be negative");

            await SendMessagePackRequestAsync(Opcode.RecordThreatResponse, new
            {
                instance_id = instanceId,
                player_id = playerId,
                terror_name = terrorName,
                round_key = roundKey,
                response = response,
                response_time = responseTime
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Find desire players for a terror
        /// </summary>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="terrorKey">Terror key/identifier</param>
        /// <param name="roundType">Round type</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of desire players</returns>
        /// <exception cref="ArgumentException">Thrown when parameters are invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<List<Infrastructure.DesirePlayerInfo>> FindDesirePlayersAsync(
            string instanceId,
            string terrorKey,
            string roundType,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(instanceId, nameof(instanceId));
            ValidateStringParameter(terrorKey, nameof(terrorKey));
            ValidateStringParameter(roundType, nameof(roundType));

            var result = await SendMessagePackRequestAsync(Opcode.FindDesirePlayers, new
            {
                instance_id = instanceId,
                terror_key = terrorKey,
                round_type = roundType
            }, cancellationToken).ConfigureAwait(false);

            if (result.TryGetValue("players", out var players) && players is IEnumerable<object> list)
            {
                return list.OfType<Dictionary<string, object>>().Select(p => new Infrastructure.DesirePlayerInfo
                {
                    player_id = GetStringValue(p, "player_id", ""),
                    player_name = GetStringValue(p, "player_name", "")
                }).ToList();
            }

            return new List<Infrastructure.DesirePlayerInfo>();
        }

        // ====================================================================
        // Voting APIs
        // ====================================================================

        /// <summary>
        /// Start a voting campaign
        /// </summary>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="terrorName">Terror name</param>
        /// <param name="expiresAt">Expiration date/time</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Voting campaign details</returns>
        /// <exception cref="ArgumentException">Thrown when parameters are invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> StartVotingAsync(
            string instanceId,
            string terrorName,
            DateTime expiresAt,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(instanceId, nameof(instanceId));
            ValidateStringParameter(terrorName, nameof(terrorName));

            // Convert DateTime to Unix timestamp for protocol consistency
            var expiresAtUnix = new DateTimeOffset(expiresAt).ToUnixTimeMilliseconds();

            return await SendMessagePackRequestAsync(Opcode.StartVoting, new
            {
                instance_id = instanceId,
                terror_name = terrorName,
                expires_at = expiresAtUnix
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Submit a vote
        /// </summary>
        /// <param name="campaignId">Campaign identifier</param>
        /// <param name="playerId">Player identifier</param>
        /// <param name="decision">Vote decision</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="ArgumentException">Thrown when parameters are invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task SubmitVoteAsync(
            string campaignId,
            string playerId,
            string decision,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(campaignId, nameof(campaignId));
            ValidateStringParameter(playerId, nameof(playerId));
            ValidateStringParameter(decision, nameof(decision));

            await SendMessagePackRequestAsync(Opcode.SubmitVote, new
            {
                campaign_id = campaignId,
                player_id = playerId,
                decision = decision
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Get voting campaign details
        /// </summary>
        /// <param name="campaignId">Campaign identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Campaign details</returns>
        /// <exception cref="ArgumentException">Thrown when campaignId is invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> GetVotingCampaignAsync(
            string campaignId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(campaignId, nameof(campaignId));

            return await SendMessagePackRequestAsync(Opcode.GetVotingCampaign, new
            {
                campaign_id = campaignId
            }, cancellationToken).ConfigureAwait(false);
        }

        // ====================================================================
        // Profile APIs
        // ====================================================================

        /// <summary>
        /// Get player profile
        /// </summary>
        /// <param name="playerId">Player identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Player profile</returns>
        /// <exception cref="ArgumentException">Thrown when playerId is invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> GetProfileAsync(
            string playerId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(playerId, nameof(playerId));

            return await SendMessagePackRequestAsync(Opcode.GetProfile, new
            {
                player_id = playerId
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Update player profile
        /// </summary>
        /// <param name="playerId">Player identifier</param>
        /// <param name="playerName">Player name (optional)</param>
        /// <param name="skillLevel">Skill level (optional)</param>
        /// <param name="terrorStats">Terror statistics (optional)</param>
        /// <param name="totalRounds">Total rounds played (optional)</param>
        /// <param name="totalSurvived">Total rounds survived (optional)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Update response</returns>
        /// <exception cref="ArgumentException">Thrown when playerId is invalid</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when numeric values are negative</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> UpdateProfileAsync(
            string playerId,
            string? playerName = null,
            int? skillLevel = null,
            object? terrorStats = null,
            int? totalRounds = null,
            int? totalSurvived = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(playerId, nameof(playerId));
            if (skillLevel.HasValue && (skillLevel.Value < 1 || skillLevel.Value > 10))
                throw new ArgumentOutOfRangeException(nameof(skillLevel), "Skill level must be between 1 and 10");
            if (totalRounds.HasValue && totalRounds.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(totalRounds), "Total rounds cannot be negative");
            if (totalSurvived.HasValue && totalSurvived.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(totalSurvived), "Total survived cannot be negative");

            var updates = new Dictionary<string, object>();
            if (playerName != null) updates["player_name"] = playerName;
            if (skillLevel.HasValue) updates["skill_level"] = skillLevel.Value;
            if (terrorStats != null) updates["terror_stats"] = terrorStats;
            if (totalRounds.HasValue) updates["total_rounds"] = totalRounds.Value;
            if (totalSurvived.HasValue) updates["total_survived"] = totalSurvived.Value;

            return await SendMessagePackRequestAsync(Opcode.UpdateProfile, new
            {
                player_id = playerId,
                updates = updates
            }, cancellationToken).ConfigureAwait(false);
        }

        // ====================================================================
        // Settings APIs
        // ====================================================================

        /// <summary>
        /// Get user settings
        /// </summary>
        /// <param name="userId">User identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>User settings</returns>
        /// <exception cref="ArgumentException">Thrown when userId is invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> GetSettingsAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(userId, nameof(userId));

            return await SendMessagePackRequestAsync(Opcode.GetSettings, new
            {
                user_id = userId
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Update user settings
        /// </summary>
        /// <param name="userId">User identifier</param>
        /// <param name="settings">Settings dictionary</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Update response</returns>
        /// <exception cref="ArgumentException">Thrown when userId is invalid</exception>
        /// <exception cref="ArgumentNullException">Thrown when settings is null</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> UpdateSettingsAsync(
            string userId,
            Dictionary<string, object> settings,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(userId, nameof(userId));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            return await SendMessagePackRequestAsync(Opcode.UpdateSettings, new
            {
                user_id = userId,
                settings = settings
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sync settings with server
        /// </summary>
        /// <param name="userId">User identifier</param>
        /// <param name="localSettings">Local settings</param>
        /// <param name="localVersion">Local version number</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Sync result</returns>
        /// <exception cref="ArgumentException">Thrown when userId is invalid</exception>
        /// <exception cref="ArgumentNullException">Thrown when localSettings is null</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when localVersion is negative</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> SyncSettingsAsync(
            string userId,
            Dictionary<string, object> localSettings,
            int localVersion,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(userId, nameof(userId));
            if (localSettings == null)
                throw new ArgumentNullException(nameof(localSettings));
            if (localVersion < 0)
                throw new ArgumentOutOfRangeException(nameof(localVersion), "Local version cannot be negative");

            return await SendMessagePackRequestAsync(Opcode.SyncSettings, new
            {
                user_id = userId,
                local_settings = localSettings,
                local_version = localVersion
            }, cancellationToken).ConfigureAwait(false);
        }

        // ====================================================================
        // Monitoring APIs
        // ====================================================================

        /// <summary>
        /// Report monitoring status
        /// </summary>
        /// <param name="instanceId">Instance identifier (optional)</param>
        /// <param name="applicationStatus">Application status</param>
        /// <param name="applicationVersion">Application version</param>
        /// <param name="uptime">Uptime in seconds</param>
        /// <param name="memoryUsage">Memory usage in MB</param>
        /// <param name="cpuUsage">CPU usage percentage</param>
        /// <param name="oscStatus">OSC connection status</param>
        /// <param name="oscLatency">OSC latency in ms (optional)</param>
        /// <param name="vrchatStatus">VRChat connection status</param>
        /// <param name="vrchatWorldId">VRChat world ID (optional)</param>
        /// <param name="vrchatInstanceId">VRChat instance ID (optional)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="ArgumentException">Thrown when parameters are invalid</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when numeric values are negative</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task ReportMonitoringStatusAsync(
            string? instanceId,
            string applicationStatus,
            string applicationVersion,
            int uptime,
            double memoryUsage,
            double cpuUsage,
            string oscStatus,
            double? oscLatency,
            string vrchatStatus,
            string? vrchatWorldId,
            string? vrchatInstanceId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(applicationStatus, nameof(applicationStatus));
            ValidateStringParameter(applicationVersion, nameof(applicationVersion));
            ValidateStringParameter(oscStatus, nameof(oscStatus));
            ValidateStringParameter(vrchatStatus, nameof(vrchatStatus));
            if (uptime < 0)
                throw new ArgumentOutOfRangeException(nameof(uptime), "Uptime cannot be negative");
            if (memoryUsage < 0)
                throw new ArgumentOutOfRangeException(nameof(memoryUsage), "Memory usage cannot be negative");
            if (cpuUsage < 0 || cpuUsage > 100)
                throw new ArgumentOutOfRangeException(nameof(cpuUsage), "CPU usage must be between 0 and 100");
            if (oscLatency.HasValue && oscLatency.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(oscLatency), "OSC latency cannot be negative");

            await SendMessagePackRequestAsync(Opcode.ReportMonitoringStatus, new
            {
                instance_id = instanceId,
                application_status = applicationStatus,
                application_version = applicationVersion,
                uptime = uptime,
                memory_usage = memoryUsage,
                cpu_usage = cpuUsage,
                osc_status = oscStatus,
                osc_latency = oscLatency,
                vrchat_status = vrchatStatus,
                vrchat_world_id = vrchatWorldId,
                vrchat_instance_id = vrchatInstanceId
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Get monitoring status history
        /// </summary>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="startTime">Start time (Unix timestamp)</param>
        /// <param name="endTime">End time (Unix timestamp)</param>
        /// <param name="limit">Maximum number of results</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Status history</returns>
        /// <exception cref="ArgumentException">Thrown when instanceId is invalid</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when parameters are invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<List<Dictionary<string, object>>> GetMonitoringStatusHistoryAsync(
            string instanceId,
            long startTime,
            long endTime,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(instanceId, nameof(instanceId));
            if (startTime < 0)
                throw new ArgumentOutOfRangeException(nameof(startTime), "Start time cannot be negative");
            if (endTime < 0)
                throw new ArgumentOutOfRangeException(nameof(endTime), "End time cannot be negative");
            if (limit <= 0)
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive");

            var result = await SendMessagePackRequestAsync(Opcode.GetMonitoringHistory, new
            {
                instance_id = instanceId,
                start_time = startTime,
                end_time = endTime,
                limit = limit
            }, cancellationToken).ConfigureAwait(false);

            if (result.TryGetValue("history", out var history) && history is IEnumerable<object> list)
            {
                return list
                    .OfType<Dictionary<string, object>>()
                    .ToList();
            }

            return new List<Dictionary<string, object>>();
        }

        /// <summary>
        /// Get monitoring errors
        /// </summary>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="startTime">Start time (Unix timestamp)</param>
        /// <param name="endTime">End time (Unix timestamp)</param>
        /// <param name="limit">Maximum number of results</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Error list</returns>
        /// <exception cref="ArgumentException">Thrown when instanceId is invalid</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when parameters are invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<List<Dictionary<string, object>>> GetMonitoringErrorsAsync(
            string instanceId,
            long startTime,
            long endTime,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(instanceId, nameof(instanceId));
            if (startTime < 0)
                throw new ArgumentOutOfRangeException(nameof(startTime), "Start time cannot be negative");
            if (endTime < 0)
                throw new ArgumentOutOfRangeException(nameof(endTime), "End time cannot be negative");
            if (limit <= 0)
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive");

            var result = await SendMessagePackRequestAsync(Opcode.GetMonitoringErrors, new
            {
                instance_id = instanceId,
                start_time = startTime,
                end_time = endTime,
                limit = limit
            }, cancellationToken).ConfigureAwait(false);

            if (result.TryGetValue("errors", out var errors) && errors is IEnumerable<object> list)
            {
                return list
                    .OfType<Dictionary<string, object>>()
                    .ToList();
            }

            return new List<Dictionary<string, object>>();
        }

        /// <summary>
        /// Log an error
        /// </summary>
        /// <param name="source">Error source</param>
        /// <param name="message">Error message</param>
        /// <param name="stackTrace">Stack trace (optional)</param>
        /// <param name="metadata">Additional metadata (optional)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="ArgumentException">Thrown when parameters are invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task LogErrorAsync(
            string source,
            string message,
            string? stackTrace = null,
            Dictionary<string, object>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(source, nameof(source));
            ValidateStringParameter(message, nameof(message));

            await SendMessagePackRequestAsync(Opcode.LogError, new
            {
                source = source,
                message = message,
                stack_trace = stackTrace,
                metadata = metadata ?? new Dictionary<string, object>()
            }, cancellationToken).ConfigureAwait(false);
        }

        // ====================================================================
        // Analytics APIs
        // ====================================================================

        /// <summary>
        /// Get player analytics
        /// </summary>
        /// <param name="playerId">Player identifier</param>
        /// <param name="startTime">Start time (Unix timestamp)</param>
        /// <param name="endTime">End time (Unix timestamp)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Player analytics</returns>
        /// <exception cref="ArgumentException">Thrown when playerId is invalid</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when times are invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> GetPlayerAnalyticsAsync(
            string playerId,
            long startTime,
            long endTime,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(playerId, nameof(playerId));
            if (startTime < 0)
                throw new ArgumentOutOfRangeException(nameof(startTime), "Start time cannot be negative");
            if (endTime < 0)
                throw new ArgumentOutOfRangeException(nameof(endTime), "End time cannot be negative");

            return await SendMessagePackRequestAsync(Opcode.GetPlayerAnalytics, new
            {
                player_id = playerId,
                start_time = startTime,
                end_time = endTime
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Get terror analytics
        /// </summary>
        /// <param name="terrorName">Terror name</param>
        /// <param name="startTime">Start time (Unix timestamp)</param>
        /// <param name="endTime">End time (Unix timestamp)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Terror analytics</returns>
        /// <exception cref="ArgumentException">Thrown when terrorName is invalid</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when times are invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> GetTerrorAnalyticsAsync(
            string terrorName,
            long startTime,
            long endTime,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(terrorName, nameof(terrorName));
            if (startTime < 0)
                throw new ArgumentOutOfRangeException(nameof(startTime), "Start time cannot be negative");
            if (endTime < 0)
                throw new ArgumentOutOfRangeException(nameof(endTime), "End time cannot be negative");

            return await SendMessagePackRequestAsync(Opcode.GetTerrorAnalytics, new
            {
                terror_name = terrorName,
                start_time = startTime,
                end_time = endTime
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Get instance analytics
        /// </summary>
        /// <param name="instanceId">Instance identifier</param>
        /// <param name="startTime">Start time (Unix timestamp)</param>
        /// <param name="endTime">End time (Unix timestamp)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Instance analytics</returns>
        /// <exception cref="ArgumentException">Thrown when instanceId is invalid</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when times are invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> GetInstanceAnalyticsAsync(
            string instanceId,
            long startTime,
            long endTime,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(instanceId, nameof(instanceId));
            if (startTime < 0)
                throw new ArgumentOutOfRangeException(nameof(startTime), "Start time cannot be negative");
            if (endTime < 0)
                throw new ArgumentOutOfRangeException(nameof(endTime), "End time cannot be negative");

            return await SendMessagePackRequestAsync(Opcode.GetInstanceAnalytics, new
            {
                instance_id = instanceId,
                start_time = startTime,
                end_time = endTime
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Get voting analytics
        /// </summary>
        /// <param name="startTime">Start time (Unix timestamp)</param>
        /// <param name="endTime">End time (Unix timestamp)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Voting analytics</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when times are invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> GetVotingAnalyticsAsync(
            long startTime,
            long endTime,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (startTime < 0)
                throw new ArgumentOutOfRangeException(nameof(startTime), "Start time cannot be negative");
            if (endTime < 0)
                throw new ArgumentOutOfRangeException(nameof(endTime), "End time cannot be negative");

            return await SendMessagePackRequestAsync(Opcode.GetVotingAnalytics, new
            {
                start_time = startTime,
                end_time = endTime
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Export analytics data
        /// </summary>
        /// <param name="exportType">Export type (e.g., "csv", "json")</param>
        /// <param name="filters">Export filters</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Export result</returns>
        /// <exception cref="ArgumentException">Thrown when parameters are invalid</exception>
        /// <exception cref="ArgumentNullException">Thrown when filters is null</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> ExportAnalyticsAsync(
            string exportType,
            Dictionary<string, object> filters,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(exportType, nameof(exportType));
            if (filters == null)
                throw new ArgumentNullException(nameof(filters));

            return await SendMessagePackRequestAsync(Opcode.ExportAnalytics, new
            {
                export_type = exportType,
                filters = filters
            }, cancellationToken).ConfigureAwait(false);
        }

        // ====================================================================
        // Backup APIs
        // ====================================================================

        /// <summary>
        /// Create a backup
        /// </summary>
        /// <param name="backupType">Backup type (e.g., "FULL", "PARTIAL")</param>
        /// <param name="compress">Whether to compress the backup</param>
        /// <param name="encrypt">Whether to encrypt the backup</param>
        /// <param name="description">Backup description</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Backup creation result</returns>
        /// <exception cref="ArgumentException">Thrown when parameters are invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<Dictionary<string, object>> CreateBackupAsync(
            string backupType,
            bool compress,
            bool encrypt,
            string description,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(backupType, nameof(backupType));
            ValidateStringParameter(description, nameof(description));

            return await SendMessagePackRequestAsync(Opcode.CreateBackup, new
            {
                backup_type = backupType,
                compress = compress,
                encrypt = encrypt,
                description = description
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Restore from backup
        /// </summary>
        /// <param name="backupId">Backup identifier</param>
        /// <param name="validateBeforeRestore">Whether to validate before restore</param>
        /// <param name="createBackupBeforeRestore">Whether to create backup before restore</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="ArgumentException">Thrown when backupId is invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task RestoreBackupAsync(
            string backupId,
            bool validateBeforeRestore,
            bool createBackupBeforeRestore,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(backupId, nameof(backupId));

            await SendMessagePackRequestAsync(Opcode.RestoreBackup, new
            {
                backup_id = backupId,
                validate_before_restore = validateBeforeRestore,
                create_backup_before_restore = createBackupBeforeRestore
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// List available backups
        /// </summary>
        /// <param name="userId">User identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of backups</returns>
        /// <exception cref="ArgumentException">Thrown when userId is invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<List<Dictionary<string, object>>> ListBackupsAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(userId, nameof(userId));

            var result = await SendMessagePackRequestAsync(Opcode.ListBackups, new
            {
                user_id = userId
            }, cancellationToken).ConfigureAwait(false);

            if (result.TryGetValue("backups", out var backups) && backups is IEnumerable<object> list)
            {
                return list
                    .OfType<Dictionary<string, object>>()
                    .ToList();
            }

            return new List<Dictionary<string, object>>();
        }

        // ====================================================================
        // Wished Terrors APIs
        // ====================================================================

        /// <summary>
        /// Update wished terrors list
        /// </summary>
        /// <param name="playerId">Player identifier</param>
        /// <param name="terrorNames">List of terror names</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="ArgumentException">Thrown when playerId is invalid</exception>
        /// <exception cref="ArgumentNullException">Thrown when terrorNames is null</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task UpdateWishedTerrorsAsync(
            string playerId,
            List<string> terrorNames,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(playerId, nameof(playerId));
            if (terrorNames == null)
                throw new ArgumentNullException(nameof(terrorNames));

            await SendMessagePackRequestAsync(Opcode.UpdateWishedTerrors, new
            {
                player_id = playerId,
                terror_names = terrorNames
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Get wished terrors list
        /// </summary>
        /// <param name="playerId">Player identifier</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of wished terror names</returns>
        /// <exception cref="ArgumentException">Thrown when playerId is invalid</exception>
        /// <exception cref="ObjectDisposedException">Thrown when instance is disposed</exception>
        public async Task<List<string>> GetWishedTerrorsAsync(
            string playerId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateStringParameter(playerId, nameof(playerId));

            var result = await SendMessagePackRequestAsync(Opcode.GetWishedTerrors, new
            {
                player_id = playerId
            }, cancellationToken).ConfigureAwait(false);

            if (result.TryGetValue("terror_names", out var terrorNames) && terrorNames is IEnumerable<object> list)
            {
                return list
                    .OfType<string>()
                    .ToList();
            }

            return new List<string>();
        }

        // ====================================================================
        // Internal Helper Methods
        // ====================================================================

        /// <summary>
        /// Send MessagePack request and get MessagePack response
        /// </summary>
        private async Task<Dictionary<string, object>> SendMessagePackRequestAsync(
            Opcode opcode,
            object request,
            CancellationToken ct)
        {
            var payload = MessagePackSerializer.Serialize(request);

            if (payload.Length > MaxPayloadSize)
                throw new InvalidOperationException($"Payload size {payload.Length} exceeds maximum {MaxPayloadSize}");

            var response = await SendRequestAsync(opcode, payload, ct).ConfigureAwait(false);
            return MessagePackSerializer.Deserialize<Dictionary<string, object>>(response);
        }

        /// <summary>
        /// Send request and wait for response
        /// </summary>
        private async Task<byte[]> SendRequestAsync(Opcode opcode, byte[] payload, CancellationToken ct)
        {
            byte reqId;
            lock (_reqIdLock)
            {
                if (_nextReqId == FireAndForgetRequestId)
                    _nextReqId = MinValidRequestId;
                reqId = _nextReqId++;
            }

            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!_pending.TryAdd(reqId, tcs))
                throw new InvalidOperationException($"Request ID {reqId} already in use");

            try
            {
                await SendMessageAsync(opcode, reqId, payload, ct).ConfigureAwait(false);

                using var timeoutCts = new CancellationTokenSource(DefaultRequestTimeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                var registration = linkedCts.Token.Register(() =>
                {
                    tcs.TrySetCanceled();
                });

                await using (registration.ConfigureAwait(false))
                {
                    return await tcs.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                _pending.TryRemove(reqId, out _);
            }
        }

        /// <summary>
        /// Send fire-and-forget message (no response expected)
        /// </summary>
        private async Task SendFireAndForgetAsync(Opcode opcode, byte[] payload, CancellationToken ct)
        {
            await SendMessageAsync(opcode, FireAndForgetRequestId, payload, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Send raw message
        /// </summary>
        private async Task SendMessageAsync(Opcode opcode, byte reqId, byte[] payload, CancellationToken ct)
        {
            if (_ws?.State != WebSocketState.Open)
                throw new InvalidOperationException("Not connected");

            if (payload.Length > ushort.MaxValue)
                throw new ArgumentException($"Payload too large: {payload.Length} bytes (max {ushort.MaxValue})");

            var message = new byte[4 + payload.Length];
            message[0] = (byte)opcode;
            message[1] = reqId;
            message[2] = (byte)(payload.Length >> 8);
            message[3] = (byte)(payload.Length & 0xFF);
            payload.CopyTo(message, 4);

            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _ws.SendAsync(message, WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// Receive loop
        /// </summary>
        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var messageBuffer = new List<byte>(DefaultReceiveBufferSize);

            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(_receiveBuffer, ct).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _isConnected = false;
                        break;
                    }

                    messageBuffer.AddRange(new ArraySegment<byte>(_receiveBuffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        ProcessMessage(messageBuffer.ToArray());
                        messageBuffer.Clear();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException ex)
                {
                    Log.Error(ex, "WebSocket error in receive loop");
                    _isConnected = false;
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Unexpected error in receive loop");
                    _isConnected = false;
                    break;
                }
            }

            _isConnected = false;
            Log.Information("Receive loop ended");
        }

        /// <summary>
        /// Process received message
        /// </summary>
        private void ProcessMessage(byte[] data)
        {
            if (data.Length < 4)
            {
                Log.Warning("Message too short: {Length} bytes", data.Length);
                return;
            }

            var opcode = (Opcode)data[0];
            var reqId = data[1];
            var payloadLen = (data[2] << 8) | data[3];

            if (payloadLen > data.Length - 4)
            {
                Log.Warning("Incomplete message: expected {Expected} bytes, got {Actual}",
                    payloadLen, data.Length - 4);
                return;
            }

            if (payloadLen > MaxPayloadSize)
            {
                Log.Warning("Payload too large: {Size} bytes", payloadLen);
                return;
            }

            var payload = data.AsSpan(4, payloadLen).ToArray();

            if (opcode == Opcode.Success && _pending.TryGetValue(reqId, out var tcs))
            {
                tcs.TrySetResult(payload);
            }
            else if (opcode == Opcode.Error && _pending.TryGetValue(reqId, out tcs))
            {
                try
                {
                    var errorData = MessagePackSerializer.Deserialize<Dictionary<string, object>>(payload);
                    var errorMsg = GetStringValue(errorData, "message", "Unknown error");
                    var errorCode = GetStringValue(errorData, "code", "UNKNOWN");
                    tcs.TrySetException(new InvalidOperationException($"Server error [{errorCode}]: {errorMsg}"));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to parse error response");
                    tcs.TrySetException(new InvalidOperationException("Server error (failed to parse)"));
                }
            }
            else if (opcode == Opcode.Pong)
            {
                // Heartbeat response - no action needed
            }
            else if (reqId != FireAndForgetRequestId)
            {
                Log.Warning("Received response for unknown request ID: {ReqId}, opcode: {Opcode}", reqId, opcode);
            }
        }

        // ====================================================================
        // Validation Helpers
        // ====================================================================

        private static void ValidateStringParameter(string? value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"{paramName} cannot be null or empty", paramName);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CloudClientZero));
        }

        private static string GetStringValue(Dictionary<string, object> dict, string key, string defaultValue)
        {
            return dict.TryGetValue(key, out var value) ? value?.ToString() ?? defaultValue : defaultValue;
        }

        private static double GetDoubleValue(Dictionary<string, object> dict, string key, double defaultValue)
        {
            if (dict.TryGetValue(key, out var value))
            {
                try
                {
                    return Convert.ToDouble(value);
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        private static bool GetBoolValue(Dictionary<string, object> dict, string key, bool defaultValue)
        {
            if (dict.TryGetValue(key, out var value))
            {
                try
                {
                    return Convert.ToBoolean(value);
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }

        // ====================================================================
        // IDisposable Implementation
        // ====================================================================

        /// <summary>
        /// Dispose of resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _cts?.Cancel();
            _cts?.Dispose();
            _ws?.Dispose();
            _sendLock.Dispose();

            // Cancel all pending requests
            foreach (var kvp in _pending)
            {
                kvp.Value.TrySetCanceled();
            }
            _pending.Clear();

            GC.SuppressFinalize(this);
        }
    }
}
