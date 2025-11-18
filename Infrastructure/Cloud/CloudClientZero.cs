/**
 * Protocol ZERO Cloud Client
 * Hybrid binary protocol: Binary headers + MessagePack payloads
 *
 * Replaces 2383-line CloudWebSocketClient.cs
 */

using System;
using System.Buffers.Binary;
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
    /// </summary>
    public class CloudClientZero : IDisposable
    {
        private Uri _uri;
        private ClientWebSocket? _ws;
        private readonly byte[] _receiveBuffer = new byte[8192];
        private readonly Dictionary<byte, TaskCompletionSource<byte[]>> _pending = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private CancellationTokenSource? _cts;
        private byte _nextReqId = 1;

        public string? SessionId { get; private set; }
        public bool IsConnected => _ws?.State == WebSocketState.Open;

        // Event for backward compatibility (not used in Protocol ZERO)
        public event Action<Dictionary<string, object>>? MessageReceived;

        public CloudClientZero(string url)
        {
            _uri = new Uri(url);
        }

        /// <summary>
        /// Update endpoint URL
        /// </summary>
        public void UpdateEndpoint(string url)
        {
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
        /// Stop connection (compatibility method)
        /// </summary>
        public async Task StopAsync()
        {
            _cts?.Cancel();
            if (_ws?.State == WebSocketState.Open || _ws?.State == WebSocketState.Connecting)
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client stopping", CancellationToken.None);
            }
            Dispose();
        }

        /// <summary>
        /// Connect to server
        /// </summary>
        public async Task ConnectAsync(CancellationToken ct = default)
        {
            _ws?.Dispose();
            _ws = new ClientWebSocket();

            await _ws.ConnectAsync(_uri, ct);
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        }

        // ====================================================================
        // Auth APIs
        // ====================================================================

        public async Task<Dictionary<string, object>> LoginAsync(
            string playerId,
            string version,
            CancellationToken cancellationToken = default)
        {
            var result = await SendMessagePackRequestAsync(Opcode.Login, new
            {
                player_id = playerId,
                version = version
            }, cancellationToken);

            if (result.TryGetValue("session_id", out var sid))
                SessionId = sid?.ToString();

            return result;
        }

        public async Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            await SendMessagePackRequestAsync(Opcode.Logout, new { }, cancellationToken);
        }

        public async Task<Dictionary<string, object>> RefreshSessionAsync(CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.RefreshSession, new { }, cancellationToken);
        }

        // ====================================================================
        // Round APIs
        // ====================================================================

        public async Task<string> GameRoundStartAsync(
            string? instanceId,
            string? playerName,
            string roundType,
            string? mapName = null,
            CancellationToken cancellationToken = default)
        {
            var result = await SendMessagePackRequestAsync(Opcode.RoundStart, new
            {
                instance_id = instanceId ?? "default",
                player_name = playerName,
                round_type = roundType,
                map_name = mapName
            }, cancellationToken);

            return result["round_id"]?.ToString() ?? string.Empty;
        }

        public async Task<Dictionary<string, object>> GameRoundEndAsync(
            string roundId,
            bool survived,
            int roundDuration,
            double damageDealt,
            string[]? itemsObtained,
            string? terrorName = null,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.RoundEnd, new
            {
                round_id = roundId,
                survived = survived,
                duration = roundDuration,
                damage_dealt = damageDealt,
                items_obtained = itemsObtained?.ToList() ?? new List<string>(),
                terror_name = terrorName
            }, cancellationToken);
        }

        // ====================================================================
        // Instance APIs
        // ====================================================================

        public async Task<Dictionary<string, object>> InstanceCreateAsync(
            int maxPlayers,
            Dictionary<string, object> settings,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.InstanceCreate, new
            {
                max_players = maxPlayers,
                settings = settings
            }, cancellationToken);
        }

        public async Task<List<Dictionary<string, object>>> InstanceListAsync(
            string? filter = null,
            int limit = 20,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            var result = await SendMessagePackRequestAsync(Opcode.InstanceList, new
            {
                filter = filter,
                limit = limit,
                offset = offset
            }, cancellationToken);

            if (result.TryGetValue("instances", out var instances) && instances is List<object> list)
            {
                return list.Cast<Dictionary<string, object>>().ToList();
            }

            return new List<Dictionary<string, object>>();
        }

        public async Task<Dictionary<string, object>> InstanceGetAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.InstanceGet, new
            {
                instance_id = instanceId
            }, cancellationToken);
        }

        public async Task InstanceUpdateAsync(
            string instanceId,
            Dictionary<string, object> updates,
            CancellationToken cancellationToken = default)
        {
            await SendMessagePackRequestAsync(Opcode.InstanceUpdate, new
            {
                instance_id = instanceId,
                updates = updates
            }, cancellationToken);
        }

        public async Task InstanceDeleteAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            await SendMessagePackRequestAsync(Opcode.InstanceDelete, new
            {
                instance_id = instanceId
            }, cancellationToken);
        }

        public async Task<Dictionary<string, object>> InstanceAlertAsync(
            string instanceId,
            string alertType,
            string message,
            Dictionary<string, object>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.InstanceAlert, new
            {
                instance_id = instanceId,
                alert_type = alertType,
                message = message,
                metadata = metadata ?? new Dictionary<string, object>()
            }, cancellationToken);
        }

        // ====================================================================
        // Player State APIs
        // ====================================================================

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
            // Fire-and-forget
            await SendFireAndForgetAsync(Opcode.UpdatePlayerState, MessagePackSerializer.Serialize(new
            {
                instance_id = instanceId,
                player_id = playerId,
                velocity = velocity,
                afk_duration = afkDuration,
                items = items ?? new List<string>(),
                damage = damage,
                is_alive = isAlive
            }), ct);
        }

        public async Task<Dictionary<string, object>> GetPlayerStateAsync(
            string instanceId,
            string playerId,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.GetPlayerState, new
            {
                instance_id = instanceId,
                player_id = playerId
            }, cancellationToken);
        }

        public async Task<List<Dictionary<string, object>>> GetAllPlayerStatesAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            var result = await SendMessagePackRequestAsync(Opcode.GetAllPlayerStates, new
            {
                instance_id = instanceId
            }, cancellationToken);

            if (result.TryGetValue("states", out var states) && states is List<object> list)
            {
                return list.Cast<Dictionary<string, object>>().ToList();
            }

            return new List<Dictionary<string, object>>();
        }

        public async Task<List<Infrastructure.PlayerStateInfo>> GetPlayerStatesAsync(
            string instanceId,
            CancellationToken cancellationToken = default)
        {
            var states = await GetAllPlayerStatesAsync(instanceId, cancellationToken);
            return states.Select(s => new Infrastructure.PlayerStateInfo
            {
                player_id = s.GetValueOrDefault("player_id", "")?.ToString() ?? "",
                player_name = s.GetValueOrDefault("player_name", "")?.ToString() ?? "",
                damage = Convert.ToDouble(s.GetValueOrDefault("damage", 0.0)),
                current_item = s.GetValueOrDefault("current_item", "None")?.ToString() ?? "None",
                is_dead = Convert.ToBoolean(s.GetValueOrDefault("is_dead", false)),
                velocity = Convert.ToDouble(s.GetValueOrDefault("velocity", 0.0)),
                afk_duration = Convert.ToDouble(s.GetValueOrDefault("afk_duration", 0.0))
            }).ToList();
        }

        // ====================================================================
        // Threat APIs
        // ====================================================================

        public async Task AnnounceThreatAsync(
            string instanceId,
            string terrorKey,
            string roundType,
            CancellationToken cancellationToken = default)
        {
            await SendMessagePackRequestAsync(Opcode.AnnounceThreat, new
            {
                instance_id = instanceId,
                terror_key = terrorKey,
                round_type = roundType
            }, cancellationToken);
        }

        public async Task RecordThreatResponseAsync(
            string instanceId,
            string playerId,
            string terrorName,
            string roundKey,
            string response,
            double responseTime,
            CancellationToken cancellationToken = default)
        {
            await SendMessagePackRequestAsync(Opcode.RecordThreatResponse, new
            {
                instance_id = instanceId,
                player_id = playerId,
                terror_name = terrorName,
                round_key = roundKey,
                response = response,
                response_time = responseTime
            }, cancellationToken);
        }

        public async Task<List<Infrastructure.DesirePlayerInfo>> FindDesirePlayersAsync(
            string instanceId,
            string terrorKey,
            string roundType,
            CancellationToken cancellationToken = default)
        {
            var result = await SendMessagePackRequestAsync(Opcode.FindDesirePlayers, new
            {
                instance_id = instanceId,
                terror_key = terrorKey,
                round_type = roundType
            }, cancellationToken);

            if (result.TryGetValue("players", out var players) && players is List<object> list)
            {
                return list.Cast<Dictionary<string, object>>().Select(p => new Infrastructure.DesirePlayerInfo
                {
                    player_id = p.GetValueOrDefault("player_id", "")?.ToString() ?? "",
                    player_name = p.GetValueOrDefault("player_name", "")?.ToString() ?? ""
                }).ToList();
            }

            return new List<Infrastructure.DesirePlayerInfo>();
        }

        // ====================================================================
        // Voting APIs
        // ====================================================================

        public async Task<Dictionary<string, object>> StartVotingAsync(
            string instanceId,
            string terrorName,
            DateTime expiresAt,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.StartVoting, new
            {
                instance_id = instanceId,
                terror_name = terrorName,
                expires_at = expiresAt
            }, cancellationToken);
        }

        public async Task SubmitVoteAsync(
            string campaignId,
            string playerId,
            string decision,
            CancellationToken cancellationToken = default)
        {
            await SendMessagePackRequestAsync(Opcode.SubmitVote, new
            {
                campaign_id = campaignId,
                player_id = playerId,
                decision = decision
            }, cancellationToken);
        }

        public async Task<Dictionary<string, object>> GetVotingCampaignAsync(
            string campaignId,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.GetVotingCampaign, new
            {
                campaign_id = campaignId
            }, cancellationToken);
        }

        // ====================================================================
        // Profile APIs
        // ====================================================================

        public async Task<Dictionary<string, object>> GetProfileAsync(
            string playerId,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.GetProfile, new
            {
                player_id = playerId
            }, cancellationToken);
        }

        public async Task<Dictionary<string, object>> UpdateProfileAsync(
            string playerId,
            string? playerName = null,
            int? skillLevel = null,
            object? terrorStats = null,
            int? totalRounds = null,
            int? totalSurvived = null,
            CancellationToken cancellationToken = default)
        {
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
            }, cancellationToken);
        }

        // ====================================================================
        // Settings APIs
        // ====================================================================

        public async Task<Dictionary<string, object>> GetSettingsAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.GetSettings, new
            {
                user_id = userId
            }, cancellationToken);
        }

        public async Task<Dictionary<string, object>> UpdateSettingsAsync(
            string userId,
            Dictionary<string, object> settings,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.UpdateSettings, new
            {
                user_id = userId,
                settings = settings
            }, cancellationToken);
        }

        public async Task<Dictionary<string, object>> SyncSettingsAsync(
            string userId,
            Dictionary<string, object> localSettings,
            int localVersion,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.SyncSettings, new
            {
                user_id = userId,
                local_settings = localSettings,
                local_version = localVersion
            }, cancellationToken);
        }

        // ====================================================================
        // Monitoring APIs
        // ====================================================================

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
            }, cancellationToken);
        }

        public async Task<List<Dictionary<string, object>>> GetMonitoringStatusHistoryAsync(
            string instanceId,
            long startTime,
            long endTime,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            var result = await SendMessagePackRequestAsync(Opcode.GetMonitoringHistory, new
            {
                instance_id = instanceId,
                start_time = startTime,
                end_time = endTime,
                limit = limit
            }, cancellationToken);

            if (result.TryGetValue("history", out var history) && history is List<object> list)
            {
                return list.Cast<Dictionary<string, object>>().ToList();
            }

            return new List<Dictionary<string, object>>();
        }

        public async Task<List<Dictionary<string, object>>> GetMonitoringErrorsAsync(
            string instanceId,
            long startTime,
            long endTime,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            var result = await SendMessagePackRequestAsync(Opcode.GetMonitoringErrors, new
            {
                instance_id = instanceId,
                start_time = startTime,
                end_time = endTime,
                limit = limit
            }, cancellationToken);

            if (result.TryGetValue("errors", out var errors) && errors is List<object> list)
            {
                return list.Cast<Dictionary<string, object>>().ToList();
            }

            return new List<Dictionary<string, object>>();
        }

        public async Task LogErrorAsync(
            string source,
            string message,
            string? stackTrace = null,
            Dictionary<string, object>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            await SendMessagePackRequestAsync(Opcode.LogError, new
            {
                source = source,
                message = message,
                stack_trace = stackTrace,
                metadata = metadata ?? new Dictionary<string, object>()
            }, cancellationToken);
        }

        // ====================================================================
        // Analytics APIs
        // ====================================================================

        public async Task<Dictionary<string, object>> GetPlayerAnalyticsAsync(
            string playerId,
            long startTime,
            long endTime,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.GetPlayerAnalytics, new
            {
                player_id = playerId,
                start_time = startTime,
                end_time = endTime
            }, cancellationToken);
        }

        public async Task<Dictionary<string, object>> GetTerrorAnalyticsAsync(
            string terrorName,
            long startTime,
            long endTime,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.GetTerrorAnalytics, new
            {
                terror_name = terrorName,
                start_time = startTime,
                end_time = endTime
            }, cancellationToken);
        }

        public async Task<Dictionary<string, object>> GetInstanceAnalyticsAsync(
            string instanceId,
            long startTime,
            long endTime,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.GetInstanceAnalytics, new
            {
                instance_id = instanceId,
                start_time = startTime,
                end_time = endTime
            }, cancellationToken);
        }

        public async Task<Dictionary<string, object>> GetVotingAnalyticsAsync(
            long startTime,
            long endTime,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.GetVotingAnalytics, new
            {
                start_time = startTime,
                end_time = endTime
            }, cancellationToken);
        }

        public async Task<Dictionary<string, object>> ExportAnalyticsAsync(
            string exportType,
            Dictionary<string, object> filters,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.ExportAnalytics, new
            {
                export_type = exportType,
                filters = filters
            }, cancellationToken);
        }

        // ====================================================================
        // Backup APIs
        // ====================================================================

        public async Task<Dictionary<string, object>> CreateBackupAsync(
            string backupType,
            bool compress,
            bool encrypt,
            string description,
            CancellationToken cancellationToken = default)
        {
            return await SendMessagePackRequestAsync(Opcode.CreateBackup, new
            {
                backup_type = backupType,
                compress = compress,
                encrypt = encrypt,
                description = description
            }, cancellationToken);
        }

        public async Task RestoreBackupAsync(
            string backupId,
            bool validateBeforeRestore,
            bool createBackupBeforeRestore,
            CancellationToken cancellationToken = default)
        {
            await SendMessagePackRequestAsync(Opcode.RestoreBackup, new
            {
                backup_id = backupId,
                validate_before_restore = validateBeforeRestore,
                create_backup_before_restore = createBackupBeforeRestore
            }, cancellationToken);
        }

        public async Task<List<Dictionary<string, object>>> ListBackupsAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            var result = await SendMessagePackRequestAsync(Opcode.ListBackups, new
            {
                user_id = userId
            }, cancellationToken);

            if (result.TryGetValue("backups", out var backups) && backups is List<object> list)
            {
                return list.Cast<Dictionary<string, object>>().ToList();
            }

            return new List<Dictionary<string, object>>();
        }

        // ====================================================================
        // Wished Terrors APIs
        // ====================================================================

        public async Task UpdateWishedTerrorsAsync(
            string playerId,
            List<string> terrorNames,
            CancellationToken cancellationToken = default)
        {
            await SendMessagePackRequestAsync(Opcode.UpdateWishedTerrors, new
            {
                player_id = playerId,
                terror_names = terrorNames
            }, cancellationToken);
        }

        public async Task<List<string>> GetWishedTerrorsAsync(
            string playerId,
            CancellationToken cancellationToken = default)
        {
            var result = await SendMessagePackRequestAsync(Opcode.GetWishedTerrors, new
            {
                player_id = playerId
            }, cancellationToken);

            if (result.TryGetValue("terror_names", out var terrorNames) && terrorNames is List<object> list)
            {
                return list.Cast<string>().ToList();
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
            var response = await SendRequestAsync(opcode, payload, ct);
            return MessagePackSerializer.Deserialize<Dictionary<string, object>>(response);
        }

        /// <summary>
        /// Send request and wait for response
        /// </summary>
        private async Task<byte[]> SendRequestAsync(Opcode opcode, byte[] payload, CancellationToken ct)
        {
            var reqId = _nextReqId++;
            if (reqId == 0xFF) reqId = 1; // Skip 0xFF (reserved for fire-and-forget)

            var tcs = new TaskCompletionSource<byte[]>();
            _pending[reqId] = tcs;

            try
            {
                await SendMessageAsync(opcode, reqId, payload, ct);

                using var timeoutCts = new CancellationTokenSource(30000);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                return await tcs.Task.WaitAsync(linkedCts.Token);
            }
            finally
            {
                _pending.Remove(reqId);
            }
        }

        /// <summary>
        /// Send fire-and-forget message (no response expected)
        /// </summary>
        private async Task SendFireAndForgetAsync(Opcode opcode, byte[] payload, CancellationToken ct)
        {
            await SendMessageAsync(opcode, 0xFF, payload, ct);
        }

        /// <summary>
        /// Send raw message
        /// </summary>
        private async Task SendMessageAsync(Opcode opcode, byte reqId, byte[] payload, CancellationToken ct)
        {
            if (_ws?.State != WebSocketState.Open)
                throw new InvalidOperationException("Not connected");

            var message = new byte[4 + payload.Length];
            message[0] = (byte)opcode;
            message[1] = reqId;
            message[2] = (byte)(payload.Length >> 8);
            message[3] = (byte)(payload.Length & 0xFF);
            payload.CopyTo(message, 4);

            await _sendLock.WaitAsync(ct);
            try
            {
                await _ws.SendAsync(message, WebSocketMessageType.Binary, true, ct);
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
            var messageBuffer = new List<byte>();

            while (_ws!.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(_receiveBuffer, ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    messageBuffer.AddRange(new ArraySegment<byte>(_receiveBuffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        ProcessMessage(messageBuffer.ToArray());
                        messageBuffer.Clear();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in receive loop");
                    break;
                }
            }
        }

        /// <summary>
        /// Process received message
        /// </summary>
        private void ProcessMessage(byte[] data)
        {
            if (data.Length < 4) return;

            var opcode = (Opcode)data[0];
            var reqId = data[1];
            var payloadLen = (data[2] << 8) | data[3];
            var payload = data.AsSpan(4, Math.Min(payloadLen, data.Length - 4)).ToArray();

            if (opcode == Opcode.Success && _pending.TryGetValue(reqId, out var tcs))
            {
                tcs.SetResult(payload);
            }
            else if (opcode == Opcode.Error && _pending.TryGetValue(reqId, out tcs))
            {
                try
                {
                    var errorData = MessagePackSerializer.Deserialize<Dictionary<string, object>>(payload);
                    var errorMsg = errorData.GetValueOrDefault("message", "Unknown error")?.ToString() ?? "Unknown error";
                    tcs.SetException(new Exception($"Server error: {errorMsg}"));
                }
                catch
                {
                    tcs.SetException(new Exception("Server error (failed to parse)"));
                }
            }
            else if (opcode == Opcode.Pong)
            {
                // Heartbeat response
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _ws?.Dispose();
            _sendLock.Dispose();
        }
    }
}
