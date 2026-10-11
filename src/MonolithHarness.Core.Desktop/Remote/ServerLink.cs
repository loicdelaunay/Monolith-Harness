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

    public bool Configured => Url.Length > 0;
    public bool SignedIn => ProtectedToken.Length > 0;
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
