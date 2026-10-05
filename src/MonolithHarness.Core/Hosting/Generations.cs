using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core.Hosting;

public sealed partial class HarnessService
{
    async Task<object> Send(JsonObject p, CancellationToken lifetime)
    {
        await using var setup = Db();
        var chat = await setup.Chats.SingleAsync(x => x.Id == I(p, "chatId"), lifetime);
        var project = await setup.Projects.SingleAsync(x => x.Id == chat.ProjectId, lifetime);
        var provider = await setup.Providers.SingleAsync(x => x.Id == I(p, "providerId"), lifetime);
        var options = await setup.States.SingleAsync(lifetime);
        hostOptions?.Apply(options);
        var images = (p["images"] as JsonArray ?? []).Select(x => new Attachment
        { Name = x!["name"]!.GetValue<string>(), Mime = x["mime"]!.GetValue<string>(), Data = Convert.FromBase64String(x["data"]!.GetValue<string>()) }).ToList();
        if (images.Count > 4 || images.Any(x => x.Data.Length > 8 * 1024 * 1024 || x.Mime is not ("image/png" or "image/jpeg" or "image/webp"))) throw new ArgumentException("4 PNG/JPEG/WebP images maximum, 8 MB each.");
        var text = S(p, "text").Trim();
        if (text.Length == 0 && images.Count == 0) throw new ArgumentException("Message required.");
        using var run = new ConversationSession(chat, project, provider, options, text, images, database,await setup.Providers.ToListAsync(lifetime)){PendingInputId=I(p,"pendingInputId")};
        permissionProject.Value = run.Project;
        project = run.Project;
        provider=run.Provider;
        options = run.Options;
        using var consumption = TokenConsumption.Begin(database, "chat", project, chat);
        await using var mcp = CreateMcpSession(chat.Id);
        if (!runs.TryAdd(chat.Id, run))
        {
            if (run.PendingInputId != 0) return false; // Another queue consumer already started the turn.
            throw new InvalidOperationException("This conversation is already running.");
        }
        using var cancel = lifetime.Register(run.Cancellation.Cancel);
        var ct = run.Cancellation.Token;
        run.Chat.UpdatedUtc = DateTime.UtcNow;
        Message? active = null;
        string error = "";
        string completionStatus = "";
        Task namingTask = Task.CompletedTask;
        async Task NameAutomaticallyAsync()
        {
            try
            {
                if (await ConversationNaming.RenameAsync(database, chat.Id, http, Decrypt, true, lifetime) is { } name)
                    await emit(new { @event = "chat.renamed", chatId = chat.Id, title = name });
            }
            catch (Exception ex) { AppLog.Write(AppLogLevel.Warning, "conversation.naming_failed", ex, chat.Id); }
        }
        try
        {
            AppLog.Write(AppLogLevel.Information, "generation.started", chatId: chat.Id);
            await emit(new{@event="started",chatId=chat.Id});
            await run.PrepareSandboxAsync(ct);
            var secret = await Decrypt(provider.ProtectedKey, ct);
            var history = await History(run, ct);
            var user = new Message { ChatId = chat.Id, Content = text, Attachments = run.Images };
            await ConversationInbox.SubmitAsync(run,user,ct); history.Add(user);
            if (ConversationNaming.At(FeatureSettings.Read(options.FeaturesJson), "first-message")) namingTask = NameAutomaticallyAsync();
            await NotifyInbox(chat.Id);
            await emit(new { @event = "message", chatId = chat.Id, title = run.Chat.Title, message = MessageView(user) });
            var definitions = Definitions(run);
            var chatOnly = ConversationModes.IsChat(run.Chat.InteractionMode);
            var system = chatOnly ? ConversationModes.ChatPrompt(options.Language, run.Chat, provider.IsOpenCode, provider.OpenCodeTools, options.ThinkingLevel) : Skills.Prompt(options.EnabledSkills, options.Language, project.GetSourceFolders().Count > 0,
                BrowserSkillAccess.Enabled(options.EnabledSkills) && FeatureSettings.Read(options.FeaturesJson).BrowserMode == "embedded",
                Skills.Enabled(options.EnabledSkills, "write_sources"));
            if (provider.IsAcp) definitions.Clear();
            run.Workflow = chatOnly ? null : CreateWorkflow(run);
            var agent = CreateAgentRuntime(run, secret, mcp);
            if (provider.IsAcp) system = AcpProviders.SystemPrompt(options.Language, run.Chat.ExecutionMode);
            else system += await agent.InitializeAsync(ct);
            if (!chatOnly) system += FeatureSettings.Read(options.FeaturesJson).GoalInstructions(run.Chat.Id);
            if (!provider.IsAcp && run.Chat.OrchestrationMode != "disabled")
            {
                var report = await agent.StartAsync(ct);
                if (report.Length > 0)
                {
                    if (provider.IsOpenCode) system += "\nSubagent findings:\n" + report;
                    var delegated = AgentHandoff.Create(chat.Id, report);
                    run.Db.Messages.Add(delegated); await run.Db.SaveChangesAsync(ct); history.Add(delegated);
                    await emit(new { @event = "message", chatId = chat.Id, message = MessageView(delegated) });
                }
            }
            var engine = new ChatEngine(http);
            for (int round = 0; ; round++)
            {
                ct.ThrowIfCancellationRequested();
                if((await ApplySteering(run,ct)).Count>0){history=await History(run,ct);round=0;}
                if (round > 0 && round % 12 == 0)
                {
                    await using var current = Db();
                    if (!await current.States.Select(x => x.AutoContinue).SingleAsync(ct))
                    { completionStatus = "12 étapes atteintes / 12 steps reached. Send continue to proceed."; await emit(new { @event = "status", chatId = chat.Id, text = completionStatus }); break; }
                    await emit(new { @event = "status", chatId = chat.Id, text = "Continuation automatique / Auto-continue…" });
                }
                if (!provider.IsExternalAgent)
                {
                    definitions = Definitions(run);
                    agent.AddDefinitions(definitions); AgentPolicy.Filter(definitions, run.Chat);
                    SandboxWorkspace.Filter(definitions, run.Chat.SandboxEnabled);
                    if (!run.Chat.SandboxEnabled && !AgentPolicy.ReadOnly(run.Chat.ExecutionMode))
                        foreach (var definition in await mcp.RefreshAsync(ct)) definitions.Add(definition!.DeepClone());
                }
                if (!provider.IsExternalAgent) history = await Compact(run, history, system, definitions, secret, ct);
                var wire = Wire(system, ConversationModes.History(history, run.Chat));
                wire = await VisionFor(run).PrepareAsync(wire, ct);
                int input = ContextWindow.Estimate(wire) + ContextWindow.Estimate(definitions);
                active = new Message { ChatId = chat.Id, Role = "assistant", State = "interrupted" };
                run.Db.Messages.Add(active); await run.Db.SaveChangesAsync(ct);
                var lastUpdate = DateTime.MinValue;
                bool sawModelOutput = false;
                var speedTracker = new GenerationSpeedTracker();
                void Update(GenerationUpdate update)
                {
                    if (update.Retry is { } retry) { sawModelOutput = false; emit(new { @event = "status", chatId = chat.Id, text = retry.Describe(options.Language) }).GetAwaiter().GetResult(); return; }
                    if (update.CompatibilityNotice.Length > 0) active.CompatibilityNotice = update.CompatibilityNotice;
                    run.ExportProgress = new(active.Id, update);
                    speedTracker.AddSample(update.Seconds, update.OutputTokens ?? ContextWindow.EstimateText(update.Text + update.Reasoning));
                    active.Content = update.Text; active.InputTokens = update.InputTokens; active.OutputTokens = update.OutputTokens; active.CachedInputTokens = update.CachedInputTokens; active.Seconds = update.Seconds;
                    bool modelOutput = update.HasModelOutput || update.Text.Length > 0 || update.Reasoning.Length > 0 || update.OutputTokens > 0;
                    if ((sawModelOutput || !modelOutput) && (DateTime.UtcNow - lastUpdate).TotalMilliseconds < 80) return;
                    sawModelOutput |= modelOutput;
                    lastUpdate = DateTime.UtcNow;
                    // The stdout writer serializes events; blocking here preserves ordering without unobserved tasks.
                    emit(new { @event = "stream", chatId = chat.Id, messageId = active.Id, text = update.Text, reasoning = update.Reasoning,
                        html = Html(update.Text), speed = update.TokensPerSecond, compatibilityNotice = active.CompatibilityNotice, cachedInputTokens = update.CachedInputTokens,
                        speedMin = speedTracker.MinSpeed, speedMax = speedTracker.MaxSpeed, speedAverage = speedTracker.AverageSpeed,
                        speedEstimated = !update.OutputTokens.HasValue,
                        tokens = (update.InputTokens ?? input) + (update.OutputTokens ?? ContextWindow.EstimateText(update.Text + update.Reasoning)),
                        limit = provider.ContextLimit, estimated = !update.InputTokens.HasValue || !update.OutputTokens.HasValue,
                        modelOutput }).GetAwaiter().GetResult();
                }
                var completion = provider.IsOpenCode
                    ? await OpenCode(run, secret, history, system, Update, ct)
                    : await engine.StreamAsync(provider, secret, wire, definitions, Update, ct, options.ThinkingLevel, FeatureSettings.Read(options.FeaturesJson), progress =>
                        emit(new { @event = "status", chatId = chat.Id, text = progress.Caption(options.Language, 0), contextPreload = progress }).GetAwaiter().GetResult(), requireNoReasoning: ConversationModes.IsChat(run.Chat.InteractionMode) && options.ThinkingLevel.Equals("none", StringComparison.OrdinalIgnoreCase), acp: provider.IsAcp ? AcpOptions(run) : null);
                active.Content = completion.Message["content"]?.GetValue<string>() ?? "";
                lastUpdate = DateTime.MinValue;
                Update(new GenerationUpdate(active.Content, completion.Message["reasoning_content"]?.GetValue<string>() ?? "", completion.InputTokens, completion.OutputTokens, completion.Seconds) { CachedInputTokens = completion.CachedInputTokens });
                active.WireJson = completion.Message.ToJsonString(); active.InputTokens = completion.InputTokens; active.OutputTokens = completion.OutputTokens; active.CachedInputTokens = completion.CachedInputTokens; active.Seconds = completion.Seconds; active.CompletedUtc = DateTime.UtcNow;
                if (provider.IsExternalAgent) active.State = "complete";
                await emit(new { @event = "message", chatId = chat.Id, message = MessageView(active) });
                var results = new List<Message>();
                if (completion.Message["tool_calls"] is JsonArray calls)
                    foreach (var call in calls)
                    {
                        var name = call!["function"]!["name"]!.GetValue<string>();
                        var arguments = call["function"]?["arguments"]?.GetValue<string>() ?? "{}";
                        ToolResult result;
                        if (!TerminalHub.IsBoundedWait(name, arguments)) await run.LoopGuard.CheckAsync(name, arguments, run.Workflow, ct);
                        var ownsToolQueue = !AgentRuntime.Handles(name) && !TerminalHub.Handles(name) && !RagTools.Handles(name) && !VisionBridge.Handles(name) && !PythonTools.Handles(name);
                        if (ownsToolQueue) await tools.WaitAsync(ct);
                        var toolTimer = Stopwatch.StartNew();
                        try
                        {
                            await emit(new { @event = "status", chatId = chat.Id, text = "Outil / Tool: " + name });
                            try
                            {
                                AgentPolicy.Demand(run.Chat, name);
                                SandboxWorkspace.Demand(run.Chat.SandboxEnabled, name);
                                await ProjectResources.DemandToolAsync(run.Project, name, arguments, (scope, details, token) => Approve(scope, "Projet · " + name, details, token), ct);
                                if (AgentRuntime.Handles(name)) result = new(await agent.CallAsync(name, JsonNode.Parse(arguments) as JsonObject ?? [], ct));
                                else if (name.StartsWith("mcp_", StringComparison.Ordinal))
                                {
                                    var output = await mcp.CallAsync(name, JsonNode.Parse(arguments) as JsonObject ?? [], provider.SupportsImages || VisionBridge.Enabled(options.EnabledSkills), ct);
                                    result = new(output.Text, output.Image);
                                }
                                else result = await Tool(run, name, JsonNode.Parse(arguments) as JsonObject ?? [], ct);
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex) { AppLog.Write(AppLogLevel.Warning, "tool.failed", ex, chat.Id); result = new("Erreur outil / Tool error: " + ex.Message); }
                        }
                        finally { toolTimer.Stop(); if (ownsToolQueue) tools.Release(); }
                        var toolWire = new JsonObject { ["role"] = "tool", ["tool_call_id"] = call["id"]!.GetValue<string>(), ["content"] = result.Text };
                        var message = new Message { ChatId = chat.Id, Role = "tool", State = "interrupted", Content = name + "\n" + result.Text, WireJson = toolWire.ToJsonString(), Seconds = toolTimer.Elapsed.TotalSeconds, CompletedUtc = DateTime.UtcNow };
                        if (result.Image != null) message.Attachments.Add(result.Image);
                        results.Add(message);
                        run.Db.Messages.Add(message); await run.Db.SaveChangesAsync(ct);
                        await emit(new { @event = "message", chatId = chat.Id, arguments, message = MessageView(message) });
                    }
                active.State = "complete";
                foreach (var result in results) result.State = "complete";
                await run.Db.SaveChangesAsync(ct);
                foreach (var result in results) await emit(new { @event = "message", chatId = chat.Id, message = MessageView(result) });
                await emit(new { @event = "message", chatId = chat.Id, message = MessageView(active) });
                history = await History(run, ct);
                var steered=await ApplySteering(run,ct);
                if(steered.Count>0){history=await History(run,ct);round=0;}
                if (!provider.IsExternalAgent) history = await Compact(run, history, system, definitions, secret, ct);
                else if (provider.IsOpenCode && FeatureSettings.Read(options.FeaturesJson).Compaction.ShouldCompact((completion.InputTokens ?? input) + (completion.OutputTokens ?? ContextWindow.EstimateText(active.Content)), provider.ContextLimit))
                {
                    await Compact(run, history, system, definitions, secret, ct, true);
                }
                active = null;
                if (results.Count == 0 && steered.Count==0) break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex is OperationCanceledException ? AppLogLevel.Information : AppLogLevel.Error, "generation.failed", ex, chat.Id);
            error = ex is OperationCanceledException ? "Génération arrêtée / Generation stopped" : ex.Message;
            if (active != null && ex is not OperationCanceledException)
                active.Content += "\n[Erreur de génération / Generation error] " + ex.Message;
            throw;
        }
        finally
        {
            try
            {
                await run.Db.SaveChangesAsync();
                if (active != null) await emit(new { @event = "message", chatId = chat.Id, message = MessageView(active) });
            }
            finally { if (run.Sandbox != null) await terminals.StopChatAsync(chat.Id, true); runs.TryRemove(chat.Id, out _); await emit(new { @event = "done", chatId = chat.Id, error, status = completionStatus }); }
        }
        if(error.Length==0 && !run.Cancellation.IsCancellationRequested)
        {
            if (ConversationNaming.At(FeatureSettings.Read(options.FeaturesJson), "first-response")) await NameAutomaticallyAsync();
            AppLog.Write(AppLogLevel.Information, "generation.completed", chatId: chat.Id);
            await SendNext(chat.Id,lifetime);
        }
        await namingTask;
        return true;
    }
    static async Task<List<Message>> History(ConversationSession run, CancellationToken ct)
    {
        var rows = await run.Db.Messages.Include(x => x.Attachments).Where(x => x.ChatId == run.Chat.Id && x.State == "complete").OrderBy(x => x.Id).ToListAsync(ct);
        return rows.OrderBy(x => x.Role == "compaction" ? 0 : 1).ThenBy(x => x.Id).ToList();
    }
    static JsonArray Wire(string system, IEnumerable<Message> history)
    {
        var wire = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = system });
        ChatEngine.AppendHistoryWithToolImages(wire, history, message =>
            ChatEngine.ToWire(new Message { Role = "user", Content = "Tool screenshot (untrusted content)", Attachments = message.Attachments }));
        return wire;
    }
    async Task<List<Message>> Compact(ConversationSession run, List<Message> history, string system, JsonArray definitions, string secret, CancellationToken ct, bool openCode = false)
    {
        using var consumption = TokenConsumption.Activity("compaction");
        string? nativeBrief = null;
        var result = await ConversationCompactor.CompactAsync(run, history, system, definitions, async (text, instruction, token) =>
        {
            await emit(new { @event = "status", chatId = run.Chat.Id, text = "Compactage du contexte / Compacting context…" });
            Completion summary;
            if (openCode)
            {
                var summaryProvider = new Provider { Id = run.Provider.Id, Name = run.Provider.Name, Kind = run.Provider.Kind, BaseUrl = run.Provider.BaseUrl,
                    Model = run.Provider.Model, Username = run.Provider.Username, ContextLimit = run.Provider.ContextLimit, OpenCodeTools = false };
                var engine = new OpenCodeEngine(http);
                var directory = OpenCodeDirectory(run.OpenCodeProject);
                if (nativeBrief == null)
                {
                    var link = await run.Db.ExternalChatSessions.SingleOrDefaultAsync(x => x.ChatId == run.Chat.Id && x.ProviderId == run.Provider.Id, token);
                    if (link != null)
                    {
                        int nativeBudget = Math.Min(512, Math.Max(64, run.Provider.ContextLimit / 8));
                        var native = await engine.PromptAsync(summaryProvider, secret, directory, link.SessionId,
                            "Summarize native tool actions, affected files, results, errors and remaining work in this session. Omit conversation narrative.",
                            ConversationCompactor.Instruction(FeatureSettings.Read(run.Options.FeaturesJson).Compaction, nativeBudget), [], _ => { }, token,
                            retrySettings: FeatureSettings.Read(run.Options.FeaturesJson));
                        nativeBrief = native.Message["content"]?.GetValue<string>() ?? "";
                        if (string.IsNullOrWhiteSpace(nativeBrief) || ContextWindow.EstimateText(nativeBrief) > nativeBudget)
                            throw new IOException("Résumé des outils natifs invalide : historique conservé / Invalid native tool summary: original preserved.");
                        text += "\n\nNative tool work (additional context):\n" + nativeBrief;
                    }
                    else nativeBrief = "";
                }
                var session = await engine.CreateSessionAsync(summaryProvider, secret, directory, "Compactage", token);
                summary = await engine.PromptAsync(summaryProvider, secret, directory, session, text, instruction, [], _ => { }, token,
                    retrySettings: FeatureSettings.Read(run.Options.FeaturesJson));
            }
            else summary = await new ChatEngine(http).StreamAsync(run.Provider, secret, new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = instruction }, new JsonObject { ["role"] = "user", ["content"] = text }),
                [], _ => { }, token, retrySettings: FeatureSettings.Read(run.Options.FeaturesJson));
            return summary.Message["content"]?.GetValue<string>() ?? "";
        }, ct);
        if (result.Changed) await emit(new { @event = "status", chatId = run.Chat.Id, text = $"Contexte compacté / Context compacted · {result.BeforeTokens:N0} → {result.AfterTokens:N0} tokens" });
        else if (result.Reason == "protected") await emit(new { @event = "status", chatId = run.Chat.Id, text = "Cible de compactage impossible : demande ou instructions trop grandes / Compaction target cannot be met: request or instructions too large" });
        return result.History;
    }
    string OpenCodeDirectory(Project p)
    {
        var directory = p.GetSourceFolders().FirstOrDefault(Directory.Exists) ?? Path.Combine(PortableStorage.Folder("OpenCodeWorkspaces", Path.GetDirectoryName(database)!), p.Id.ToString());
        Directory.CreateDirectory(directory); return directory;
    }
    async Task EnsureOpenCode(Provider p, string password, string directory, CancellationToken ct)
    {
        await startup.WaitAsync(ct);
        try
        {
            var engine = new OpenCodeEngine(http);
            try { await engine.HealthAsync(p, password, ct); return; } catch (HttpRequestException) when (p.AutoStart) { }
            if (!p.AutoStart) throw new IOException("Start opencode serve, or enable automatic startup.");
            var uri = new Uri(p.BaseUrl); if (!uri.IsLoopback) throw new InvalidOperationException("OpenCode auto-start requires a loopback URL.");
            var start = new ProcessStartInfo(string.IsNullOrWhiteSpace(p.ExecutablePath) ? "opencode" : p.ExecutablePath)
            { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "serve", "--hostname", uri.Host, "--port", uri.Port.ToString() }) start.ArgumentList.Add(argument);
            start.Environment["OPENCODE_SERVER_USERNAME"] = string.IsNullOrWhiteSpace(p.Username) ? "opencode" : p.Username;
            start.Environment["OPENCODE_SERVER_PASSWORD"] = password;
            var process = Process.Start(start) ?? throw new IOException("Could not start opencode."); servers.Add(process);
            process.OutputDataReceived += (_, _) => { }; process.ErrorDataReceived += (_, _) => { }; process.BeginOutputReadLine(); process.BeginErrorReadLine();
            for (int i = 0; i < 40; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (process.HasExited) throw new IOException("OpenCode exited during startup. Configure the opencode CLI executable.");
                try { await engine.HealthAsync(p, password, ct); return; } catch (HttpRequestException) { await Task.Delay(250, ct); }
            }
            throw new IOException("OpenCode startup timed out.");
        }
        finally { startup.Release(); }
    }
    async Task<Completion> OpenCode(ConversationSession run, string password, List<Message> history, string system, Action<GenerationUpdate> update, CancellationToken ct)
    {
        var p = run.Provider; var directory = OpenCodeDirectory(run.OpenCodeProject);
        await EnsureOpenCode(p, password, directory, ct);
        var engine = new OpenCodeEngine(http);
        var link = await run.Db.ExternalChatSessions.SingleOrDefaultAsync(x => x.ChatId == run.Chat.Id && x.ProviderId == p.Id, ct);
        if (link == null)
        {
            link = new ExternalChatSession { ChatId = run.Chat.Id, ProviderId = p.Id, SessionId = await engine.CreateSessionAsync(p, password, directory, run.Chat.Title, ct) };
            run.Db.ExternalChatSessions.Add(link); await run.Db.SaveChangesAsync(ct);
            system += "\nPrevious history:\n" + string.Join("\n", ConversationModes.History(history, run.Chat).TakeLast(20).Select(x => $"[{x.Role}] {x.Content}"));
        }
        var pendingUsers=history.AsEnumerable().Reverse().TakeWhile(x=>x.Role=="user").Reverse().ToList();
        var prompt = pendingUsers.Count>0?string.Join("\n\n",pendingUsers.Select(x=>x.Content)):run.Prompt;
        var attachments = (pendingUsers.Count>0?pendingUsers.SelectMany(x=>x.Attachments):run.Images).ToList();
        if (!p.SupportsImages && attachments.Count > 0)
        {
            var described = await VisionFor(run).PrepareAsync(new JsonArray(ChatEngine.ToWire(new Message { Content = prompt, Attachments = attachments })), ct);
            prompt = described[0]!["content"]!.GetValue<string>(); attachments.Clear();
        }
        return await engine.PromptAsync(p, password, directory, link.SessionId, prompt, system,
            attachments.Select(x => new OpenCodeAttachment(x.Name, x.Mime, x.Data)).ToList(), update, ct,
            async (permission, token) => await Approve($"opencode|{p.Id}|{directory}|{permission.Action}|{string.Join('|', permission.Resources)}",
                run.Chat.Title + " · OpenCode · " + permission.Action, string.Join('\n', permission.Resources) + "\n" + permission.Details, token) ? "once" : "reject", new(run.Chat.ExecutionMode, run.Chat.OrchestrationMode, run.Chat.ChatWebEnabled, run.Options.ThinkingLevel), run.Workflow, FeatureSettings.Read(run.Options.FeaturesJson));
    }
}
