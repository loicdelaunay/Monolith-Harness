using System.Text.Json.Nodes;
using MonolithHarness.Core;

namespace MonolithHarness.Cli;

public static class Headless
{
    public static async Task<int> RunAsync(CliOptions options)
    {
        if (options.Prompt.Length == 0 && Console.IsInputRedirected) options.Prompt = await Console.In.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(options.Prompt)) throw new ArgumentException("Prompt required: MonolithHarness run \"your request\"");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        CliClient? client = null;
        bool questionRequired = false;
        var outputLock = new object();
        var written = new Dictionary<int, string>();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            client = new(options, (name, parameters, _) => name == "permission"
                // Sensitive local tools stay denied; --allow-tools also approves the actions of a cloud run, which the user asked for explicitly.
                ? Task.FromResult<JsonNode?>(JsonValue.Create(options.Allow && parameters["cloud"]?.GetValue<bool>() == true ? "allow" : "deny"))
                : throw new NotSupportedException("Host operation unavailable in CLI: " + name), value =>
            {
                var node = CliClient.J(value);
                lock (outputLock)
                {
                    string kind = node["event"]?.GetValue<string>() ?? "";
                    if (kind == "started") timer.Restart();
                    if (kind == "done") { node["durationSeconds"] = timer.Elapsed.TotalSeconds; if (!options.Json) Console.Error.WriteLine($"Durée / Duration: {timer.Elapsed.TotalSeconds:0.#} s"); }
                    if (options.Json)
                    {
                        node.Remove("html"); if (node["message"] is JsonObject message) message.Remove("html");
                        Console.WriteLine(node.ToJsonString());
                    }
                    else if (kind == "stream")
                    {
                        int id = node["messageId"]!.GetValue<int>(); string text = node["text"]?.GetValue<string>() ?? "";
                        var previous = written.GetValueOrDefault(id, "");
                        if (text.StartsWith(previous, StringComparison.Ordinal)) Console.Write(TerminalText.Clean(text[previous.Length..]));
                        written[id] = text;
                    }
                    else if (kind == "message" && node["message"]?["role"]?.GetValue<string>() == "assistant")
                    {
                        var message = node["message"]!; int id = message["id"]!.GetValue<int>(); string text = message["content"]?.GetValue<string>() ?? "";
                        if (!written.ContainsKey(id)) { Console.WriteLine(TerminalText.Clean(text)); written[id] = text; }
                    }
                    else if (kind == "status") Console.Error.WriteLine(TerminalText.Clean(node["text"]?.GetValue<string>() ?? ""));
                    if (kind == "question") { questionRequired = true; cancellation.Cancel(); }
                }
                return Task.CompletedTask;
            });
            var initial = options.Cloud != null ? await OpenCloudAsync(client, options, cancellation.Token) : await client.Initialize(options, cancellation.Token);
            if (initial.ProviderId == 0) throw new InvalidOperationException(options.Cloud != null
                ? "No server provider: connect with 'MonolithHarness server login' (providers are synchronized at sign-in)."
                : "Configure a provider in interactive mode with /connect.");
            await client.Call("send", new { chatId = initial.ChatId, providerId = initial.ProviderId, text = options.Prompt }, cancellation.Token);
            if (!options.Json) Console.WriteLine();
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(questionRequired ? "An agent question needs an interactive session. Resume with --chat." : "Cancelled.");
            return questionRequired ? 3 : 130;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            if (client != null) await client.DisposeAsync();
        }
    }

    /// <summary>Selects (or creates) the conversation of a cloud project and a server provider; nothing is created for the current folder.</summary>
    static async Task<(WorkspaceSnapshot Snapshot, int ChatId, int ProviderId)> OpenCloudAsync(CliClient client, CliOptions options, CancellationToken ct)
    {
        await client.Service.Initialize();
        var opened = (await client.Call("server.project.open", new { id = options.Cloud }, ct))!;
        int projectId = opened["id"]!.GetValue<int>();
        var snapshot = await client.Snapshot(ct);
        var chatId = options.Chat ?? await client.CreateChat(projectId, ct);
        if (snapshot.Chats.All(c => c.Id != chatId)) snapshot = await client.Snapshot(ct);
        var link = client.ServerLink;
        var provider = options.Provider ?? snapshot.Providers.FirstOrDefault(p => link.Providers.Values.Contains(p.Id) && p.Name.EndsWith(MonolithHarness.Core.Remote.ServerSync.ServerProviderSuffix))?.Id
            ?? snapshot.Providers.FirstOrDefault(p => link.Providers.Values.Contains(p.Id))?.Id ?? 0;
        return (snapshot, chatId, provider);
    }
}
