using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToNRoundCounterSDK;

/// <summary>
/// Lightweight WebSocket RPC client for ToNRoundCounter Cloud.
/// </summary>
public sealed class ToNRoundCounterCloudClient : IAsyncDisposable, IDisposable
{
    private const string DefaultSessionId = "sdk-client";
    private const int ReceiveBufferSize = 8192;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
    };

    private readonly ToNRoundCounterCloudOptions _options;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CloudMessage>> _pending = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Func<CustomRpcEventArgs, Task>>> _eventHandlers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _connectionCts;
    private Task? _receiveTask;
    private string _sessionId = DefaultSessionId;
    private string? _appId;
    private string? _appToken;

    public ToNRoundCounterCloudClient(ToNRoundCounterCloudOptions? options = null)
    {
        _options = options ?? new ToNRoundCounterCloudOptions();
    }

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<Exception>? ErrorOccurred;
    public event EventHandler<CloudStreamEventArgs>? StreamReceived;

    public bool IsConnected => _socket?.State == WebSocketState.Open;
    public string? PlayerId { get; private set; }
    public string? ApiKey { get; private set; }
    public string? SessionToken { get; private set; }

    public static Uri CreateAppAuthorizationUri(
        Uri cloudBaseUri,
        string appId,
        string redirectUri,
        string? state = null,
        string? appName = null,
        IEnumerable<string>? scopes = null)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            throw new ArgumentException("APPID is required.", nameof(appId));
        }

        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            throw new ArgumentException("Callback redirect URI is required.", nameof(redirectUri));
        }

        var builder = new UriBuilder(new Uri(cloudBaseUri, "/authorize-app"));
        var query = new List<string>
        {
            $"app_id={Uri.EscapeDataString(appId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
        };

        if (!string.IsNullOrWhiteSpace(state))
        {
            query.Add($"state={Uri.EscapeDataString(state)}");
        }

        if (!string.IsNullOrWhiteSpace(appName))
        {
            query.Add($"app_name={Uri.EscapeDataString(appName)}");
        }

        var normalizedScopes = NormalizeScopes(scopes);
        if (normalizedScopes.Length > 0)
        {
            query.Add($"scope={Uri.EscapeDataString(string.Join(" ", normalizedScopes))}");
        }

        builder.Query = string.Join("&", query);
        return builder.Uri;
    }

    public static Uri CreateAppAuthorizationUri(
        string appId,
        string redirectUri,
        string? state = null,
        string? appName = null,
        IEnumerable<string>? scopes = null)
    {
        return CreateAppAuthorizationUri(
            ToNRoundCounterCloudOptions.DefaultCloudBaseUri,
            appId,
            redirectUri,
            state,
            appName,
            scopes);
    }

    public static async Task<AppAuthorizationResult> RequestAppAuthorizationAsync(
        Uri cloudBaseUri,
        string appId,
        string? appName = null,
        TimeSpan? timeout = null,
        Func<Uri, CancellationToken, Task>? openAuthorizationUriAsync = null,
        CancellationToken cancellationToken = default,
        IEnumerable<string>? scopes = null)
    {
        var state = GenerateState();
        using var callbackServer = new LoopbackAppAuthorizationServer();

        var authorizationUri = CreateAppAuthorizationUri(
            cloudBaseUri,
            appId,
            callbackServer.RedirectUri.ToString(),
            state,
            appName,
            scopes);

        var waitTask = callbackServer.WaitForCallbackAsync(
            appId,
            state,
            timeout ?? TimeSpan.FromMinutes(5),
            cancellationToken);

        var opener = openAuthorizationUriAsync ?? LoopbackAppAuthorizationServer.OpenDefaultBrowserAsync;
        await opener(authorizationUri, cancellationToken).ConfigureAwait(false);

        return await waitTask.ConfigureAwait(false);
    }

    public static Task<AppAuthorizationResult> RequestAppAuthorizationAsync(
        string appId,
        string? appName = null,
        TimeSpan? timeout = null,
        Func<Uri, CancellationToken, Task>? openAuthorizationUriAsync = null,
        CancellationToken cancellationToken = default,
        IEnumerable<string>? scopes = null)
    {
        return RequestAppAuthorizationAsync(
            ToNRoundCounterCloudOptions.DefaultCloudBaseUri,
            appId,
            appName,
            timeout,
            openAuthorizationUriAsync,
            cancellationToken,
            scopes);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return;
        }

        _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(_options.WebSocketUri, cancellationToken).ConfigureAwait(false);
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_connectionCts.Token));
        Connected?.Invoke(this, EventArgs.Empty);
    }

    public async Task<LoginResult> LoginWithApiKeyAsync(
        string playerId,
        string apiKey,
        string? appId = null,
        string? appToken = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedAppId = appId ?? _options.AppId;
        var resolvedAppToken = appToken ?? _options.AppToken;
        if (string.IsNullOrWhiteSpace(resolvedAppId) || string.IsNullOrWhiteSpace(resolvedAppToken))
        {
            throw new InvalidOperationException("SDK login requires both AppId and AppToken. Pass them to LoginWithApiKeyAsync or ToNRoundCounterCloudOptions.");
        }

        var result = await SendRpcAsync<LoginResult>(
            "auth.loginWithApiKey",
            new
            {
                player_id = playerId,
                api_key = apiKey,
                app_id = resolvedAppId,
                app_token = resolvedAppToken,
                client_version = _options.ClientVersion,
                client_type = "external-sdk",
            },
            cancellationToken).ConfigureAwait(false);

        PlayerId = result.PlayerId;
        ApiKey = apiKey;
        SessionToken = result.SessionToken;
        _sessionId = string.IsNullOrWhiteSpace(result.SessionId) ? result.SessionToken : result.SessionId!;
        _appId = resolvedAppId.Trim();
        _appToken = resolvedAppToken;
        return result;
    }

    public Task<LoginResult> LoginWithApiKeyAsync(
        string playerId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        return LoginWithApiKeyAsync(playerId, apiKey, null, null, cancellationToken);
    }

    public Task RevokeAppTokenAsync(
        string playerId,
        string apiKey,
        string appId,
        CancellationToken cancellationToken = default)
    {
        return SendRpcNoResultAsync(
            "auth.revokeAppToken",
            new
            {
                player_id = playerId,
                api_key = apiKey,
                app_id = appId,
            },
            cancellationToken);
    }

    public Task<OneTimeTokenResult> GenerateOneTimeTokenAsync(
        string playerId,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        return SendRpcAsync<OneTimeTokenResult>(
            "auth.generateOneTimeToken",
            new { player_id = playerId, api_key = apiKey },
            cancellationToken);
    }

    public Task<InstanceCreateResult> CreateInstanceAsync(
        int maxPlayers = 10,
        object? settings = null,
        CancellationToken cancellationToken = default)
    {
        return SendRpcAsync<InstanceCreateResult>(
            "instance.create",
            new
            {
                max_players = maxPlayers,
                settings = settings ?? new { auto_suicide_mode = "Individual", voting_timeout = 30 },
            },
            cancellationToken);
    }

    public Task<InstanceListResult> ListInstancesAsync(
        string filter = "available",
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        return SendRpcAsync<InstanceListResult>(
            "instance.list",
            new { filter, limit, offset },
            cancellationToken);
    }

    public Task<CloudInstance> GetInstanceAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        return SendRpcAsync<CloudInstance>(
            "instance.get",
            new { instance_id = instanceId },
            cancellationToken);
    }

    public Task UpdatePlayerStateAsync(
        string instanceId,
        PlayerStateUpdate state,
        CancellationToken cancellationToken = default)
    {
        return SendRpcNoResultAsync(
            "player.state.update",
            new
            {
                instance_id = instanceId,
                player_state = new
                {
                    player_id = state.PlayerId,
                    player_name = state.PlayerName,
                    velocity = state.Velocity,
                    afk_duration = state.AfkDuration,
                    items = state.Items ?? Array.Empty<string>(),
                    damage = state.Damage,
                    is_alive = state.IsAlive,
                    timestamp = DateTimeOffset.UtcNow.ToString("O"),
                },
            },
            cancellationToken);
    }

    public Task<JsonElement> ReportRoundAsync(RoundReport report, CancellationToken cancellationToken = default)
    {
        return SendRpcJsonAsync(
            "round.report",
            new
            {
                instance_id = report.InstanceId,
                round_type = report.RoundType,
                terror_name = report.TerrorName,
                terror_key = report.TerrorName,
                start_time = report.StartTime.ToUniversalTime().ToString("O"),
                end_time = report.EndTime.ToUniversalTime().ToString("O"),
                initial_player_count = report.InitialPlayerCount,
                survivor_count = report.SurvivorCount,
                status = report.Status,
            },
            cancellationToken);
    }

    public async Task<string> SubscribeAsync(string channel, CancellationToken cancellationToken = default)
    {
        var result = await SendRpcJsonAsync(ToSdkAppRpc("subscribe"), new { channel }, cancellationToken).ConfigureAwait(false);
        if (result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty("subscriptionId", out var subscriptionId) &&
            subscriptionId.GetString() is { Length: > 0 } value)
        {
            return value;
        }

        throw new CloudApiException("Subscribe response did not include a subscriptionId.");
    }

    public Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken = default)
    {
        return SendRpcNoResultAsync(ToSdkAppRpc("unsubscribe"), new { subscriptionId }, cancellationToken);
    }

    public Task<T> SendAppRpcAsync<T>(string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        return SendRpcAsync<T>(ToSdkAppRpc(method), parameters, cancellationToken);
    }

    public Task<JsonElement> SendAppRpcJsonAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        return SendRpcJsonAsync(ToSdkAppRpc(method), parameters, cancellationToken);
    }

    public Task SendAppRpcNoResultAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        return SendRpcNoResultAsync(ToSdkAppRpc(method), parameters, cancellationToken);
    }

    public Task<CustomRpcSendResult> SendCustomRpcAsync(
        string method,
        object? payload = null,
        IEnumerable<string>? targetUserIds = null,
        string? instanceId = null,
        bool includeSelf = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedMethod = NormalizeCustomRpcMethod(method);

        var targets = targetUserIds?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return SendAppRpcAsync<CustomRpcSendResult>(
            "custom.rpc.send",
            new
            {
                method = normalizedMethod,
                payload,
                target_user_ids = targets is { Length: > 0 } ? targets : null,
                instance_id = string.IsNullOrWhiteSpace(instanceId) ? null : instanceId.Trim(),
                include_self = includeSelf,
            },
            cancellationToken);
    }

    public IDisposable On(string method, Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return On(method, _ =>
        {
            handler();
            return Task.CompletedTask;
        });
    }

    public IDisposable On(string method, Action<CustomRpcEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return On(method, args =>
        {
            handler(args);
            return Task.CompletedTask;
        });
    }

    public IDisposable On(string method, Func<CustomRpcEventArgs, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var normalizedMethod = NormalizeEventName(method);
        var handlers = _eventHandlers.GetOrAdd(
            normalizedMethod,
            _ => new ConcurrentDictionary<Guid, Func<CustomRpcEventArgs, Task>>());
        var subscriptionId = Guid.NewGuid();
        handlers[subscriptionId] = handler;

        return new EventHandlerSubscription(_eventHandlers, normalizedMethod, subscriptionId);
    }

    public IDisposable on(string method, Action handler)
    {
        return On(method, handler);
    }

    public IDisposable on(string method, Action<CustomRpcEventArgs> handler)
    {
        return On(method, handler);
    }

    public IDisposable on(string method, Func<CustomRpcEventArgs, Task> handler)
    {
        return On(method, handler);
    }

    public async Task<T> SendRpcAsync<T>(string rpc, object? parameters = null, CancellationToken cancellationToken = default)
    {
        var result = await SendRpcJsonAsync(rpc, parameters, cancellationToken).ConfigureAwait(false);
        return result.Deserialize<T>(JsonOptions)
            ?? throw new CloudApiException($"RPC '{rpc}' returned an empty or invalid result.");
    }

    public async Task<JsonElement> SendRpcJsonAsync(string rpc, object? parameters = null, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(rpc, parameters, cancellationToken).ConfigureAwait(false);
        if (response.Result.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return default;
        }

        return response.Result.Clone();
    }

    public async Task SendRpcNoResultAsync(string rpc, object? parameters = null, CancellationToken cancellationToken = default)
    {
        await SendRequestAsync(rpc, parameters, cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _connectionCts?.Cancel();

        if (_socket is { State: WebSocketState.Open })
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", cancellationToken)
                .ConfigureAwait(false);
        }

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        CleanupSocket();
    }

    private async Task<CloudMessage> SendRequestAsync(string rpc, object? parameters, CancellationToken cancellationToken)
    {
        if (string.Equals(rpc, "auth.register", StringComparison.Ordinal) ||
            string.Equals(rpc, "auth.registerAppToken", StringComparison.Ordinal))
        {
            throw new NotSupportedException("This registration RPC is not available through ToNRoundCounterSDK.");
        }

        if (_socket is not { State: WebSocketState.Open } socket)
        {
            throw new InvalidOperationException("Client is not connected. Call ConnectAsync first.");
        }

        var request = new CloudMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = "request",
            Rpc = rpc,
            Params = parameters,
            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
        };

        if (!IsAuthRpc(rpc) && _sessionId != DefaultSessionId)
        {
            request.SessionId = _sessionId;
            request.AppId = _appId ?? throw new InvalidOperationException("SDK app_id is not available. Log in again with AppId and AppToken.");
            request.AppToken = _appToken ?? throw new InvalidOperationException("SDK app_token is not available. Log in again with AppId and AppToken.");
        }

        var tcs = new TaskCompletionSource<CloudMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(request.Id, tcs))
        {
            throw new InvalidOperationException($"Duplicate request id: {request.Id}");
        }

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }

            using var timeoutCts = new CancellationTokenSource(_options.RequestTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var response = await tcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            ThrowIfError(response, rpc);
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"RPC '{rpc}' timed out after {_options.RequestTimeout.TotalSeconds:0.#} seconds.");
        }
        finally
        {
            _pending.TryRemove(request.Id, out _);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferSize];
        var message = new MemoryStream();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _socket is { State: WebSocketState.Open } socket)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;

                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    DispatchMessage(message.ToArray());
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
            CancelPending(ex);
        }
        finally
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DispatchMessage(byte[] payload)
    {
        var response = JsonSerializer.Deserialize<CloudMessage>(payload, JsonOptions);
        if (response == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(response.Id) && _pending.TryRemove(response.Id, out var pending))
        {
            pending.TrySetResult(response);
            return;
        }

        if (!string.IsNullOrWhiteSpace(response.Stream))
        {
            DateTimeOffset? timestamp = DateTimeOffset.TryParse(response.Timestamp, out var parsed) ? parsed : null;
            var data = response.Data.Clone();
            var streamEvent = new CloudStreamEventArgs(response.Stream!, data, timestamp);
            StreamReceived?.Invoke(this, streamEvent);
            DispatchStreamHandlers(streamEvent);
            DispatchCustomRpc(response.Stream!, data, timestamp);
        }
    }

    private void DispatchStreamHandlers(CloudStreamEventArgs streamEvent)
    {
        if (!_eventHandlers.TryGetValue(streamEvent.Stream, out var handlers) || handlers.IsEmpty)
        {
            return;
        }

        var args = new CustomRpcEventArgs(
            streamEvent.Stream,
            streamEvent.Data.Clone(),
            streamEvent.Data.Clone(),
            GetOptionalString(streamEvent.Data, "from_user_id"),
            GetOptionalString(streamEvent.Data, "from_player_id"),
            GetOptionalString(streamEvent.Data, "instance_id"),
            streamEvent.Timestamp,
            streamEvent.Stream);

        DispatchEventHandlers(handlers, args);
    }

    private void DispatchCustomRpc(string stream, JsonElement data, DateTimeOffset? streamTimestamp)
    {
        if (string.IsNullOrWhiteSpace(_appId) ||
            !string.Equals(stream, $"SDK.app.{_appId}.custom.rpc", StringComparison.Ordinal))
        {
            return;
        }

        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("method", out var methodElement) ||
            methodElement.GetString() is not { Length: > 0 } method ||
            !_eventHandlers.TryGetValue(method, out var handlers) ||
            handlers.IsEmpty)
        {
            return;
        }

        var payload = data.TryGetProperty("payload", out var payloadElement)
            ? payloadElement.Clone()
            : default;
        var timestamp = streamTimestamp;
        if (data.TryGetProperty("timestamp", out var timestampElement) &&
            DateTimeOffset.TryParse(timestampElement.GetString(), out var parsedTimestamp))
        {
            timestamp = parsedTimestamp;
        }

        var args = new CustomRpcEventArgs(
            method,
            payload,
            data.Clone(),
            GetOptionalString(data, "from_user_id"),
            GetOptionalString(data, "from_player_id"),
            GetOptionalString(data, "instance_id"),
            timestamp,
            stream);

        DispatchEventHandlers(handlers, args);
    }

    private void DispatchEventHandlers(
        ConcurrentDictionary<Guid, Func<CustomRpcEventArgs, Task>> handlers,
        CustomRpcEventArgs args)
    {
        foreach (var handler in handlers.Values)
        {
            try
            {
                var task = handler(args);
                if (!task.IsCompletedSuccessfully)
                {
                    _ = ObserveHandlerTaskAsync(task);
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }
    }

    private async Task ObserveHandlerTaskAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }
    }

    private static void ThrowIfError(CloudMessage response, string rpc)
    {
        if (string.Equals(response.Status, "error", StringComparison.OrdinalIgnoreCase) || response.Error != null)
        {
            var message = response.Error?.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = $"RPC '{rpc}' failed.";
            }

            throw new CloudApiException(message!, response.Error?.Code, response.Error?.Details ?? default);
        }
    }

    private static bool IsAuthRpc(string rpc)
    {
        return rpc is "auth.loginWithApiKey" or "auth.loginWithOneTimeToken";
    }

    private static string NormalizeEventName(string method)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            throw new ArgumentException("Event name is required.", nameof(method));
        }

        var normalized = method.Trim();
        if (normalized.Length > 256)
        {
            throw new ArgumentException("Event name must be 256 characters or fewer.", nameof(method));
        }

        return normalized;
    }

    private static string NormalizeCustomRpcMethod(string method)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            throw new ArgumentException("Custom RPC method is required.", nameof(method));
        }

        var normalized = method.Trim();
        if (normalized.Length > 128)
        {
            throw new ArgumentException("Custom RPC method must be 128 characters or fewer.", nameof(method));
        }

        if (normalized.StartsWith("SDK.app.", StringComparison.Ordinal) ||
            normalized.StartsWith("auth.", StringComparison.Ordinal))
        {
            throw new ArgumentException("Custom RPC method must be an app-local method name.", nameof(method));
        }

        return normalized;
    }

    private static string? GetOptionalString(JsonElement data, string propertyName)
    {
        return data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty(propertyName, out var element) &&
            element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    private static string[] NormalizeScopes(IEnumerable<string>? scopes)
    {
        return scopes?
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray()
            ?? Array.Empty<string>();
    }

    private string ToSdkAppRpc(string method)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            throw new ArgumentException("RPC method is required.", nameof(method));
        }

        if (method.StartsWith("SDK.app.", StringComparison.Ordinal))
        {
            return method;
        }

        if (string.IsNullOrWhiteSpace(_appId))
        {
            throw new InvalidOperationException("SDK app_id is not available. Log in with AppId and AppToken before sending app RPCs.");
        }

        return $"SDK.app.{_appId}.{method}";
    }

    private static string GenerateState()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

    private void CancelPending(Exception ex)
    {
        foreach (var pending in _pending)
        {
            if (_pending.TryRemove(pending.Key, out var tcs))
            {
                tcs.TrySetException(ex);
            }
        }
    }

    private void CleanupSocket()
    {
        _socket?.Dispose();
        _socket = null;
        _connectionCts?.Dispose();
        _connectionCts = null;
    }

    private sealed class EventHandlerSubscription : IDisposable
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Func<CustomRpcEventArgs, Task>>> _handlersByMethod;
        private readonly string _method;
        private readonly Guid _subscriptionId;
        private int _disposed;

        public EventHandlerSubscription(
            ConcurrentDictionary<string, ConcurrentDictionary<Guid, Func<CustomRpcEventArgs, Task>>> handlersByMethod,
            string method,
            Guid subscriptionId)
        {
            _handlersByMethod = handlersByMethod;
            _method = method;
            _subscriptionId = subscriptionId;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (_handlersByMethod.TryGetValue(_method, out var handlers))
            {
                handlers.TryRemove(_subscriptionId, out _);
                if (handlers.IsEmpty)
                {
                    _handlersByMethod.TryRemove(_method, out _);
                }
            }
        }
    }

    public void Dispose()
    {
        _connectionCts?.Cancel();
        CleanupSocket();
        _sendLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Dispose();
        }
    }
}
