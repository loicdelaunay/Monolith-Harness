using System.Text;

namespace MonolithHarness.Core.Remote;

public sealed record ServerStatus(bool Configured, string Url, bool AllowHttp, string Email, bool SignedIn, bool Reachable, string ServerName, string Version,
    Dictionary<string, bool> Features, ServerUser? User, bool PasswordChangeSuggested, DateTime? TokenExpiresUtc, string Error, string ErrorCode);

/// <summary>
/// The optional link of a client (Desktop, CLI) to a Monolith Harness Server: address, account, protected token, REST client and
/// real-time socket. Without configuration nothing here runs and the application stays fully local.
/// </summary>
public sealed class ServerConnection : IAsyncDisposable
{
    readonly Func<string, byte[]> protect;
    readonly Func<byte[], string> unprotect;
    readonly HttpClient http;
    readonly object gate = new();
    readonly Dictionary<string, List<Func<ServerEvent, Task>>> handlers = [];
    ServerApi? api;
    ServerSocket? socket;
    Uri? socketFor;

    public ServerConnection(ServerLinkStore store, Func<string, byte[]> protect, Func<byte[], string> unprotect, HttpClient? http = null)
    {
        Store = store;
        this.protect = protect;
        this.unprotect = unprotect;
        this.http = http ?? ServerApi.CreateClient();
    }

    public ServerLinkStore Store { get; }
    public ServerLinkData Link => Store.Read();
    public bool Configured => Link.Configured;
    public bool SignedIn => Link.SignedIn;

    /// <summary>Raised when the server stops accepting the stored token (expired or revoked).</summary>
    public event Action? SessionLost;

    /// <summary>Called after the real-time connection is (re)established, before new events are delivered.</summary>
    public event Func<Task>? RealtimeConnected;

    public string? Token()
    {
        var protectedToken = Link.ProtectedToken;
        if (protectedToken.Length == 0) return null;
        try { return unprotect(Convert.FromBase64String(protectedToken)); }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException or InvalidOperationException) { return null; }
    }

    /// <summary>The REST client for the configured address.</summary>
    public ServerApi Api
    {
        get
        {
            var link = Link;
            if (!link.Configured) throw new ServerException("not_configured", "Aucun serveur configuré / No server configured.");
            var uri = ServerUrl.Parse(link.Url, link.AllowHttp);
            lock (gate)
            {
                if (api == null || !ServerUrl.SameServer(api.BaseUri, uri)) api = new ServerApi(http, uri, Token);
                return api;
            }
        }
    }

    // ---- Connection and account ----

    /// <summary>Checks the address (HTTPS policy included), then stores it. A different server forgets the previous account and links.</summary>
    public async Task<ServerInfo> ConnectAsync(string url, bool allowHttp, CancellationToken ct = default)
    {
        var uri = ServerUrl.Parse(url, allowHttp);
        var probe = new ServerApi(http, uri, () => null);
        var info = await probe.InfoAsync(ct);
        if (info.ApiVersion != 1) throw new ServerException("incompatible", $"Version d'API non prise en charge ({info.ApiVersion}) / Unsupported API version.");
        var changed = !string.Equals(Link.Url, uri.ToString(), StringComparison.OrdinalIgnoreCase);
        Store.Update(link =>
        {
            if (changed)
            {
                link.Email = ""; link.ProtectedToken = ""; link.TokenExpiresUtc = null;
                link.Providers.Clear(); link.Projects.Clear(); link.Chats.Clear(); link.Shares.Clear();
            }
            link.Url = uri.ToString();
            link.AllowHttp = allowHttp;
            link.ServerName = info.Name;
            link.ServerVersion = info.Version;
        });
        if (changed) await StopRealtimeAsync();
        return info;
    }

    public async Task<ServerLogin> LoginAsync(string email, string password, string label, CancellationToken ct = default)
    {
        var login = await Api.LoginAsync(email, password, label, ct);
        Store.Update(link =>
        {
            link.Email = login.User.Email;
            link.ProtectedToken = Convert.ToBase64String(protect(login.Token));
            link.TokenExpiresUtc = login.ExpiresUtc;
        });
        return login;
    }

    /// <summary>Signs out on the server (best effort) and forgets the token. Links to cloud projects stay for a later sign-in.</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (Link.SignedIn)
            try { await Api.LogoutAsync(ct); } catch (ServerException) { }
        Store.Update(link => { link.ProtectedToken = ""; link.TokenExpiresUtc = null; });
        await StopRealtimeAsync();
    }

    public async Task<ServerStatus> StatusAsync(CancellationToken ct = default)
    {
        var link = Link;
        if (!link.Configured) return new(false, "", false, link.Email, false, false, "", "", [], null, false, null, "", "");
        ServerInfo? info = null;
        ServerUser? user = null;
        string error = "", code = "";
        try
        {
            info = await Api.InfoAsync(ct);
            if (link.SignedIn)
            {
                try { user = await Api.MeAsync(ct); }
                catch (ServerException ex) when (ex.Status == 401)
                {
                    Store.Update(l => { l.ProtectedToken = ""; l.TokenExpiresUtc = null; });
                    SessionLost?.Invoke();
                    error = "Session expirée ou révoquée : reconnectez-vous / Session expired or revoked: sign in again."; code = "session_lost";
                }
            }
        }
        catch (ServerException ex) { error = ex.Message; code = ex.Code; }
        var now = Link;
        return new(true, now.Url, now.AllowHttp, now.Email, now.SignedIn, info != null, info?.Name ?? now.ServerName, info?.Version ?? now.ServerVersion,
            info?.Features ?? [], user, user?.PasswordChangePending ?? false, now.TokenExpiresUtc, error, code);
    }

    // ---- Real time ----

    /// <summary>Starts the real-time connection when signed in (idempotent).</summary>
    public ServerSocket Realtime()
    {
        var link = Link;
        if (!link.SignedIn) throw new ServerException("not_signed_in", "Connexion au serveur requise / Sign in to the server first.");
        var uri = ServerUrl.Parse(link.Url, link.AllowHttp);
        lock (gate)
        {
            if (socket != null && socketFor != null && ServerUrl.SameServer(socketFor, uri)) return socket;
            socket?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            socket = new ServerSocket(uri, Token, Dispatch);
            socket.Connected += async () => { if (RealtimeConnected is { } handler) await handler(); };
            socketFor = uri;
            socket.Start();
            return socket;
        }
    }

    public async Task StopRealtimeAsync()
    {
        ServerSocket? current;
        lock (gate) { current = socket; socket = null; socketFor = null; }
        if (current != null) await current.DisposeAsync();
    }

    /// <summary>Delivers events of <paramref name="topic"/> (or, for "user:", of the account topic) until the returned object is disposed.</summary>
    public async Task<IDisposable> WatchAsync(string topic, Func<ServerEvent, Task> handler, bool subscribe = true)
    {
        lock (gate)
        {
            if (!handlers.TryGetValue(topic, out var list)) handlers[topic] = list = [];
            list.Add(handler);
        }
        var current = Realtime();
        if (subscribe) await current.SubscribeAsync(topic);
        await current.WaitConnectedAsync(TimeSpan.FromSeconds(10));
        return new Watch(() =>
        {
            bool last;
            lock (gate)
            {
                var found = handlers.TryGetValue(topic, out var list);
                if (found) list!.Remove(handler);
                last = found && list!.Count == 0;
                if (last) handlers.Remove(topic);
            }
            if (last && subscribe && socket is { } open) _ = open.UnsubscribeAsync(topic);
        });
    }

    async Task Dispatch(ServerEvent evt)
    {
        List<Func<ServerEvent, Task>> targets;
        lock (gate)
        {
            targets = [];
            if (handlers.TryGetValue(evt.Topic, out var exact)) targets.AddRange(exact);
            // The account topic carries the user's id; callers register "user" for it.
            if (evt.Topic.StartsWith("user:", StringComparison.Ordinal) && handlers.TryGetValue("user", out var user)) targets.AddRange(user);
        }
        foreach (var handler in targets)
        {
            try { await handler(evt); }
            catch (Exception ex) when (ex is not OperationCanceledException) { AppLog.Write(AppLogLevel.Warning, "server.event_failed", ex); }
        }
    }

    sealed class Watch(Action dispose) : IDisposable
    {
        int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        await StopRealtimeAsync();
        http.Dispose();
    }

    /// <summary>Test helper and diagnostics: description of what a token looks like, never the token itself.</summary>
    public static string Mask(string? token) => string.IsNullOrEmpty(token) ? "" : token[..Math.Min(4, token.Length)] + new string('•', 8);
}
