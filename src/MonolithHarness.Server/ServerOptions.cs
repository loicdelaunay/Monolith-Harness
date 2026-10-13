using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonolithHarness.Server;

/// <summary>Listening configuration. Precedence: defaults, server.json, MONOLITH_SERVER_* variables, command line.</summary>
public sealed class ServerOptions
{
    public const int DefaultPort = 5800;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Loopback by default: remote exposure is an explicit choice ("0.0.0.0"), ideally behind HTTPS.</summary>
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = DefaultPort;
    public int? HttpsPort { get; set; }
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }
    /// <summary>Trust X-Forwarded-* headers from a TLS-terminating reverse proxy.</summary>
    public bool BehindProxy { get; set; }
    public bool RedirectToHttps { get; set; }
    /// <summary>
    /// What the server does about new versions when it starts: "ask" (propose it on the console, Y/N), "auto" (install it and restart without asking),
    /// "notify" (only say so, here and in the web interface) or "off" (never look). Without a console, "ask" behaves like "notify".
    /// </summary>
    public string UpdateMode { get; set; } = "ask";
    /// <summary>Seconds the startup check may take; a slow or absent network never delays the start longer.</summary>
    public int UpdateTimeoutSeconds { get; set; } = 5;
    /// <summary>Seconds the console waits for the Y/N answer; no answer means "no" and the server starts normally.</summary>
    public int UpdatePromptSeconds { get; set; } = 20;
    /// <summary>Where releases are listed (GitHub's API for the project's releases by default).</summary>
    public string UpdateFeed { get; set; } = MonolithHarness.Core.GitHubUpdates.Api + "?per_page=100";

    [JsonIgnore] public IPAddress Address => ParseHost(Host);

    public static IPAddress ParseHost(string host) => host.Trim() switch
    {
        "" or "localhost" => IPAddress.Loopback,
        "*" or "+" or "0.0.0.0" => IPAddress.Any,
        "::" => IPAddress.IPv6Any,
        var value => IPAddress.TryParse(value, out var address) ? address : throw new ArgumentException("Invalid host address: " + value)
    };

    public void Validate()
    {
        _ = Address;
        if (Port is < 0 or > 65535) throw new ArgumentException("Port must be between 0 and 65535.");
        if (HttpsPort is < 0 or > 65535) throw new ArgumentException("HTTPS port must be between 0 and 65535.");
        if (HttpsPort != null && (string.IsNullOrWhiteSpace(CertificatePath) || !File.Exists(CertificatePath)))
            throw new ArgumentException("HTTPS needs a PKCS#12 certificate: set certificatePath in server.json or pass --cert.");
        if (RedirectToHttps && HttpsPort == null) throw new ArgumentException("redirectToHttps requires an HTTPS port.");
        UpdateMode = (UpdateMode ?? "ask").Trim().ToLowerInvariant();
        if (UpdateMode is not ("ask" or "auto" or "notify" or "off")) throw new ArgumentException("updateMode must be ask, auto, notify or off.");
        if (UpdateTimeoutSeconds is < 1 or > 120) throw new ArgumentException("updateTimeoutSeconds must be between 1 and 120.");
        if (UpdatePromptSeconds is < 3 or > 600) throw new ArgumentException("updatePromptSeconds must be between 3 and 600.");
        if (!Uri.TryCreate(UpdateFeed, UriKind.Absolute, out var feed) || feed.Scheme is not ("https" or "http"))
            throw new ArgumentException("updateFeed must be an http(s) address.");
        // A plain-HTTP feed would let anyone on the path offer a release: only loopback (tests, a local mirror) may use it.
        if (feed.Scheme == "http" && !feed.IsLoopback) throw new ArgumentException("updateFeed must use HTTPS (HTTP is only accepted on this machine).");
    }

    public static ServerOptions Load(ServerPaths paths, IReadOnlyList<string> args)
    {
        var options = new ServerOptions();
        if (File.Exists(paths.Config))
            options = JsonSerializer.Deserialize<ServerOptions>(File.ReadAllText(paths.Config), Json) ?? options;
        static string? Env(string name) => Environment.GetEnvironmentVariable("MONOLITH_SERVER_" + name) is { Length: > 0 } value ? value : null;
        if (Env("HOST") is { } host) options.Host = host;
        if (Env("PORT") is { } port) options.Port = ParsePort(port);
        if (Env("HTTPS_PORT") is { } httpsPort) options.HttpsPort = ParsePort(httpsPort);
        if (Env("CERT") is { } cert) options.CertificatePath = cert;
        if (Env("CERT_PASSWORD") is { } password) options.CertificatePassword = password;
        if (Env("UPDATE") is { } update) options.UpdateMode = update;
        if (Env("UPDATE_FEED") is { } feed) options.UpdateFeed = feed;
        for (var i = 0; i < args.Count; i++)
        {
            string Next() => i + 1 < args.Count ? args[++i] : throw new ArgumentException(args[i] + " requires a value.");
            switch (args[i])
            {
                case "--port": options.Port = ParsePort(Next()); break;
                case "--host": options.Host = Next(); break;
                case "--https-port": options.HttpsPort = ParsePort(Next()); break;
                case "--cert": options.CertificatePath = Next(); break;
                case "--cert-password": options.CertificatePassword = Next(); break;
                case "--behind-proxy": options.BehindProxy = true; break;
                case "--redirect-https": options.RedirectToHttps = true; break;
                case "--update": options.UpdateMode = Next(); break;
                case "--no-update-check": options.UpdateMode = "off"; break;
                case "--update-feed": options.UpdateFeed = Next(); break;
            }
        }
        options.Validate();
        return options;
    }

    static int ParsePort(string value) => int.TryParse(value, out var port) && port is >= 0 and <= 65535 ? port : throw new ArgumentException("Invalid port: " + value);

    /// <summary>Writes the default configuration the first time so administrators can find and edit it.</summary>
    public static void EnsureFile(ServerPaths paths)
    {
        if (File.Exists(paths.Config)) return;
        File.WriteAllText(paths.Config, JsonSerializer.Serialize(new ServerOptions(), Json));
    }
}
