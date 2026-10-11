using System.Net;

namespace MonolithHarness.Core.Remote;

/// <summary>A failure reported by the Monolith Harness Server (stable <see cref="Code"/>) or by the connection to it.</summary>
public sealed class ServerException(string code, string message, int status = 0, int? retryAfterSeconds = null) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>Address normalization and the HTTP policy: remote servers need HTTPS unless the user explicitly accepts plain HTTP.</summary>
public static class ServerUrl
{
    public static bool IsLoopback(Uri uri) => uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// "myserver:5800" becomes https://myserver:5800/. Plain http is refused for a non-local server unless
    /// <paramref name="allowHttp"/> is set (the "accept HTTP" option of the clients).
    /// </summary>
    public static Uri Parse(string? input, bool allowHttp)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0) throw new ServerException("url_required", "Adresse du serveur requise / Server address required.");
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ServerException("invalid_url", "Adresse invalide : utilisez https://hôte:port (sans identifiants) / Invalid address: use https://host:port (no credentials).");
        var builder = new UriBuilder(uri) { Path = "/", Query = "", Fragment = "" };
        var normalized = builder.Uri;
        if (normalized.Scheme == "http" && !allowHttp && !IsLoopback(normalized))
            throw new ServerException("insecure_http",
                "Connexion HTTP non chiffrée refusée. Utilisez HTTPS, ou activez « Accepter HTTP » dans les réglages du serveur / Unencrypted HTTP refused. Use HTTPS, or enable “Accept HTTP” in the server settings.");
        return normalized;
    }

    public static Uri WebSocket(Uri baseUri)
    {
        var builder = new UriBuilder(baseUri) { Scheme = baseUri.Scheme == "https" ? "wss" : "ws", Path = "/ws" };
        return builder.Uri;
    }

    public static string Describe(Uri uri) => uri.GetLeftPart(UriPartial.Authority);

    /// <summary>Same-origin check used before sending a token to an address read from storage.</summary>
    public static bool SameServer(Uri a, Uri b) => Uri.Compare(a, b, UriComponents.SchemeAndServer | UriComponents.Port, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
}
