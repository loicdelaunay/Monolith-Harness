using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

/// <summary>ACP v1 over newline-delimited JSON-RPC stdio. Each turn replays the saved history into a fresh session.</summary>
public sealed class AcpEngine
{
    public async Task ProbeAsync(Provider provider, CancellationToken ct)
    {
        var directory = Path.Combine(PortableStorage.Folder("AcpWorkspaces"), "connection-test"); Directory.CreateDirectory(directory);
        await using var client = new Client(AcpProviders.StartInfo(provider, directory));
        await client.Initialize(ct);
        if (provider.Kind == "chatgpt-acp") client.CheckChatGptAuthentication();
    }
    public async Task<List<string>> ModelsAsync(Provider provider, CancellationToken ct, bool authenticate = false)
    {
        var directory = Path.Combine(PortableStorage.Folder("AcpWorkspaces"), "connection-test");
        Directory.CreateDirectory(directory);
        await using var client = new Client(AcpProviders.StartInfo(provider, directory));
        await client.Initialize(ct);
        if (authenticate) await client.AuthenticateChatGpt(provider, ct);
        var session = await client.NewSession(directory, ct);
        var models = new List<string>();
        if (session["models"]?["availableModels"] is JsonArray oldModels)
            models.AddRange(oldModels.Select(x => x?["modelId"]?.ToString() ?? ""));
        if (session["configOptions"] is JsonArray config)
            foreach (var option in config.Where(x => x?["category"]?.ToString() == "model")) CollectOptions(option?["options"] as JsonArray, models);
        var current = session["models"]?["currentModelId"]?.ToString();
        if (!string.IsNullOrWhiteSpace(current)) models.Add(current);
        if (models.Count == 0) models.Add(AcpProviders.DefaultModel);
        provider.SupportsImages = client.SupportsImages;
        return ProviderModels.Normalize(models);
    }
    static void CollectOptions(JsonArray? options, List<string> models)
    {
        if (options == null) return;
        foreach (var option in options)
        {
            if (option?["value"] is { } value) models.Add(value.ToString());
            CollectOptions(option?["options"] as JsonArray, models);
        }
    }

    public async Task<Completion> PromptAsync(Provider provider, JsonArray messages, Action<GenerationUpdate> update,
        CancellationToken ct, string? effort = null, AcpRunOptions? options = null)
    {
        options ??= new(Path.Combine(PortableStorage.Folder("AcpWorkspaces"), "read-only"));
        if (provider.Kind == "antigravity-acp" && (options.Mode != "execute" || !options.ToolsEnabled))
            throw new InvalidOperationException("L’adaptateur Antigravity ACP actuel ne garantit pas l’isolation des outils en mode Chat, Plan ou outils désactivés. Utilisez Agent / Exécution ; les permissions restent gérées par Antigravity.");
        Directory.CreateDirectory(options.Directory);
        await using var client = new Client(AcpProviders.StartInfo(provider, options.Directory, effort));
        var text = new StringBuilder(); var reasoning = new StringBuilder(); var timer = Stopwatch.StartNew();
        string? sessionId = null;
        client.Permission = async (request, token) =>
        {
            if (request["sessionId"]?.ToString() != sessionId) return false;
            var call = request["toolCall"];
            var kind = call?["kind"]?.ToString() ?? "other";
            if (!options.ToolsEnabled || options.Mode == "chat" || AgentPolicy.ReadOnly(options.Mode) && kind is not ("read" or "search" or "fetch" or "think")) return false;
            var paths = (call?["locations"] as JsonArray ?? []).Select(x => x?["path"]?.ToString() ?? "").Where(x => x.Length > 0).ToList();
            return options.Authorize != null && await options.Authorize(new(call?["title"]?.ToString() ?? "Outil ACP", kind,
                call?["rawInput"]?.ToJsonString() ?? "", paths), token);
        };
        client.Notification = (method, parameters) =>
        {
            if (method != "session/update" || parameters["sessionId"]?.ToString() != sessionId) return;
            var value = parameters["update"]; var type = value?["sessionUpdate"]?.ToString();
            if (type == "agent_message_chunk" && value?["content"]?["type"]?.ToString() == "text") text.Append(value["content"]!["text"]?.ToString());
            else if (type == "agent_thought_chunk" && value?["content"]?["type"]?.ToString() == "text") reasoning.Append(value["content"]!["text"]?.ToString());
            else if (type is "tool_call" or "tool_call_update")
                reasoning.AppendLine().Append(value?["title"]?.ToString() ?? value?["toolCallId"]?.ToString()).Append(" · ").Append(value?["status"]?.ToString());
            else if (type == "plan" && value?["entries"] is JsonArray entries)
                foreach (var entry in entries) reasoning.AppendLine().Append(entry?["content"]?.ToString()).Append(" · ").Append(entry?["status"]?.ToString());
            else return;
            update(new(text.ToString(), reasoning.ToString(), null, null, timer.Elapsed.TotalSeconds) { HasModelOutput = true });
        };
        await client.Initialize(ct);
        await client.AuthenticateChatGpt(provider, ct);
        var session = await client.NewSession(options.Directory, ct);
        sessionId = session["sessionId"]?.ToString() ?? throw new IOException("ACP : identifiant de session absent.");
        await client.Configure(session, sessionId, provider.Kind == "antigravity-acp" ? AcpProviders.DefaultModel : provider.Model, effort, options, ct);
        var prompt = Content(messages, client.SupportsImages);
        try
        {
            var result = await client.Request("session/prompt", new() { ["sessionId"] = sessionId, ["prompt"] = prompt }, ct);
            if (result["stopReason"]?.ToString() == "cancelled") throw new OperationCanceledException("ACP : génération arrêtée.", ct);
            if (result["stopReason"]?.ToString() == "refusal" && text.Length == 0) text.Append("L’agent a refusé cette demande.");
            if (text.Length == 0 && reasoning.Length == 0) throw new IOException("ACP : la session s’est terminée sans réponse.");
            return new(new JsonObject { ["role"] = "assistant", ["content"] = text.ToString(), ["reasoning_content"] = reasoning.ToString() }, null, null, timer.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            await client.Cancel(sessionId);
            await Task.Delay(150, CancellationToken.None);
            throw;
        }
    }

    public static JsonArray Content(JsonArray messages, bool images)
    {
        var prompt = new JsonArray();
        foreach (var message in messages)
        {
            var role = message?["role"]?.ToString() ?? "user";
            prompt.Add(new JsonObject { ["type"] = "text", ["text"] = "[" + role + "]\n" + (message?["content"] is JsonArray ? "" : message?["content"]?.ToString()) });
            if (message?["content"] is not JsonArray parts) continue;
            foreach (var part in parts)
                if (part?["type"]?.ToString() == "text") prompt.Add(new JsonObject { ["type"] = "text", ["text"] = part["text"]?.ToString() });
                else if (part?["type"]?.ToString() == "image_url")
                {
                    if (!images) throw new InvalidOperationException("Cet agent ACP n’annonce pas la prise en charge des images. Activez le relais vision ou utilisez un autre modèle.");
                    var uri = part["image_url"]?["url"]?.ToString() ?? ""; var comma = uri.IndexOf(','); var end = uri.IndexOf(';');
                    if (!uri.StartsWith("data:") || comma < 0 || end < 0) throw new ArgumentException("ACP : image intégrée requise.");
                    prompt.Add(new JsonObject { ["type"] = "image", ["mimeType"] = uri[5..end], ["data"] = uri[(comma + 1)..] });
                }
        }
        prompt.Add(new JsonObject { ["type"] = "text", ["text"] = "Continue this conversation by responding to the latest user request. Earlier messages are context, not new actions to repeat. Use your native tools only when the current permissions and mode allow them." });
        return prompt;
    }

    sealed class Client : IAsyncDisposable
    {
        readonly Process process;
        readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject>> pending = new();
        readonly List<string> sessions = [];
        readonly SemaphoreSlim writer = new(1, 1);
        readonly CancellationTokenSource lifetime = new();
        readonly Task reader, errors;
        long nextId;
        Exception? failure;
        public bool SupportsImages { get; private set; }
        JsonObject? initialization;
        public Action<string, JsonObject>? Notification { get; set; }
        public Func<JsonObject, CancellationToken, Task<bool>>? Permission { get; set; }
        public Client(ProcessStartInfo start)
        {
            try { process = Process.Start(start) ?? throw new IOException("ACP : impossible de lancer l’agent."); }
            catch (System.ComponentModel.Win32Exception ex) { throw new IOException("ACP : exécutable introuvable. Installez Node.js ou configurez le chemin de l’agent.", ex); }
            reader = Read(); errors = DrainErrors();
        }
        async Task DrainErrors()
        {
            // Drain diagnostic output without exposing credentials or blocking the child process.
            try { while (await process.StandardError.ReadLineAsync(lifetime.Token) != null) { } } catch (OperationCanceledException) { }
        }
        public async Task Initialize(CancellationToken ct)
        {
            initialization = await Request("initialize", new() { ["protocolVersion"] = 1, ["clientCapabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject { ["name"] = "monolith-harness", ["version"] = typeof(AcpEngine).Assembly.GetName().Version?.ToString() ?? "1" } }, ct);
            if (initialization["protocolVersion"]?.GetValue<int>() != 1) throw new IOException("Version ACP incompatible (version 1 requise).");
            SupportsImages = initialization["agentCapabilities"]?["promptCapabilities"]?["image"]?.GetValue<bool>() == true;
        }
        public void CheckChatGptAuthentication()
        {
            if (initialization?["authMethods"] is not JsonArray methods || !methods.Any(x => x?["id"]?.ToString() is "chat-gpt" or "chatgpt"))
                throw new IOException("Cet adaptateur n’annonce pas la connexion ChatGPT. Utilisez l’adaptateur Codex ACP compatible.");
        }
        public async Task AuthenticateChatGpt(Provider provider, CancellationToken ct)
        {
            if (provider.Kind != "chatgpt-acp") return;
            CheckChatGptAuthentication();
            var method = ((JsonArray)initialization!["authMethods"]!).First(x => x?["id"]?.ToString() is "chat-gpt" or "chatgpt")!["id"]!.ToString();
            await Request("authenticate", new() { ["methodId"] = method }, ct);
        }
        public async Task<JsonObject> NewSession(string directory, CancellationToken ct)
        {
            var session = await Request("session/new", new() { ["cwd"] = Path.GetFullPath(directory), ["mcpServers"] = new JsonArray() }, ct);
            if (session["sessionId"]?.ToString() is { Length: > 0 } id) sessions.Add(id);
            return session;
        }
        public async Task Configure(JsonObject session, string id, string model, string? effort, AcpRunOptions options, CancellationToken ct)
        {
            if (session["modes"]?["availableModes"] is JsonArray modes)
            {
                var wanted = options.ToolsEnabled && options.Mode == "execute" ? (modes.Any(x => x?["id"]?.ToString() == "workspace-write") ? "workspace-write" : "agent") : "read-only";
                if (!modes.Any(x => x?["id"]?.ToString() == wanted)) throw new IOException("ACP : mode requis indisponible : " + wanted);
                await Request("session/set_mode", new() { ["sessionId"] = id, ["modeId"] = wanted }, ct);
            }
            else if (options.Mode != "execute") throw new InvalidOperationException("Cet agent ACP ne permet pas d’imposer un mode en lecture seule.");
            if (session["configOptions"] is JsonArray config)
            {
                foreach (var (category, requested) in new[] { ("model", model), ("thought_level", effort ?? "auto") })
                {
                    if (requested is "default" or "auto" or "") continue;
                    var option = config.FirstOrDefault(x => x?["category"]?.ToString() == category);
                    if (option == null) { if (category == "model") throw new IOException("Cet agent ACP ne permet pas de sélectionner ce modèle."); continue; }
                    var values = new List<string>(); CollectOptions(option["options"] as JsonArray, values);
                    if (!values.Contains(requested)) { if (category == "model") throw new IOException("Modèle ACP indisponible : " + requested); continue; }
                    await Request("session/set_config_option", new() { ["sessionId"] = id, ["configId"] = option["id"]?.ToString(), ["value"] = requested }, ct);
                }
            }
            else if (model != AcpProviders.DefaultModel && session["models"]?["availableModels"] is JsonArray)
                await Request("session/set_model", new() { ["sessionId"] = id, ["modelId"] = model }, ct);
        }
        public async Task<JsonObject> Request(string method, JsonObject parameters, CancellationToken ct)
        {
            var id = Interlocked.Increment(ref nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var source = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = source;
            if (Volatile.Read(ref failure) is { } disconnected) source.TrySetException(disconnected);
            try
            {
                await Write(new() { ["jsonrpc"] = "2.0", ["id"] = long.Parse(id), ["method"] = method, ["params"] = parameters }, ct);
                return await source.Task.WaitAsync(ct);
            }
            finally { pending.TryRemove(id, out _); }
        }
        public async Task Cancel(string id)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try { await Write(new() { ["jsonrpc"] = "2.0", ["method"] = "session/cancel", ["params"] = new JsonObject { ["sessionId"] = id } }, timeout.Token); } catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException) { }
        }
        async Task Write(JsonObject message, CancellationToken ct)
        {
            await writer.WaitAsync(ct);
            try { await process.StandardInput.WriteLineAsync(message.ToJsonString().AsMemory(), ct); await process.StandardInput.FlushAsync(ct); }
            finally { writer.Release(); }
        }
        async Task Read()
        {
            Exception ended = new IOException("La connexion ACP s’est interrompue. Vérifiez l’installation et la connexion du compte dans l’agent.");
            try
            {
                while (await process.StandardOutput.ReadLineAsync(lifetime.Token) is { } line)
                {
                    JsonObject? message;
                    try { message = JsonNode.Parse(line) as JsonObject; } catch (System.Text.Json.JsonException) { continue; }
                    if (message == null) continue;
                    var id = message["id"]?.ToString(); var method = message["method"]?.ToString();
                    if (method != null)
                    {
                        var parameters = message["params"] as JsonObject ?? new();
                        if (id == null) { Notification?.Invoke(method, parameters); continue; }
                        var answer = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = message["id"]!.DeepClone() };
                        if (method == "session/request_permission")
                        {
                            bool allowed = Permission != null && await Permission(parameters, lifetime.Token);
                            var choices = parameters["options"] as JsonArray ?? [];
                            var selected = choices.FirstOrDefault(x => x?["kind"]?.ToString() == (allowed ? "allow_once" : "reject_once"));
                            answer["result"] = new JsonObject { ["outcome"] = selected == null ? new JsonObject { ["outcome"] = "cancelled" }
                                : new JsonObject { ["outcome"] = "selected", ["optionId"] = selected["optionId"]?.ToString() } };
                        }
                        else answer["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Client method not supported" };
                        await Write(answer, lifetime.Token); continue;
                    }
                    if (id == null || !pending.TryGetValue(id, out var source)) continue;
                    if (message["error"] is JsonObject error)
                    {
                        var code = error["code"]?.ToString();
                        source.TrySetException(new IOException(code == "-32000" ? "ACP : authentification requise. Connectez votre compte dans les fournisseurs ou dans agy."
                            : "ACP : demande refusée par l’agent (code " + code + "). Vérifiez le modèle et les permissions de l’agent."));
                    }
                    else source.TrySetResult(message["result"] as JsonObject ?? new());
                }
            }
            catch (Exception ex) { ended = ex; }
            finally { Volatile.Write(ref failure, ended); foreach (var source in pending.Values) source.TrySetException(ended); }
        }
        public async ValueTask DisposeAsync()
        {
            // Closing agent sessions first lets adapters release their own child processes.
            foreach (var id in sessions)
            {
                using var closing = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
                try { await Request("session/close", new() { ["sessionId"] = id }, closing.Token); }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException) { }
            }
            try
            {
                process.StandardInput.Close();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(500));
            }
            catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or IOException)
            {
                try { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); } }
                catch (Exception stopped) when (stopped is TimeoutException or InvalidOperationException) { }
            }
            lifetime.Cancel();
            try { await Task.WhenAll(reader, errors).WaitAsync(TimeSpan.FromSeconds(2)); } catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException) { }
            process.Dispose(); lifetime.Dispose(); writer.Dispose();
        }
    }
}
