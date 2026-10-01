using Microsoft.EntityFrameworkCore;

using MonolithHarness.Core;

using System.Diagnostics;

using System.Text;

using System.Text.Json.Nodes;

using static MonolithHarness.App.UiText;



namespace MonolithHarness.App;



public sealed partial class MainWindow

{

    readonly List<Process> openCodeProcesses = [];



    public static string ResolveOpenCodeExecutable(string? configuredPath)

    {

        if (!string.IsNullOrWhiteSpace(configuredPath))

        {

            if (Path.IsPathFullyQualified(configuredPath) && File.Exists(configuredPath)) return configuredPath;

            if (!Path.IsPathFullyQualified(configuredPath)) return configuredPath;

        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var defaultDesktop = Path.Combine(localAppData, @"Programs\@opencode-aidesktop\OpenCode.exe");

        if (File.Exists(defaultDesktop)) return defaultDesktop;

        return string.IsNullOrWhiteSpace(configuredPath) ? "opencode" : configuredPath;

    }



    public static bool IsElectronOpenCode(string executable)

    {

        if (string.IsNullOrWhiteSpace(executable)) return false;

        if (executable.EndsWith("OpenCode.exe", StringComparison.OrdinalIgnoreCase)) return true;

        try

        {

            if (File.Exists(executable))

            {

                var dir = Path.GetDirectoryName(executable);

                if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "resources", "app.asar"))) return true;

            }

        }

        catch { }

        return false;

    }



    public static string EnsureOpenCodeRunnerScript()

    {

        var runnerPath = Path.Combine(HarnessDb.DataDirectory, "opencode-runner.mjs");

        Directory.CreateDirectory(HarnessDb.DataDirectory);

        const string script = """

import fs from 'fs';

import path from 'path';

import { pathToFileURL } from 'url';



const asarPath = path.join(path.dirname(process.execPath), 'resources', 'app.asar');

if (!fs.existsSync(asarPath)) {

  console.error('app.asar not found at ' + asarPath);

  process.exit(1);

}

const chunksDir = path.join(asarPath, 'out', 'main', 'chunks');

const files = fs.readdirSync(chunksDir);

let serverModule = null;

for (const f of files) {

  if (f.startsWith('node-') && f.endsWith('.js')) {

    try {

      const mod = await import(pathToFileURL(path.join(chunksDir, f)).href);

      if (mod.Server && typeof mod.Server.listen === 'function') {

        serverModule = mod.Server;

        break;

      }

    } catch {}

  }

}

if (!serverModule) {

  console.error('Could not find OpenCode Server in ' + chunksDir);

  process.exit(1);

}



const port = parseInt(process.env.OPENCODE_PORT || process.argv[2] || '4096', 10);

const hostname = process.env.OPENCODE_HOSTNAME || process.argv[3] || '127.0.0.1';

const username = process.env.OPENCODE_SERVER_USERNAME || 'opencode';

const password = process.env.OPENCODE_SERVER_PASSWORD || '';



const bypassFree = process.env.OPENCODE_BYPASS_FREE_LIMITATION === '1';



if (bypassFree && typeof globalThis.fetch === 'function') {

  const origFetch = globalThis.fetch;

  globalThis.fetch = async function(url, init = {}) {

    init = init || {};

    init.headers = init.headers || {};

    const setHeader = (k, v) => {

      if (init.headers instanceof Headers) {

        if (!init.headers.has(k)) init.headers.set(k, v);

      } else if (Array.isArray(init.headers)) {

        if (!init.headers.some(([hk]) => hk.toLowerCase() === k.toLowerCase())) init.headers.push([k, v]);

      } else {

        if (!init.headers[k] && !init.headers[k.toLowerCase()]) init.headers[k] = v;

      }

    };

    setHeader('x-opencode-client', 'desktop');

    return origFetch(url, init);

  };

}



const listener = await serverModule.listen({

  port,

  hostname,

  username,

  password,

  cors: bypassFree ? ['*'] : ['oc://renderer']

});

console.log('OpenCode server listening on', listener.url);



process.stdin.resume();

process.on('SIGINT', async () => { try { await listener.stop(); } catch {} process.exit(0); });

process.on('SIGTERM', async () => { try { await listener.stop(); } catch {} process.exit(0); });

""";

        if (!File.Exists(runnerPath) || File.ReadAllText(runnerPath) != script)

        {

            File.WriteAllText(runnerPath, script, Encoding.UTF8);

        }

        return runnerPath;

    }



    readonly SemaphoreSlim openCodeStartupQueue = new(1, 1);



    async Task EnsureOpenCodeServerAsync(Provider target, string password, CancellationToken ct, Project? targetProject = null)

    {

        await openCodeStartupQueue.WaitAsync(ct);

        try { await StartOpenCodeServerAsync(target, password, ct, targetProject); }

        finally { openCodeStartupQueue.Release(); }

    }



    async Task StartOpenCodeServerAsync(Provider target, string password, CancellationToken ct, Project? targetProject)

    {

        try { await openCodeEngine.HealthAsync(target, password, ct); return; }

        catch when (target.AutoStart) { }

        if (!target.AutoStart) throw new IOException(T("Serveur OpenCode indisponible. Lancez « opencode serve » ou activez son démarrage automatique."));

        var uri = new Uri(target.BaseUrl);

        if (!uri.IsLoopback) throw new InvalidOperationException(T("Le démarrage automatique OpenCode est limité à une adresse locale."));

        var executable = ResolveOpenCodeExecutable(target.ExecutablePath);

        if (Path.IsPathFullyQualified(executable) && !File.Exists(executable)) throw new FileNotFoundException(T("Exécutable OpenCode introuvable."), executable);

        var start = new ProcessStartInfo

        {

            FileName = executable,

            UseShellExecute = false,

            CreateNoWindow = true,

            WorkingDirectory = OpenCodeDirectory(targetProject)

        };

        if (!string.IsNullOrEmpty(password)) start.Environment["OPENCODE_SERVER_PASSWORD"] = password;

        var user = string.IsNullOrWhiteSpace(target.Username) ? "opencode" : target.Username.Trim();

        start.Environment["OPENCODE_SERVER_USERNAME"] = user;

        if (target.BypassFreeLimitation)

        {

            start.Environment["OPENCODE_CLIENT"] = "desktop";

            start.Environment["OPENCODE_BYPASS_FREE_LIMITATION"] = "1";

        }



        if (IsElectronOpenCode(executable))

        {

            var runner = EnsureOpenCodeRunnerScript();

            start.Environment["ELECTRON_RUN_AS_NODE"] = "1";

            start.Environment["OPENCODE_PORT"] = uri.Port.ToString();

            start.Environment["OPENCODE_HOSTNAME"] = uri.Host;

            start.ArgumentList.Add(runner);

        }

        else

        {

            start.ArgumentList.Add("serve");

            start.ArgumentList.Add("--hostname");

            start.ArgumentList.Add(uri.Host);

            start.ArgumentList.Add("--port");

            start.ArgumentList.Add(uri.Port.ToString());

        }



        Process process;

        try { process = Process.Start(start) ?? throw new IOException(T("Impossible de démarrer OpenCode.")); }

        catch (Exception ex) { throw new IOException(T("Impossible de démarrer OpenCode. Vérifiez le chemin de l’exécutable."), ex); }

        openCodeProcesses.Add(process);

        Exception? last = null;

        for (var attempt = 0; attempt < 40; attempt++)

        {

            ct.ThrowIfCancellationRequested();

            if (process.HasExited) throw new IOException(T("OpenCode s’est arrêté pendant son démarrage."));

            try { await openCodeEngine.HealthAsync(target, password, ct); return; }

            catch (Exception ex) { last = ex; await Task.Delay(250, ct); }

        }

        throw new IOException(T("Le serveur OpenCode n’a pas répondu dans le délai prévu."), last);

    }



    string OpenCodeDirectory(Project? targetProject = null)

    {

        var project = targetProject ?? this.project;

        var source = project?.GetSourceFolders().FirstOrDefault(Directory.Exists);

        if (!string.IsNullOrWhiteSpace(source)) return source;

        var fallback = Path.Combine(HarnessDb.DataDirectory, "OpenCodeWorkspaces", "project-" + (project?.Id ?? 0));

        Directory.CreateDirectory(fallback);

        return fallback;

    }



    void StopOpenCodeProcesses()

    {

        foreach (var process in openCodeProcesses)

        {

            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }

            process.Dispose();

        }

        openCodeProcesses.Clear();

    }



    async Task SendOpenCodeAsync(ConversationRun run, string password)

    {
        using var consumption = TokenConsumption.Begin(run.Db.FilePath, "chat", run.Project, run.Chat);

        var db = run.Db; var chat = run.Chat; var provider = run.Provider; var state = run.Options;

        var ct = run.Cancellation.Token;

        var prompt = run.Prompt;

        var images = run.Images.Select(x => new OpenCodeAttachment(x.Name, x.Mime, x.Data)).ToList();

        var history = await db.Messages.Where(x => x.ChatId == chat.Id && x.State == "complete").OrderBy(x => x.Id).ToListAsync();

        var priorHistory = history.ToList();

        var user = new Message { ChatId = chat.Id, Content = prompt, Attachments = run.Images };

        await ConversationInbox.SubmitAsync(run,user,ct);

        MarkRunSubmitted(run);

        await RefreshInboxAsync();

        if (history.Count == 0) run.Messages.Children.Clear();

        AddHistoryActions(user, AddMessage("user", prompt, user.Attachments, run.Messages));

        ScrollRunToBottom(run);

        Message? active = null; AssistantMessageUi? assistantUi = null;

        try

        {

            if (!provider.SupportsImages && run.Images.Count > 0)

            {

                var described = await VisionFor(run).PrepareAsync(new JsonArray(ChatEngine.ToWire(user)), ct);

                prompt = described[0]!["content"]!.GetValue<string>(); images.Clear();

            }

            await EnsureOpenCodeServerAsync(provider, password, ct, run.Project);

            var directory = OpenCodeDirectory(run.Project);

            var link = await db.ExternalChatSessions.SingleOrDefaultAsync(x => x.ChatId == chat.Id && x.ProviderId == provider.Id, ct);

            var isNewSession = link == null;

            if (link == null)

            {

                link = new ExternalChatSession { ChatId = chat.Id, ProviderId = provider.Id,

                    SessionId = await openCodeEngine.CreateSessionAsync(provider, password, directory, chat.Title, ct) };

                db.ExternalChatSessions.Add(link); await db.SaveChangesAsync(ct);

            }



            var system = state.Language == "en"

                ? "You are connected through OpenCode inside Monolith Harness. Answer the user directly. Respect every permission denial from the application."

                : "Tu es connecté à travers OpenCode dans Monolith Harness. Réponds directement à l’utilisateur en français. Respecte chaque refus d’autorisation de l’application.";

            run.Workflow = CreateWorkflow(run);

            await using var agentMcp = CreateMcpSession(run.Chat.Id);
            var agent = CreateAgentRuntime(run, password, agentMcp);

            system += await agent.InitializeAsync(ct);
            system += FeatureSettings.Read(run.Options.FeaturesJson).GoalInstructions(run.Chat.Id);
            system += "\n" + Skills.ReplyLanguage(run.Options.Language);

            if (run.Chat.OrchestrationMode != "disabled")

            {

                var report = await agent.StartAsync(ct);
                if (report.Length > 0)
                {

                system += "\nSubagent findings:\n" + report;

                var delegated = new Message { ChatId = chat.Id, Role = "assistant", Content = "Sous-agents / Subagents\n" + report };

                db.Messages.Add(delegated); await db.SaveChangesAsync(ct);

                AddAssistantMessage(delegated.Content, target: run.Messages, sourceProject: run.Project);
                }

            }

            if (!provider.OpenCodeTools) system += state.Language == "en"

                ? " OpenCode tools are disabled for this connection."

                : " Les outils OpenCode sont désactivés pour cette connexion.";

            var baseSystem = system;
            if (isNewSession && history.Count > 0)

            {

                var transcript = new StringBuilder("\n\nHistorique précédent de cette conversation Monolith Harness :\n");

                foreach (var item in priorHistory.TakeLast(20))

                {

                    var line = $"\n[{item.Role}] {item.Content}\n";

                    if (transcript.Length + line.Length > 20_000) break;

                    transcript.Append(line);

                }

                system += transcript;

            }



            active = new Message { ChatId = chat.Id, Role = "assistant", State = "interrupted" };

            db.Messages.Add(active); await db.SaveChangesAsync(ct);

            assistantUi = AddAssistantMessage("…", target: run.Messages); ScrollRunToBottom(run);

            SetRunStatus(run, T("OpenCode réfléchit…"));

            var inputEstimate = ContextWindow.EstimateText(system + prompt);

            inputEstimate += priorHistory.Sum(x => ContextWindow.EstimateText(x.Content));

            ShowContextUsage(run, inputEstimate, estimated: true);

            run.Tracker = new GenerationSpeedTracker();

            var maxAttempts = provider.BypassFreeLimitation ? 2 : 1;

            Completion? completion = null;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)

            {

                try

                {

                    completion = await openCodeEngine.PromptAsync(provider, password, directory, link.SessionId, prompt, system, images, update =>

                    {

                        if (update.Retry is { } retry) { SetRunStatus(run, retry.Describe(run.Options.Language)); return; }
                        run.ExportProgress = new(active.Id, update);

                        active.Content = update.Text; active.InputTokens = update.InputTokens; active.OutputTokens = update.OutputTokens; active.CachedInputTokens = update.CachedInputTokens; active.Seconds = update.Seconds;

                        ModelProgress(run, update);
                        var tokens = update.OutputTokens ?? ContextWindow.EstimateText(update.Text + update.Reasoning);

                        run.Tracker?.AddSample(update.Seconds, tokens);

                        if (update.Reasoning.Length > 0) assistantUi.UpdateThinking(update.Reasoning, update.Text.Length > 0, streaming: true);

                        assistantUi.UpdateContent(update.Text.Length > 0 ? update.Text : update.Reasoning.Length > 0 ? T("Raisonnement en cours…") : "…", streaming: true);

                        UpdateMetrics(run, update, inputEstimate);

                        if (IsVisible(run)) ScrollToBottom();

                    }, ct, (permission, token) => AuthorizeOpenCodePermissionAsync(provider, directory, permission, token), new(run.Chat.ExecutionMode, run.Chat.OrchestrationMode), run.Workflow, FeatureSettings.Read(run.Options.FeaturesJson));

                    run.WaitingForModel = false; RefreshModelActivity();
                    break;

                }

                catch (Exception ex) when (attempt < maxAttempts && provider.BypassFreeLimitation && IsFreeLimitError(ex))

                {

                    SetRunStatus(run, T("Bypass free limitation : renouvellement de session…"), StatusKind.Notice);

                    try

                    {

                        db.ExternalChatSessions.Remove(link);

                        await db.SaveChangesAsync(ct);

                    }

                    catch { }

                    link = new ExternalChatSession

                    {

                        ChatId = chat.Id,

                        ProviderId = provider.Id,

                        SessionId = await openCodeEngine.CreateSessionAsync(provider, password, directory, chat.Title, ct)

                    };

                    db.ExternalChatSessions.Add(link);

                    await db.SaveChangesAsync(ct);



                    if (!isNewSession && history.Count > 0)

                    {

                        var transcript = new StringBuilder("\n\nHistorique précédent de cette conversation Monolith Harness :\n");

                        foreach (var item in priorHistory.TakeLast(20))

                        {

                            var line = $"\n[{item.Role}] {item.Content}\n";

                            if (transcript.Length + line.Length > 20_000) break;

                            transcript.Append(line);

                        }

                        if (!system.Contains("Historique précédent"))

                            system += transcript;

                    }

                    active.Content = "";

                    assistantUi.UpdateContent("…", streaming: true);

                    await Task.Delay(500, ct);

                }

            }

            if (completion == null) throw new IOException(T("Impossible d’obtenir une réponse d’OpenCode."));

            active.Content = completion.Message["content"]?.GetValue<string>() ?? "";

            active.InputTokens = completion.InputTokens; active.OutputTokens = completion.OutputTokens; active.CachedInputTokens = completion.CachedInputTokens; active.Seconds = completion.Seconds; active.CompletedUtc = DateTime.UtcNow;

            active.WireJson = completion.Message.ToJsonString(); active.State = "complete";

            run.Tracker?.Complete(completion.Seconds, completion.OutputTokens ?? ContextWindow.EstimateText(active.Content));

            if (run.Tracker != null) messageTrackers[active.Id] = run.Tracker;

            run.Tracker = null;

            assistantUi.UpdateContent(active.Content);

            assistantUi.SetDuration(completion.Seconds, active.CompletedUtc);
            assistantUi.SetCachedInputTokens(completion.CachedInputTokens);

            var reasoning = completion.Message["reasoning_content"]?.GetValue<string>();

            if (!string.IsNullOrEmpty(reasoning)) assistantUi.UpdateThinking(reasoning, true);

            UpdateMetrics(run, new(active.Content, reasoning ?? "", completion.InputTokens, completion.OutputTokens, completion.Seconds) { CachedInputTokens = completion.CachedInputTokens }, inputEstimate);

            await db.SaveChangesAsync(ct);

            var contextTokens = (completion.InputTokens ?? inputEstimate) +

                                (completion.OutputTokens ?? ContextWindow.EstimateText(active.Content + reasoning));

            if (FeatureSettings.Read(run.Options.FeaturesJson).Compaction.ShouldCompact(contextTokens, provider.ContextLimit))

                await CompactOpenCodeSessionAsync(run, provider, password, directory, link, baseSystem, ct);

            else SetRunStatus(run, T("Réponse OpenCode terminée · historique enregistré."), StatusKind.Notice);

            AddHistoryActions(active, assistantUi.Container);

            active = null;

        }

        catch (Exception ex)

        {

            run.Tracker = null;

            run.Failed=true;

            SetRunStatus(run, ex is OperationCanceledException ? T("Génération arrêtée. Réponse partielle conservée.") : ex.Message,

                ex is OperationCanceledException ? StatusKind.Notice : StatusKind.Error);

            AppLog.Write(ex is OperationCanceledException ? AppLogLevel.Information : AppLogLevel.Error, "opencode.failed", ex, run.Chat.Id);

            if (active != null && assistantUi != null) assistantUi.UpdateContent(active.Content + T("\n[Réponse interrompue]"));

        }

    }



    async Task CompactOpenCodeSessionAsync(ConversationRun run, Provider target, string password, string directory, ExternalChatSession link, string system, CancellationToken ct)

    {
        using var consumption = TokenConsumption.Activity("compaction");

        var db = run.Db; var chat = run.Chat; var state = run.Options;

        SetRunStatus(run, T("Compaction automatique du contexte…"));

        var summaryProvider = new Provider

        {
            Id = target.Id,

            Name = target.Name,

            Kind = target.Kind,

            BaseUrl = target.BaseUrl,

            Model = target.Model,

            Username = target.Username,

            ContextLimit = target.ContextLimit,

            SupportsImages = target.SupportsImages,

            OpenCodeTools = false,

            BypassFreeLimitation = target.BypassFreeLimitation

        };

        string? nativeBrief = null;
        var history = await LoadContextHistoryAsync(run, ct);
        var result = await ConversationCompactor.CompactAsync(run, history, system, [], async (text, instruction, token) =>
        {
            // Native tool executions are held by OpenCode rather than the local message table.
            // Capture those facts once before starting bounded, isolated summary requests.
            if (nativeBrief == null)
            {
                var settings = FeatureSettings.Read(run.Options.FeaturesJson).Compaction;
                int nativeBudget = Math.Min(512, Math.Max(64, target.ContextLimit / 8));
                var nativeInstruction = ConversationCompactor.Instruction(settings, nativeBudget);
                var native = await openCodeEngine.PromptAsync(summaryProvider, password, directory, link.SessionId,
                    "Summarize native tool actions, affected files, results, errors and remaining work in this session. Omit conversation narrative.",
                    nativeInstruction, [], _ => { }, token, retrySettings: FeatureSettings.Read(run.Options.FeaturesJson));
                nativeBrief = native.Message["content"]?.GetValue<string>() ?? "";
                if (string.IsNullOrWhiteSpace(nativeBrief) || ContextWindow.EstimateText(nativeBrief) > nativeBudget)
                    throw new IOException(WorkflowText("Résumé des outils natifs invalide : historique conservé.", "Invalid native tool summary: original preserved."));
                text += "\n\nNative tool work (additional context):\n" + nativeBrief;
            }
            var isolated = await openCodeEngine.CreateSessionAsync(summaryProvider, password, directory, "Compactage", token);
            var summary = await openCodeEngine.PromptAsync(summaryProvider, password, directory, isolated, text, instruction,
                [], _ => { }, token, retrySettings: FeatureSettings.Read(run.Options.FeaturesJson));
            return summary.Message["content"]?.GetValue<string>() ?? "";
        }, ct);
        ShowContextUsage(run, result.AfterTokens, estimated: true);
        SetRunStatus(run, result.Changed
            ? WorkflowText("Contexte compacté · ", "Context compacted · ") + $"{result.BeforeTokens:N0} → {result.AfterTokens:N0} tokens"
            : WorkflowText("Cible de compactage non atteinte : historique conservé.", "Compaction target not met: original history preserved."), StatusKind.Notice);
    }
    async Task<string> AuthorizeOpenCodePermissionAsync(Provider target, string directory, OpenCodePermission permission, CancellationToken ct)

    {

        var resource = permission.Resources.Count == 0 ? "*" : string.Join(" | ", permission.Resources);

        var scope = "opencode|" + target.Id + "|" + directory.ToLowerInvariant() + "|" + permission.Action.ToLowerInvariant() + "|" + resource.ToLowerInvariant();

        var existing = await db.PermissionGrants.AnyAsync(x => x.Scope == scope, ct);

        var details = T("OpenCode demande l’autorisation d’utiliser : ") + permission.Action + "\n" + T("Cible : ") + resource;

        if (!string.IsNullOrWhiteSpace(permission.Details)) details += "\n\n" + permission.Details[..Math.Min(permission.Details.Length, 2000)];

        var allowed = await RequestAccessAsync(scope, T("Autorisation d’outil OpenCode"), details,

            T("OpenCode · ") + permission.Action + " · " + resource, ct);

        if (!allowed) return "reject";

        var permanent = existing || await db.PermissionGrants.AnyAsync(x => x.Scope == scope, ct);

        return permanent ? "always" : "once";

    }



    public static bool IsFreeLimitError(Exception? ex)

    {

        if (ex == null) return false;

        var text = ex.Message + " " + (ex.InnerException?.Message ?? "");

        return text.Contains("FreeUsageLimitError", StringComparison.OrdinalIgnoreCase) ||

               text.Contains("free_tier_limit", StringComparison.OrdinalIgnoreCase) ||

               text.Contains("Free limit reached", StringComparison.OrdinalIgnoreCase) ||

               text.Contains("Free usage exceeded", StringComparison.OrdinalIgnoreCase) ||

               text.Contains("rate_limit", StringComparison.OrdinalIgnoreCase) ||

               text.Contains("429", StringComparison.OrdinalIgnoreCase) ||

               text.Contains("quota", StringComparison.OrdinalIgnoreCase) ||

               text.Contains("free limitation", StringComparison.OrdinalIgnoreCase);

    }

}
