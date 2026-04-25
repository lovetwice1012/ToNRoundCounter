using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ToNRoundCounterSDK;

public sealed record AppAuthorizationResult(
    string AppId,
    string AppToken,
    string State,
    Uri RedirectUri,
    IReadOnlyList<string> Scopes);

internal sealed class LoopbackAppAuthorizationServer : IDisposable
{
    private const string CallbackPath = "/tonround-sdk-callback/";

    private readonly TcpListener _listener;
    private bool _disposed;

    public LoopbackAppAuthorizationServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start(1);

        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        RedirectUri = new Uri($"http://127.0.0.1:{endpoint.Port}{CallbackPath}");
    }

    public Uri RedirectUri { get; }

    public async Task<AppAuthorizationResult> WaitForCallbackAsync(
        string expectedAppId,
        string expectedState,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        using var stopRegistration = linkedCts.Token.Register(() =>
        {
            try
            {
                _listener.Stop();
            }
            catch
            {
            }
        });

        try
        {
            using var client = await _listener.AcceptTcpClientAsync(linkedCts.Token).ConfigureAwait(false);
            return await HandleCallbackAsync(client, expectedAppId, expectedState).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            linkedCts.IsCancellationRequested &&
            (ex is OperationCanceledException || ex is SocketException || ex is ObjectDisposedException))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw new TimeoutException($"App authorization callback was not received within {timeout.TotalSeconds:0.#} seconds.");
        }
    }

    private async Task<AppAuthorizationResult> HandleCallbackAsync(
        TcpClient client,
        string expectedAppId,
        string expectedState)
    {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            await WriteHtmlResponseAsync(stream, 400, "Bad Request", "The authorization callback request was empty.").ConfigureAwait(false);
            throw new CloudApiException("The authorization callback request was empty.", "INVALID_CALLBACK");
        }

        while (!string.IsNullOrEmpty(await reader.ReadLineAsync().ConfigureAwait(false)))
        {
        }

        var target = ParseRequestTarget(requestLine);
        var callbackUri = target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(target)
            : new Uri(RedirectUri, target);

        if (!string.Equals(callbackUri.AbsolutePath, CallbackPath, StringComparison.Ordinal))
        {
            await WriteHtmlResponseAsync(stream, 404, "Not Found", "This callback endpoint is not active.").ConfigureAwait(false);
            throw new CloudApiException("The authorization callback path was invalid.", "INVALID_CALLBACK");
        }

        var query = ParseQuery(callbackUri.Query);
        if (query.TryGetValue("error", out var error))
        {
            await WriteHtmlResponseAsync(stream, 403, "Authorization Denied", "You can close this tab and return to the application.").ConfigureAwait(false);
            throw new CloudApiException($"App authorization failed: {error}", error);
        }

        query.TryGetValue("app_id", out var appId);
        query.TryGetValue("app_token", out var appToken);
        query.TryGetValue("state", out var state);
        query.TryGetValue("scope", out var scope);

        if (!string.Equals(appId, expectedAppId, StringComparison.Ordinal))
        {
            await WriteHtmlResponseAsync(stream, 400, "Invalid APPID", "The callback APPID did not match the authorization request.").ConfigureAwait(false);
            throw new CloudApiException("The callback APPID did not match the authorization request.", "APP_ID_MISMATCH");
        }

        if (!string.Equals(state, expectedState, StringComparison.Ordinal))
        {
            await WriteHtmlResponseAsync(stream, 400, "Invalid State", "The callback state did not match the authorization request.").ConfigureAwait(false);
            throw new CloudApiException("The callback state did not match the authorization request.", "STATE_MISMATCH");
        }

        if (string.IsNullOrWhiteSpace(appToken))
        {
            await WriteHtmlResponseAsync(stream, 400, "Missing APPToken", "The callback did not include an APPToken.").ConfigureAwait(false);
            throw new CloudApiException("The callback did not include an APPToken.", "APP_TOKEN_MISSING");
        }

        await WriteHtmlResponseAsync(stream, 200, "Authorization Complete", "Authorization complete. You can close this tab and return to the application.").ConfigureAwait(false);

        return new AppAuthorizationResult(appId!, appToken, state!, callbackUri, ParseScopes(scope));
    }

    private static IReadOnlyList<string> ParseScopes(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return Array.Empty<string>();
        }

        return scope
            .Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string ParseRequestTarget(string requestLine)
    {
        var parts = requestLine.Split(' ');
        if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.OrdinalIgnoreCase))
        {
            throw new CloudApiException("The authorization callback must be an HTTP GET request.", "INVALID_CALLBACK");
        }

        return parts[1];
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var trimmed = query.TrimStart('?');
        if (string.IsNullOrEmpty(trimmed))
        {
            return result;
        }

        foreach (var segment in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = segment.IndexOf('=');
            var key = separator >= 0 ? segment[..separator] : segment;
            var value = separator >= 0 ? segment[(separator + 1)..] : string.Empty;

            result[UrlDecode(key)] = UrlDecode(value);
        }

        return result;
    }

    private static string UrlDecode(string value)
    {
        return Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));
    }

    private static async Task WriteHtmlResponseAsync(NetworkStream stream, int statusCode, string title, string message)
    {
        var html = $"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{HtmlEncode(title)}</title>
</head>
<body>
  <h1>{HtmlEncode(title)}</h1>
  <p>{HtmlEncode(message)}</p>
</body>
</html>
""";

        var body = Encoding.UTF8.GetBytes(html);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {title}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n" +
            $"Content-Length: {body.Length}\r\n\r\n");

        await stream.WriteAsync(header).ConfigureAwait(false);
        await stream.WriteAsync(body).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static string HtmlEncode(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);
    }

    public static Task OpenDefaultBrowserAsync(Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.ToString(),
            UseShellExecute = true,
        });

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _listener.Stop();
        _disposed = true;
    }
}
