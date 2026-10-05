using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;
using MonolithHarness.Core.Hosting;
using System.Text.Json;
using System.Text.Json.Nodes;

static class AcpChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "monolith-acp-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var originalRoot = PortableStorage.Root; PortableStorage.UseDatabase(Path.Combine(folder, "database.sqlite"));
        try
        {
            var trace = Path.Combine(folder, "trace with spaces.jsonl");
            var arguments = new List<string>();
            var executable = Environment.ProcessPath!;
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) arguments.Add(typeof(AcpChecks).Assembly.Location);
            arguments.Add("--acp-fake"); arguments.Add(trace);
            var provider = AcpProviders.Create(AcpProviders.Find("chatgpt-acp")!);
            provider.ExecutablePath = executable; provider.AcpArgumentsJson = JsonSerializer.Serialize(arguments);
            JsonArray Trace() => new(File.ReadAllLines(trace).Select(x => JsonNode.Parse(x)).ToArray());
            check(provider.IsAcp && provider.IsExternalAgent && !provider.IsOpenCode, "ACP distinguished from HTTP and OpenCode");
            check(AcpProviders.Presets.Count == 2 && AcpProviders.Create(AcpProviders.Presets[0]).Model == "default", "Antigravity and ChatGPT subscription presets");
            var start = AcpProviders.StartInfo(provider, folder);
            check(start.ArgumentList.Last() == trace && !start.UseShellExecute && start.CreateNoWindow, "ACP arguments preserve paths without a shell or visible window");
            check(!start.Environment.ContainsKey("OPENAI_API_KEY") && !start.Environment.ContainsKey("CODEX_API_KEY") && start.Environment["MODEL_PROVIDER"] == "openai", "ChatGPT connection excludes API billing and inherited gateways");
            var engine = new AcpEngine(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var models = await engine.ModelsAsync(provider, timeout.Token, authenticate: true);
            check(models.SequenceEqual(new[] { "model-a", "model-b" }) && provider.SupportsImages, "ACP model config options and image capability discovery");
            check(Trace().Any(x => x?["method"]?.ToString() == "authenticate" && x?["params"]?["methodId"]?.ToString() == "chat-gpt"), "Explicit ChatGPT authentication, not API key authentication");
            int approvals = 0; var updates = new List<GenerationUpdate>();
            var wire = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = "Speak French" },
                new JsonObject { ["role"] = "user", ["content"] = "Previous question" }, new JsonObject { ["role"] = "assistant", ["content"] = "Previous answer" },
                new JsonObject { ["role"] = "user", ["content"] = "Latest question" });
            var completion = await engine.PromptAsync(provider, wire, updates.Add, timeout.Token, "high",
                new(folder, "execute", true, (permission, ct) => { approvals++; check(permission.Kind == "execute" && permission.Title == "Run test", "ACP tool request details forwarded"); return Task.FromResult(true); }));
            check(completion.Message["content"]?.ToString() == "Bonjour monde", "ACP streamed answer assembled");
            check(completion.Message["reasoning_content"]?.ToString().Contains("Réflexion") == true && updates.Count >= 3, "ACP thoughts and activity stream into the reasoning bubble");
            check(approvals == 1 && Trace().Any(x => x?["result"]?["outcome"]?["optionId"]?.ToString() == "allow-one"), "ACP allow_once returned to the agent");
            check(Trace().Any(x => x?["method"]?.ToString() == "session/set_mode" && x?["params"]?["modeId"]?.ToString() == "agent"), "Execute mode selected explicitly");
            check(Trace().Any(x => x?["method"]?.ToString() == "session/set_config_option" && x?["params"]?["value"]?.ToString() == "high"), "Supported native reasoning effort selected");
            var sent = Trace().Last(x => x?["method"]?.ToString() == "session/prompt")!["params"]!["prompt"]!.ToJsonString();
            check(sent.Contains("Previous answer") && sent.Contains("Latest question") && sent.Contains("Speak French"), "Fresh ACP sessions replay local conversation history and instructions");
            approvals = 0;
            await engine.PromptAsync(provider, wire, _ => { }, timeout.Token, options: new(folder, "plan", true, (_, _) => { approvals++; return Task.FromResult(true); }));
            check(approvals == 0 && Trace().Last(x => x?["id"]?.ToString() == "permission-1")?["result"]?["outcome"]?["optionId"]?.ToString() == "reject-one", "Plan blocks writes and execution without asking for approval");
            check(Trace().Last(x => x?["method"]?.ToString() == "session/set_mode")?["params"]?["modeId"]?.ToString() == "read-only", "Read-only mode enforced for planning");
            await engine.PromptAsync(provider, wire, _ => { }, timeout.Token, options: new(folder, "chat", true, (_, _) => { approvals++; return Task.FromResult(true); }));
            check(approvals == 0, "Chat blocks external tool permission requests");
            using (var cancel = new CancellationTokenSource())
            {
                var task = engine.PromptAsync(provider, new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "[cancel]" }),
                    _ => cancel.Cancel(), cancel.Token, options: new(folder, "execute", true));
                try { await task; check(false, "Expected cancellation"); } catch (OperationCanceledException) { check(true, "ACP generation cancellation observed"); }
                check(Trace().Any(x => x?["method"]?.ToString() == "session/cancel"), "ACP cancellation notification delivered before process cleanup");
            }
            var image = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = new JsonArray(new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = "data:image/png;base64,AQID" } }) });
            check(AcpEngine.Content(image, true).Any(x => x?["type"]?.ToString() == "image" && x?["data"]?.ToString() == "AQID"), "ACP image block preserves base64 and MIME");
            try { AcpEngine.Content(image, false); check(false, "Expected unsupported image"); } catch (InvalidOperationException) { check(true, "Images rejected when the agent has no image capability"); }
            var antigravity = AcpProviders.Create(AcpProviders.Find("antigravity-acp")!);
            try { await engine.PromptAsync(antigravity, wire, _ => { }, timeout.Token, options: new(folder, "plan", true)); check(false, "Expected unsupported Plan"); }
            catch (InvalidOperationException) { check(true, "Antigravity unsupported restricted modes fail before launching"); }
            try { AcpProviders.Validate(new Provider { Kind = "chatgpt-acp", AcpArgumentsJson = "[1]" }); check(false, "Expected invalid arguments"); }
            catch (ArgumentException) { check(true, "ACP arguments require a JSON string array"); }
            try { AcpProviders.Validate(new Provider { Kind = "chatgpt-acp", ExecutablePath = "agent.cmd" }); check(false, "Expected shell rejection"); }
            catch (ArgumentException) { check(true, "Shell wrappers require an explicit executable instead"); }
            await using (var db = new HarnessDb(Path.Combine(folder, "database.sqlite")))
            {
                await db.InitializeAsync(); db.Providers.Add(provider); await db.SaveChangesAsync(); var id = provider.Id;
                db.ChangeTracker.Clear(); var saved = await db.Providers.SingleAsync(x => x.Id == id);
                check(saved.AcpArgumentsJson == provider.AcpArgumentsJson && saved.Kind == provider.Kind, "Migration persists ACP settings without changing existing providers");
                check(await db.Providers.CountAsync() == 3, "ACP migration preserves original provider rows");
            }
            var events = new List<JsonObject>(); var permissionRequests = 0;
            await using (var service = new HarnessService(Path.Combine(folder, "database.sqlite"), (_, _, _) => { permissionRequests++; return Task.FromResult<JsonNode?>(JsonValue.Create("once")); }, value => { events.Add(JsonSerializer.SerializeToNode(value, HarnessService.Json)!.AsObject()); return Task.CompletedTask; }))
            {
                await using var setup = new HarnessDb(Path.Combine(folder, "database.sqlite"));
                var chat = await setup.Chats.FirstAsync(); chat.InteractionMode = "agent"; chat.ExecutionMode = "execute"; chat.OrchestrationMode = "disabled"; await setup.SaveChangesAsync();
                await service.Dispatch("provider.save", new JsonObject { ["id"] = provider.Id, ["name"] = "ACP fake", ["kind"] = provider.Kind, ["baseUrl"] = "", ["model"] = "default", ["executablePath"] = provider.ExecutablePath, ["acpArgumentsJson"] = provider.AcpArgumentsJson, ["openCodeTools"] = true }, timeout.Token);
                await service.Dispatch("send", new JsonObject { ["chatId"] = chat.Id, ["providerId"] = provider.Id, ["text"] = "Hosted ACP conversation" }, timeout.Token);
                check(permissionRequests == 1 && events.Any(x => x["event"]?.ToString() == "stream" && x["text"]?.ToString() == "Bonjour monde"), "Shared GUI/CLI host routes ACP streaming and permissions");
                setup.ChangeTracker.Clear();
                check(await setup.Messages.AnyAsync(x => x.ChatId == chat.Id && x.Role == "assistant" && x.Content == "Bonjour monde" && x.State == "complete"), "Hosted ACP answer saved in local conversation history");
                var hosted = await setup.Providers.SingleAsync(x => x.Id == provider.Id);
                check(hosted.BaseUrl == "" && hosted.AcpArgumentsJson == provider.AcpArgumentsJson && hosted.ProtectedKey.Length == 0, "Hosted provider save accepts ACP settings without HTTP or API credentials");
            }
            check(completion.InputTokens == null && completion.OutputTokens == null && completion.CachedInputTokens == null, "ACP does not invent usage or cache statistics");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); PortableStorage.UseDatabase(Path.Combine(originalRoot, "database.sqlite")); Directory.Delete(folder, true); }
    }

    public static async Task Fake(string trace)
    {
        static Task Emit(JsonObject message) => Console.Out.WriteLineAsync(message.ToJsonString());
        static JsonObject Result(JsonNode? id, JsonObject result) => new() { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result };
        static JsonObject Update(string type, string text) => new() { ["jsonrpc"] = "2.0", ["method"] = "session/update", ["params"] = new JsonObject { ["sessionId"] = "session-1", ["update"] = new JsonObject {
            ["sessionUpdate"] = type, ["content"] = new JsonObject { ["type"] = "text", ["text"] = text } } } };
        JsonNode? promptId = null;
        while (await Console.In.ReadLineAsync() is { } line)
        {
            File.AppendAllText(trace, line + "\n"); var message = JsonNode.Parse(line)!; var id = message["id"]; var method = message["method"]?.ToString();
            if (method == "initialize") await Emit(Result(id, new() { ["protocolVersion"] = 1, ["agentCapabilities"] = new JsonObject { ["promptCapabilities"] = new JsonObject { ["image"] = true } },
                ["authMethods"] = new JsonArray(new JsonObject { ["id"] = "chat-gpt", ["name"] = "Sign in with ChatGPT" }) }));
            else if (method == "authenticate") await Emit(Result(id, new()));
            else if (method == "session/new") await Emit(Result(id, new() { ["sessionId"] = "session-1", ["modes"] = new JsonObject { ["availableModes"] = new JsonArray(new JsonObject { ["id"] = "read-only" }, new JsonObject { ["id"] = "agent" }) },
                ["configOptions"] = new JsonArray(new JsonObject { ["id"] = "model", ["category"] = "model", ["options"] = new JsonArray(new JsonObject { ["value"] = "model-a" }, new JsonObject { ["value"] = "model-b" }) },
                    new JsonObject { ["id"] = "effort", ["category"] = "thought_level", ["options"] = new JsonArray(new JsonObject { ["value"] = "high" }) }) }));
            else if (method is "session/set_mode" or "session/set_config_option") await Emit(Result(id, new()));
            else if (method == "session/prompt")
            {
                promptId = id?.DeepClone();
                if (message["params"]!["prompt"]!.ToJsonString().Contains("[cancel]")) { await Emit(Update("agent_thought_chunk", "Started")); continue; }
                await Emit(Update("agent_thought_chunk", "Réflexion"));
                await Emit(new() { ["jsonrpc"] = "2.0", ["id"] = "permission-1", ["method"] = "session/request_permission", ["params"] = new JsonObject {
                    ["sessionId"] = "session-1", ["toolCall"] = new JsonObject { ["title"] = "Run test", ["kind"] = "execute", ["rawInput"] = new JsonObject { ["command"] = "test" } },
                    ["options"] = new JsonArray(new JsonObject { ["optionId"] = "allow-one", ["kind"] = "allow_once" }, new JsonObject { ["optionId"] = "reject-one", ["kind"] = "reject_once" }) } });
            }
            else if (id?.ToString() == "permission-1")
            {
                await Emit(Update("agent_message_chunk", "Bonjour ")); await Emit(Update("agent_message_chunk", "monde")); await Emit(Result(promptId, new() { ["stopReason"] = "end_turn" }));
            }
            else if (method == "session/cancel") await Emit(Result(promptId, new() { ["stopReason"] = "cancelled" }));
            else if (id != null) await Emit(Result(id, new()));
        }
    }

    public static async Task LiveAntigravityAdapter()
    {
        var folder = Path.Combine(Path.GetTempPath(), "monolith-antigravity-adapter-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var originalRoot = PortableStorage.Root;
        var previous = Environment.GetEnvironmentVariable("MONOLITHHARNESS_FAKE_AGY_TRACE");
        var trace = Path.Combine(folder, "agy.jsonl");
        PortableStorage.UseDatabase(Path.Combine(folder, "database.sqlite")); Environment.SetEnvironmentVariable("MONOLITHHARNESS_FAKE_AGY_TRACE", trace);
        try
        {
            var executable = Path.Combine(Path.GetDirectoryName(typeof(AcpChecks).Assembly.Location)!, "MonolithHarness.Tests" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            if (!File.Exists(executable)) throw new FileNotFoundException("Fake agy requires the test apphost.");
            var provider = AcpProviders.Create(AcpProviders.Find("antigravity-acp")!); provider.Model = "model-a";
            provider.AcpArgumentsJson = JsonSerializer.Serialize(new[] { "--binary-path", executable });
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)); var engine = new AcpEngine();
            var models = await engine.ModelsAsync(provider, timeout.Token);
            if (!models.SequenceEqual(new[] { "default" })) throw new Exception("Antigravity adapter default model discovery failed.");
            var completion = await engine.PromptAsync(provider, new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Test adapter only" }),
                _ => { }, timeout.Token, options: new(folder, "execute", true));
            var invocations = File.ReadAllLines(trace).Select(x => JsonNode.Parse(x)).Where(x => x?["args"] != null).ToList();
            if (completion.Message["content"]?.ToString() != "Réponse Antigravity" || invocations.Count != 2 || invocations.Any(x => x!["args"]!.ToJsonString().Contains("dangerously-skip-permissions")) || !invocations.Last()!["args"]!.AsArray().Any(x => x?.ToString() == "--model=model-a"))
                throw new Exception("Antigravity adapter check failed: text=" + completion.Message["content"]?.ToString() + "; invocations=" + invocations.Count + "; args=" + invocations.LastOrDefault()?["args"]?.ToJsonString());
            Console.WriteLine("Live Antigravity ACP adapter: catalog fallback, streaming, model argument and retained permissions passed (fake agy, no Google login or paid request).");
        }
        finally { Environment.SetEnvironmentVariable("MONOLITHHARNESS_FAKE_AGY_TRACE", previous); PortableStorage.UseDatabase(Path.Combine(originalRoot, "database.sqlite")); Directory.Delete(folder, true); }
    }
    public static async Task FakeAgy(string trace, string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false); Console.InputEncoding = System.Text.Encoding.UTF8;
        File.AppendAllText(trace, JsonSerializer.Serialize(new { args }) + "\n");
        await Console.Out.WriteLineAsync("{\"event\":\"init\",\"conversation_id\":\"fake-agy\",\"permission_mode\":\"request-review\"}");
        while (await Console.In.ReadLineAsync() is { } line)
        {
            File.AppendAllText(trace, line + "\n");
            await Console.Out.WriteLineAsync("{\"event\":\"step_update\",\"step_update\":{\"conversation_id\":\"fake-agy\",\"step_index\":0,\"state\":\"DONE\",\"step_type\":\"agent_response\",\"text_delta\":\"Réponse Antigravity\"}}");
            await Console.Out.WriteLineAsync("{\"event\":\"result\",\"result\":{\"status\":\"SUCCESS\",\"response\":\"Réponse Antigravity\",\"conversation_id\":\"fake-agy\",\"duration_seconds\":0.1}}");
        }
    }
}
