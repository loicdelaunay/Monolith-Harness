using System.Text.Json.Nodes;
using MonolithHarness.Core;
using MonolithHarness.Core.Remote;

namespace MonolithHarness.Cli;

/// <summary><c>MonolithHarness server ...</c>: the optional connection to a Monolith Harness Server, without the interactive interface.</summary>
public static class ServerCommand
{
    const string Usage = """
        MonolithHarness server <command> [options]

          status                         Connection, account, synchronization state and problems
          connect <url> [--allow-http]   Check and store the server address (HTTPS expected; --allow-http accepts plain HTTP)
          login <e-mail>                 Sign in (password: prompt, --password-stdin, or MONOLITH_SERVER_PASSWORD)
          logout                         Sign out and remove the server's providers
          sync                           Synchronize now (providers, cloud projects, shared conversations)
          interval <never|1|5|60|1440>   Minutes between two complete resynchronizations (default 5; live changes always apply)
          on | off                       Turn the server mode on or off (off: nothing is exchanged; the account and links are kept)
          share-usage <on|off>           Share the speed and consumption of your model calls with the server (numbers only; off by default)
          erase-usage                    Erase on the server everything this account shared
          dismiss                        Hide the server message of the moment until it changes
          shares                         Conversations shared with the account (and the public ones)
          join <code>                    Follow a shared conversation from its sharing code and print what the host shows
          projects                       Cloud projects of the account
          open <name|id>                 Mirror a cloud project locally
          new <name>                     Create a cloud project
          forget                         Forget the server completely

        Add --json for machine-readable output and --database PATH to use another portable folder.
        Without a server the CLI stays fully local.
        """;

    public static async Task<int> RunAsync(CliOptions options)
    {
        var args = new List<string>();
        for (var i = 0; i < options.ServerArgs.Length; i++)
        {
            if (options.ServerArgs[i] == "--database") { i++; continue; }
            if (options.ServerArgs[i] is "--json" or "--allow-http" or "--password-stdin") continue;
            args.Add(options.ServerArgs[i]);
        }
        bool json = options.ServerArgs.Contains("--json"), allowHttp = options.ServerArgs.Contains("--allow-http"), passwordStdin = options.ServerArgs.Contains("--password-stdin");
        if (args.Count == 0 || args[0] is "-h" or "--help" or "help") { Console.WriteLine(Usage); return args.Count == 0 && !options.ServerArgs.Contains("--help") ? 2 : 0; }
        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToList();
        await using var client = new CliClient(options, (name, _, _) => throw new NotSupportedException("Host operation unavailable in CLI: " + name), _ => Task.CompletedTask);
        await client.Service.Initialize();
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        var ct = cancellation.Token;
        try
        {
            switch (command)
            {
                case "status":
                    return Print(json, await client.Call("server.status", ct: ct), Describe);
                case "connect":
                    if (rest.Count != 1) throw new ArgumentException("Usage: server connect <url> [--allow-http]");
                    return Print(json, await client.Call("server.connect", new { url = rest[0], allowHttp }, ct), Describe);
                case "login":
                    if (rest.Count != 1) throw new ArgumentException("Usage: server login <e-mail>");
                    var result = await client.Call("server.login", new { email = rest[0], password = ReadPassword(passwordStdin), label = "CLI " + Environment.MachineName }, ct);
                    if (!json && result!["passwordChangeSuggested"]?.GetValue<bool>() == true) Console.Error.WriteLine("This password is temporary: change it from the interactive CLI (/server) or the server web interface.");
                    return Print(json, result, Describe);
                case "logout": return Print(json, await client.Call("server.logout", ct: ct), Describe);
                case "on" or "off":
                    return Print(json, await client.Call("server.enable", new { enabled = command == "on" }, ct), node => command == "on" ? "Server mode on." : "Server mode off: nothing is exchanged with the server (the account and links are kept; `server on` resumes).");
                case "share-usage":
                {
                    var choice = rest.Count == 1 ? rest[0].ToLowerInvariant() : "";
                    if (choice is not ("on" or "off")) throw new ArgumentException("Usage: server share-usage <on|off>");
                    return Print(json, await client.Call("server.usage.share", new { enabled = choice == "on" }, ct), node => choice == "on"
                        ? "Sharing your usage statistics: the last 30 days and then each model call (provider, model, tokens, duration, speed; never any content)."
                        : "Sharing stopped. What was already sent stays on the server until `server erase-usage`.");
                }
                case "erase-usage": return Print(json, await client.Call("server.usage.erase", ct: ct), node => $"{node!["deleted"]} measurement(s) erased from the server.");
                case "dismiss": return Print(json, await client.Call("server.announcement.dismiss", ct: ct), _ => "Message hidden until the administrator publishes another one.");
                case "forget": return Print(json, await client.Call("server.disconnect", ct: ct), Describe);
                case "sync":
                {
                    var done = await client.Call("server.sync.now", ct: ct);
                    return Print(json, done, node => DescribeSync(node!["state"]));
                }
                case "interval":
                {
                    if (rest.Count != 1) throw new ArgumentException("Usage: server interval <never|1|5|60|1440>");
                    var minutes = rest[0].ToLowerInvariant() is "never" or "0" ? 0 : int.TryParse(rest[0], out var value) ? value : -1;
                    if (minutes is not (0 or 1 or 5 or 60 or 1440)) throw new ArgumentException("Choose never, 1, 5, 60 or 1440 minutes.");
                    return Print(json, await client.Call("server.sync.interval", new { minutes }, ct), node => "Full resynchronization: " + (node!["minutes"]!.GetValue<int>() == 0 ? "never (live changes still apply)" : "every " + node["minutes"] + " min"));
                }
                case "shares":
                {
                    var mine = (await client.Call("server.shares", ct: ct))!.AsArray().Where(x => x!["isHost"]?.GetValue<bool>() != true && x["revokedUtc"] is null);
                    var open = (await client.Call("server.shares.public", ct: ct))!.AsArray();
                    var all = new JsonArray(mine.Select(x => (JsonNode?)x!.DeepClone()).Concat(open.Select(x => (JsonNode?)x!.DeepClone())).ToArray());
                    return Print(json, all, node => node!.AsArray().Count == 0 ? "No conversation is shared with this account." : string.Join('\n', node.AsArray().Select(x => $"{x!["id"]}  {x["title"]}  ({x["hostEmail"]}){(x["live"]?.GetValue<bool>() == true ? "  live" : "")}{(x["isPublic"]?.GetValue<bool>() == true ? "  public" : "")}")));
                }
                case "join":
                    if (rest.Count == 0) throw new ArgumentException("Usage: server join <code>");
                    return Print(json, await client.Call("server.share.join", new { code = string.Join("", rest) }, ct), DescribeShare);
                case "projects":
                    return Print(json, await client.Call("server.projects", ct: ct), node => string.Join('\n', node!.AsArray().Select(x => $"{x!["id"]}  {x["name"]}  ({x["access"]}){(x["localProjectId"] is null ? "" : "  [local]")}")));
                case "open":
                    if (rest.Count == 0) throw new ArgumentException("Usage: server open <name|id>");
                    return Print(json, await client.Call("server.project.open", new { id = string.Join(' ', rest) }, ct), node => $"Cloud project mirrored locally: {node!["name"]} (#{node["id"]})");
                case "new":
                    if (rest.Count == 0) throw new ArgumentException("Usage: server new <name>");
                    return Print(json, await client.Call("server.project.create", new { name = string.Join(' ', rest) }, ct), node => $"Cloud project created: {node!["name"]} (#{node["id"]})");
                default: throw new ArgumentException("Unknown server command: " + command + "\n" + Usage);
            }
        }
        catch (InvalidOperationException ex) when (ex.InnerException is ServerException server)
        {
            Console.Error.WriteLine(TerminalText.Clean(server.Message));
            return server.Code is "unreachable" or "timeout" ? 4 : 1;
        }
    }

    static int Print(bool json, JsonNode? node, Func<JsonNode?, string> text)
    {
        Console.WriteLine(json ? node?.ToJsonString() ?? "null" : text(node));
        return 0;
    }

    /// <summary>What a shared conversation shows right now (read-only), as plain text.</summary>
    static string DescribeShare(JsonNode? node)
    {
        var lines = new List<string> { $"{node?["title"]} · {node?["share"]?["hostEmail"]} · " + (node?["closed"] is { } closed && closed.ToString().Length > 0 ? closed.ToString() : node?["online"]?.GetValue<bool>() == true ? "live" : "host offline") };
        foreach (var message in node?["messages"]?.AsArray() ?? []) lines.Add($"\n[{message!["role"]}] {message["content"]}");
        foreach (var comment in node?["comments"]?.AsArray() ?? []) lines.Add($"\n💬 {comment!["author"]}: {comment["text"]}");
        return string.Join('\n', lines);
    }

    static string DescribeSync(JsonNode? state)
    {
        if (state == null) return "";
        var lines = new List<string>();
        if (state["announcement"] is JsonObject note) lines.Add($"[{note["level"]}] Server message: {TerminalText.Clean(note["text"]?.ToString() ?? "")}");
        if (state["shareUsage"]?.GetValue<bool>() == true) lines.Add($"Sharing usage statistics · {state["usageSent"]} measurement(s) sent");
        lines.AddRange(new[]
        {
            $"State: {state["state"]}{(state["realtime"]?.GetValue<bool>() == true ? " · real time on" : "")} · last sync: {(state["lastSyncUtc"] is { } last ? DateTime.Parse(last.ToString()).ToLocalTime().ToString("g") : "never")} · full resync every {(state["minutes"]?.GetValue<int>() == 0 ? "— (never)" : state["minutes"] + " min")}",
            $"{state["providers"]} provider(s) · {state["projects"]} cloud project(s) · {state["sharedWithMe"]} shared with you · {state["publicShares"]} public"
        });
        foreach (var issue in state["issues"]?.AsArray() ?? []) lines.Add($"  [{issue!["level"]}] {issue["area"]}: {issue["message"]}");
        return string.Join('\n', lines);
    }

    static string Describe(JsonNode? node)
    {
        // Depending on the command the status is the node itself or nested one or two levels down.
        var status = node?["configured"] != null ? node : node?["status"]?["configured"] != null ? node["status"] : node?["status"]?["status"] ?? node;
        node = node?["status"]?["summary"] != null ? node["status"] : node;
        if (status?["configured"]?.GetValue<bool>() != true) return "No server configured: the CLI is fully local.";
        var providers = (node?["summary"]?["providers"] as JsonArray)?.Count ?? 0;
        return $"{status["serverName"]} {status["version"]} · {status["url"]}\n"
            + (status["signedIn"]?.GetValue<bool>() == true ? $"Signed in as {status["email"]} · {providers} shared provider(s)" : "Not signed in")
            + (status["reachable"]?.GetValue<bool>() == false ? "\nServer unreachable: " + status["error"] : "")
            + (status["errorCode"]?.ToString() == "session_lost" ? "\n" + status["error"] : "");
    }

    /// <summary>The password never travels on the command line: stdin, an environment variable or a masked prompt.</summary>
    static string ReadPassword(bool fromStdin)
    {
        if (fromStdin) return Console.In.ReadLine() ?? throw new ArgumentException("No password on stdin.");
        if (Environment.GetEnvironmentVariable("MONOLITH_SERVER_PASSWORD") is { Length: > 0 } variable) return variable;
        if (Console.IsInputRedirected) return Console.In.ReadLine() ?? throw new ArgumentException("No password on stdin.");
        Console.Error.Write("Password: ");
        var password = new System.Text.StringBuilder();
        while (Console.ReadKey(true) is var key && key.Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace) { if (password.Length > 0) password.Length--; }
            else if (!char.IsControl(key.KeyChar)) password.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        return password.ToString();
    }
}
