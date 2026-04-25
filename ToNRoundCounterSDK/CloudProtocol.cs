using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToNRoundCounterSDK;

/// <summary>
/// Request/response envelope used by ToNRoundCounter Cloud WebSocket RPC.
/// </summary>
public sealed class CloudMessage
{
    [JsonPropertyName("version")]
    public string? Version { get; set; } = "1.0";

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("type")]
    public string Type { get; set; } = "request";

    [JsonPropertyName("rpc")]
    public string? Rpc { get; set; }

    [JsonPropertyName("stream")]
    public string? Stream { get; set; }

    [JsonPropertyName("params")]
    public object? Params { get; set; }

    [JsonPropertyName("result")]
    public JsonElement Result { get; set; }

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("error")]
    public CloudError? Error { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("app_id")]
    public string? AppId { get; set; }

    [JsonPropertyName("app_token")]
    public string? AppToken { get; set; }
}

public sealed class CloudError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public JsonElement Details { get; set; }
}

public sealed class CloudStreamEventArgs : EventArgs
{
    public CloudStreamEventArgs(string stream, JsonElement data, DateTimeOffset? timestamp)
    {
        Stream = stream;
        Data = data;
        Timestamp = timestamp;
    }

    public string Stream { get; }
    public JsonElement Data { get; }
    public DateTimeOffset? Timestamp { get; }
}

public sealed class CustomRpcEventArgs : EventArgs
{
    public CustomRpcEventArgs(
        string method,
        JsonElement payload,
        JsonElement data,
        string? fromUserId,
        string? fromPlayerId,
        string? instanceId,
        DateTimeOffset? timestamp)
    {
        Method = method;
        Payload = payload;
        Data = data;
        FromUserId = fromUserId;
        FromPlayerId = fromPlayerId;
        InstanceId = instanceId;
        Timestamp = timestamp;
    }

    public string Method { get; }
    public JsonElement Payload { get; }
    public JsonElement Data { get; }
    public string? FromUserId { get; }
    public string? FromPlayerId { get; }
    public string? InstanceId { get; }
    public DateTimeOffset? Timestamp { get; }

    public T? GetPayload<T>(JsonSerializerOptions? options = null)
    {
        if (Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return default;
        }

        return Payload.Deserialize<T>(options);
    }
}

public sealed class CloudApiException : Exception
{
    public CloudApiException(string message, string? code = null, JsonElement details = default)
        : base(message)
    {
        Code = code;
        Details = details;
    }

    public string? Code { get; }
    public JsonElement Details { get; }
}
