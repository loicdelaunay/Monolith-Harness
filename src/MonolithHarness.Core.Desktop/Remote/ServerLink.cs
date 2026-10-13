using System.Text.Json;

namespace MonolithHarness.Core.Remote;

public sealed class CloudProjectLink
{
    public string ServerProjectId { get; set; } = "";
    public string Access { get; set; } = "owner";
}

public sealed class CloudChatLink
{
    public string ServerProjectId { get; set; } = "";
    public int ServerChatId { get; set; }
    /// <summary>The server's update stamp of the conversation when its messages were last pulled: a different stamp means new content.</summary>
    public DateTime? PulledUtc { get; set; }
    /// <summary>The title the server had at the last synchronization (to tell a server rename from a local one).</summary>
    public string? ServerTitle { get; set; }
}

/// <summary>
/// Everything a client remembers about its (optional) server: address, account, protected token, and the links between local
/// entities and their server counterparts. Beside the database (portable); the password is never stored.
/// </summary>
public sealed class ServerLinkData
{
    public string Url { get; set; } = "";
    /// <summary>The "accept HTTP" option: lets a non-local server be reached without TLS.</summary>
    public bool AllowHttp { get; set; }
    public string Email { get; set; } = "";
    /// <summary>The account's id and display name (shown next to what it writes in a shared project).</summary>
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>The session token protected like provider keys, as Base64.</summary>
    public string ProtectedToken { get; set; } = "";
    public DateTime? TokenExpiresUtc { get; set; }
    public string ServerName { get; set; } = "";
    public string ServerVersion { get; set; } = "";
    /// <summary>Server provider id → local provider id.</summary>
    public Dictionary<string, int> Providers { get; set; } = [];
    /// <summary>Local project id → cloud project.</summary>
    public Dictionary<int, CloudProjectLink> Projects { get; set; } = [];
    /// <summary>Local chat id → cloud conversation.</summary>
    public Dictionary<int, CloudChatLink> Chats { get; set; } = [];
    /// <summary>Local chat id → shared conversation id (the "local shared" mode).</summary>
    public Dictionary<int, string> Shares { get; set; } = [];
    /// <summary>Minutes between two complete resynchronizations with the server; 0 = never (changes still arrive live). Default 5.</summary>
    public int SyncMinutes { get; set; } = 5;
    /// <summary>The sidebar shows the server's projects and shared conversations instead of the local ones.</summary>
    public bool CloudView { get; set; }
    /// <summary>
    /// The server mode is on. Off, nothing is sent to or received from the server (no synchronization, no live channel, no cloud view, no shared provider,
    /// no cloud request) but the address, the account and the links are kept, so that turning it back on is one click.
    /// </summary>
    public bool Enabled { get; set; } = true;
    /// <summary>The user shares the speed and consumption of their model calls with the server (off by default).</summary>
    public bool ShareUsage { get; set; }
    /// <summary>Identifies this installation among the user's devices, so that the same call is never counted twice.</summary>
    public string DeviceId { get; set; } = "";
    /// <summary>Id of the last consumption record already sent; sending resumes after it.</summary>
    public long UsageCursor { get; set; }
    /// <summary>Records completed before this moment are not sent (the first sending covers the last 30 days).</summary>
    public DateTime? UsageSinceUtc { get; set; }
    public long UsageSent { get; set; }
    public DateTime? UsageLastSentUtc { get; set; }
    /// <summary>The message of the moment the user closed (its stamp): it does not come back until the administrator changes it.</summary>
    public string DismissedAnnouncement { get; set; } = "";

    public bool Configured => Url.Length > 0;
    public bool SignedIn => ProtectedToken.Length > 0;
    /// <summary>Something is exchanged with the server: configured, signed in and switched on.</summary>
    public bool Active => Configured && SignedIn && Enabled;
}

/// <summary>Atomic, thread-safe persistence of <see cref="ServerLinkData"/>.</summary>
public sealed class ServerLinkStore(string path)
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    readonly object gate = new();

    public static string PathFor(string database) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(database))!, "server-link.json");

    public ServerLinkData Read()
    {
        lock (gate) return Load();
    }

    /// <summary>Applies a change and persists it; returns the new state.</summary>
    public ServerLinkData Update(Action<ServerLinkData> change)
    {
        lock (gate)
        {
            var data = Load();
            change(data);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(data, Options));
            File.Move(temporary, path, true);
            return data;
        }
    }

    ServerLinkData Load()
    {
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<ServerLinkData>(File.ReadAllText(path), Options) ?? new(); }
        catch (Exception ex) when (ex is JsonException or IOException) { return new(); }
    }
}
