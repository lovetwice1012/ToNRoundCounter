using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToNRoundCounterSDK;

public sealed record ToNRoundCounterCloudOptions
{
    public static Uri DefaultCloudBaseUri { get; } = new("https://toncloud.sprink.cloud");
    public static Uri DefaultWebSocketUri { get; } = new("wss://toncloud.sprink.cloud/ws");

    public Uri WebSocketUri { get; init; } = DefaultWebSocketUri;
    public string ClientVersion { get; init; } = "1.0.0";
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public string? AppId { get; init; }
    public string? AppToken { get; init; }
    public IReadOnlyList<string>? AppScopes { get; init; }
}

public sealed record LoginResult(
    [property: JsonPropertyName("session_token")] string SessionToken,
    [property: JsonPropertyName("player_id")] string PlayerId,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("scopes")] IReadOnlyList<string>? Scopes = null);

public sealed record OneTimeTokenResult(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("login_url")] string LoginUrl,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

public sealed record InstanceCreateResult(
    [property: JsonPropertyName("instance_id")] string InstanceId,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt);

public sealed record InstanceListResult(
    [property: JsonPropertyName("instances")] IReadOnlyList<CloudInstanceSummary> Instances,
    [property: JsonPropertyName("total")] int Total);

public sealed record CloudInstanceSummary(
    [property: JsonPropertyName("instance_id")] string InstanceId,
    [property: JsonPropertyName("member_count")] int MemberCount,
    [property: JsonPropertyName("max_players")] int MaxPlayers,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt);

public sealed record CloudInstance(
    [property: JsonPropertyName("instance_id")] string InstanceId,
    [property: JsonPropertyName("members")] IReadOnlyList<CloudInstanceMember> Members,
    [property: JsonPropertyName("settings")] JsonElement Settings,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt);

public sealed record CloudInstanceMember(
    [property: JsonPropertyName("player_id")] string PlayerId,
    [property: JsonPropertyName("player_name")] string PlayerName,
    [property: JsonPropertyName("joined_at")] DateTimeOffset? JoinedAt);

public sealed record PlayerStateUpdate(
    string PlayerId,
    string? PlayerName = null,
    double Velocity = 0,
    double AfkDuration = 0,
    IReadOnlyList<string>? Items = null,
    double Damage = 0,
    bool IsAlive = true);

public sealed record RoundReport(
    string? InstanceId,
    string RoundType,
    string? TerrorName,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int InitialPlayerCount,
    int SurvivorCount,
    string Status);

public sealed record CustomRpcSendResult(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("app_id")] string AppId,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("delivered_count")] int DeliveredCount,
    [property: JsonPropertyName("target_user_count")] int? TargetUserCount,
    [property: JsonPropertyName("instance_id")] string? InstanceId,
    [property: JsonPropertyName("timestamp")] DateTimeOffset? Timestamp);
