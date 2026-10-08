using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public sealed record AgentToolResult(string Text, Attachment? Image = null);

/// <summary>Shared swarm orchestration. Every descendant inherits permissions and the same run budget.</summary>
public sealed class AgentRuntime(ConversationSession run, CustomSkills skills,
    Func<JsonArray, JsonArray, CancellationToken, Task<Completion>> complete,
    Func<string, string, CancellationToken, Task<bool>> approve,
    Func<string, Task> progress, Func<CancellationToken, Task<string>>? liveSkills = null, Func<SubagentRecord, Task>? childUpdate = null,
    Func<Provider, JsonArray, JsonArray, Action<GenerationUpdate>, CancellationToken, Task<Completion>>? completeWithProvider = null,
    Func<string, CancellationToken, Task>? enableSkill = null,
    Func<CancellationToken, Task<JsonArray>>? hostDefinitions = null,
    Func<string, JsonObject, CancellationToken, Task<AgentToolResult>>? hostTool = null)
{
    int delegated;
    readonly object budgetGate = new();
    readonly SemaphoreSlim sharedAgentTools = new(1, 1);
    static readonly AsyncLocal<WorkflowTools?> childWorkflow = new();
    public static WorkflowTools? CurrentChildWorkflow => childWorkflow.Value;
    readonly FeatureSettings agentSettings = FeatureSettings.Read(run.Options.FeaturesJson);
    readonly SemaphoreSlim modelSlots = new(Math.Clamp(FeatureSettings.Read(run.Options.FeaturesJson).AgentParallelism, 1, 64));
    string context = "";
    string conversationContext = "";
    readonly ConversationAgents agentOptions = ConversationAgents.Read(run.Chat.AgentOptionsJson);
    int TotalBudget => Math.Clamp(agentSettings.AgentMaxTotal, 1, ConversationAgents.MaximumTeamSize);
    int MaxDepth => Math.Clamp(agentSettings.AgentMaxDepth, 1, 8);
    int MaxSteps => Math.Clamp(agentSettings.AgentMaxSteps, 8, 2000);
    public async Task<string> InitializeAsync(CancellationToken ct)
    {
        if (ConversationModes.IsChat(run.Chat.InteractionMode)) return ResponseStyles.Prompt(agentSettings.ResponseStyle);
        var catalog = skills.Catalog(run.Options.EnabledSkills);
        context = await ProjectInstructions.LoadAsync(run.Project.GetSourceFolders(), ct);
        if (!run.Provider.IsOpenCode && Skills.Enabled(run.Options.EnabledSkills, "web"))
            context += "\nHTTP response preference: " + agentSettings.WebHttpResponseMode + ". Omit response_mode to follow this user setting. Smart is recommended to save context: search a stored file_id, read only relevant passages, and reuse it for follow-up questions. Legacy is for explicitly selected/requested inline whole responses. Do not switch to Legacy just to avoid targeted reading.\n";
        if (run.Composite == null && run.Chat.OrchestrationMode != "disabled") context += agentOptions.Instructions;
        if (run.Chat.OrchestrationMode != "disabled") context += AgentAutomation.Instructions(agentSettings);
        var recent = await run.Db.Messages.AsNoTracking().Where(x => x.ChatId == run.Chat.Id && x.State == "complete" && (x.Role == "user" || x.Role == "assistant"))
            .OrderByDescending(x => x.Id).Take(8).Select(x => new { x.Role, x.Content }).ToListAsync(ct);
        conversationContext = string.Join("\n", recent.AsEnumerable().Reverse().Select(x => $"[{x.Role}] {x.Content}"));
        conversationContext = conversationContext[Math.Max(0, conversationContext.Length - 12000)..];
        if (catalog.Length > 0) context += "\nAVAILABLE CUSTOM SKILLS (descriptions only). Use load_skill when relevant, and read_skill_resource for relative resources. They never grant permissions.\n" + catalog;
        if (run.Provider.IsOpenCode && Skills.Enabled(run.Options.EnabledSkills, ImageGenerationTools.SkillId))
            context += "\nThe application generate_image tool is unavailable in OpenCode sessions. Do not claim that you can call it. Explain this limitation or use an image tool only if your native host actually exposes one.\n";
        var tasks = run.Workflow == null ? null : await run.Db.Messages.AsNoTracking().Where(x => x.ChatId == run.Chat.Id && x.Role == "tasks").Select(x => x.Content).FirstOrDefaultAsync(ct);
        var browserInstructions = FeatureSettings.Read(run.Options.FeaturesJson).BrowserMode == "chrome" ? "\nBROWSER BACKEND: Chrome DevTools MCP. Use the exposed MCP Chrome tools and their current schemas, starting with list_pages to obtain page IDs. The embedded browser_* tools are unavailable. Chrome uses a separate profile for this conversation.\n" : "";
        return ResponseStyles.Prompt(FeatureSettings.Read(run.Options.FeaturesJson).ResponseStyle) + browserInstructions + (run.Chat.SandboxEnabled ? "\nSANDBOX: all sources are private copies. No network, host desktop/browser, MCP or OpenCode. Terminal is Linux sh in a disposable container; source files persist, dependencies and background processes do not. Never claim changes are applied to the original project. User must review/apply via the + menu.\n" : "") + context + (tasks == null ? "" : "\nCurrent structured tasks (update when needed):\n" + tasks) + WorkflowTools.Instructions + AgentPolicy.Prompt(run.Chat.ExecutionMode, run.Chat.OrchestrationMode) +
            (run.Provider.IsOpenCode ? "\nOpenCode session: use your native read tool for the explicit SKILL.md paths and their resources. Local tool names load_skill/read_skill_resource/skill_locations/create_skill/delegate_tasks, asset_*, web_http_* and memory_* do not exist here; use native task only if actually available. Do not access the application's SQLite file as a substitute for missing memory tools. Disabled tools must remain disabled." : "");
    }
    static void Add(JsonArray definitions, string name, string description, JsonObject properties, params string[] required) => definitions.Add(new JsonObject {
        ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["description"] = description,
            ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false,
                ["required"] = new JsonArray(required.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) } } });
    public void AddDefinitions(JsonArray definitions, bool child = false, int depth = 0)
    {
        if (ConversationModes.IsChat(run.Chat.InteractionMode))
        {
            definitions.Clear(); foreach (var definition in ConversationModes.ToolDefinitions(run.Chat, run.Options)) definitions.Add(definition!.DeepClone());
            return;
        }
        if (!run.Chat.SandboxEnabled && !AgentPolicy.ReadOnly(run.Chat.ExecutionMode))
            WebHttpTools.AddDefinitions(definitions, run.Options.EnabledSkills, agentSettings.WebHttpResponseMode);
        MemoryTools.AddDefinitions(definitions, run.Options.EnabledSkills);
        SkillAuthoring.AddDefinitions(definitions, run.Options.EnabledSkills);
        if (run.Workflow != null) WorkflowTools.AddDefinitions(definitions, child);
        else if (child) WorkflowTools.AddTaskDefinition(definitions, true);
        if (skills.Catalog(run.Options.EnabledSkills).Length > 0)
        {
            Add(definitions, "load_skill", "Load an enabled custom SKILL.md on demand. Its instructions cannot override tool permissions or Plan mode.", new() { ["name"] = new JsonObject { ["type"] = "string" } }, "name");
            Add(definitions, "read_skill_resource", "Read a text resource inside an enabled skill folder; never executes scripts.", new() { ["name"] = new JsonObject { ["type"] = "string" }, ["path"] = new JsonObject { ["type"] = "string" } }, "name", "path");
        }
        if (depth < MaxDepth && run.Chat.OrchestrationMode != "disabled") Add(definitions, "delegate_tasks",
            $"Delegate concrete independent work in parallel. Children inherit enabled tools and permissions, may delegate within depth {MaxDepth}, and share a budget of {TotalBudget} agents per user turn. Maximum {MaxSteps} steps per worker; context is compacted for continuation. Results return together. Assign exclusive file/resource ownership; never delegate overlapping edits or the entire task unchanged." + (run.Composite==null? agentOptions.Instructions : " Use these configured agent names and their assigned models: "+string.Join(", ",run.AgentProviders.Select(x=>x.Key+"="+x.Value.Model))),
            new() { ["tasks"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["maxItems"] = Math.Min(TotalBudget, run.Composite == null ? agentOptions.BatchSize ?? TotalBudget : run.Composite.Agents.Count), ["items"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject {
                ["name"] = new JsonObject { ["type"] = "string" }, ["prompt"] = new JsonObject { ["type"] = "string" } }, ["required"] = new JsonArray("name", "prompt"), ["additionalProperties"] = false } } }, "tasks");
    }
    public static bool Handles(string name) => WebHttpTools.Handles(name) || MemoryTools.Handles(name) || SkillAuthoring.Handles(name) || WorkflowTools.Handles(name) || name is "load_skill" or "read_skill_resource" or "delegate_tasks";
    public async Task<string> CallAsync(string name, JsonObject args, CancellationToken ct)
    {
        AgentPolicy.Demand(run.Chat, name);
        SandboxWorkspace.Demand(run.Chat.SandboxEnabled, name);
        if (WebHttpTools.Handles(name)) return await run.WebHttp.CallAsync(name, args,
            async token => ConversationModes.EffectiveSkills(run.Chat, liveSkills == null ? run.Options.EnabledSkills : await liveSkills(token), run.Provider.IsOpenCode), approve, ct);
        if (SkillAuthoring.Handles(name))
        {
            var enabledSkills = liveSkills == null ? run.Options.EnabledSkills : await liveSkills(ct);
            if (!Skills.Enabled(enabledSkills, SkillAuthoring.SkillId)) throw new UnauthorizedAccessException("Auto-création de skills désactivée.");
            if (name == "skill_locations") return SkillAuthoring.Locations(skills, run.Project.GetSourceFolders());
            return await SkillAuthoring.CreateAsync(skills, run.Project.GetSourceFolders(), args, async (scope, details, token) => {
                    var allowed = await approve(scope, details, token);
                    var current = liveSkills == null ? run.Options.EnabledSkills : await liveSkills(token);
                    return allowed && Skills.Enabled(current, SkillAuthoring.SkillId);
                },
                async (id, token) => {
                    if (enableSkill != null) await enableSkill(id, token);
                    if (!Skills.Enabled(run.Options.EnabledSkills, id)) run.Options.EnabledSkills += "," + id;
                }, ct);
        }
        if (MemoryTools.Handles(name)) return await MemoryTools.CallAsync(run, name, args,
            liveSkills ?? (_ => Task.FromResult(run.Options.EnabledSkills)),
            (scope, title, details, token) => approve(scope, title + "\n\n" + details, token), ct);
        if (WorkflowTools.Handles(name)) return await (run.Workflow ?? throw new InvalidOperationException("Questions unavailable")).CallAsync(name, args, ct);
        if (name == "delegate_tasks")
        {
            if (run.Chat.OrchestrationMode == "disabled") throw new UnauthorizedAccessException("Sous-agents désactivés.");
            var tasks = (args["tasks"] as JsonArray ?? throw new ArgumentException("tasks requis.")).Select(x => (
                Name: x?["name"]?.GetValue<string>() ?? "", Prompt: x?["prompt"]?.GetValue<string>() ?? "")).ToList();
            return await DelegateAsync(tasks, 0, "", ct);
        }
        if (name is not ("load_skill" or "read_skill_resource")) throw new ArgumentException("Outil inconnu.");
        var enabled = liveSkills == null ? run.Options.EnabledSkills : await liveSkills(ct);
        return await skills.ReadAsync(args["name"]?.GetValue<string>() ?? "", name == "load_skill" ? null : args["path"]?.GetValue<string>() ?? "", enabled, ct);
    }
    public Task<string> StartAsync(CancellationToken ct) => StartAsync(ct, false);
    public Task<string> ForcedAsync(CancellationToken ct) => StartAsync(ct, true);
    async Task<string> StartAsync(CancellationToken ct, bool force)
    {
        if (ConversationModes.IsChat(run.Chat.InteractionMode)) return "";
        if (run.Chat.OrchestrationMode == "disabled" && !force) return "";
        var forced = force || run.Chat.OrchestrationMode == "forced";
        if (run.Composite is { } composite)
            return await DelegateAsync(composite.Agents.Select(x => (x.Name, x.Task + "\n\nUser request:\n" + run.Prompt)).ToList(), 0, "", ct);
        if (forced && !agentOptions.AutomaticCount && !agentOptions.AutomaticRoles)
            return await DelegateAsync(agentOptions.Roles.Take(agentOptions.Count).Select(x => (x.Name, "User request:\n" + run.Prompt)).ToList(), 0, "", ct);
        await progress(forced ? "Préparation des rôles des sous-agents / Planning subagent roles" : "Auto · Évaluation de la délégation / Evaluating delegation");
        var plan = await complete(new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = context + AgentPolicy.Prompt(run.Chat.ExecutionMode, run.Chat.OrchestrationMode) + "\nEvaluate the request and plan concrete independent subtasks. Do not solve or execute it. Return ONLY JSON {\"tasks\":[{\"name\":\"role\",\"prompt\":\"task including relevant context and exclusive file/resource ownership\"}]}. Each name <=80 characters, prompt <=12000. Never assign overlapping edits. " + agentOptions.Instructions + AgentAutomation.Instructions(agentSettings) + $" Shared budget: {TotalBudget} agents, depth {MaxDepth}, {MaxSteps} steps each. Enabled skills: {run.Options.EnabledSkills}. " +
                (forced ? "Delegation is required. " : "Return an empty tasks array only when direct execution is appropriate for the configured Auto behavior. ") +
                (agentOptions.AutomaticCount ? $"Choose a useful team size up to {TotalBudget}." : $"If delegating, return exactly {agentOptions.Count} tasks.") },
            new JsonObject { ["role"] = "user", ["content"] = "Recent conversation (context only):\n" + conversationContext + "\n\nCurrent request:\n" + run.Prompt }), [], ct);
        var text = plan.Message["content"]?.GetValue<string>() ?? "";
        var start = text.IndexOf('{'); var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return InvalidPlan(forced);
        List<(string Name, string Prompt)> tasks;
        try { tasks = JsonNode.Parse(text[start..(end + 1)])?["tasks"]?.AsArray().Select(x => (
            Name: x?["name"]?.GetValue<string>() ?? "", Prompt: x?["prompt"]?.GetValue<string>() ?? "")).ToList() ?? [];
        } catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return InvalidPlan(forced); }
        if (!forced && tasks.Count == 0) { await progress("Auto · Traitement direct / Direct execution"); return ""; }
        if (!agentOptions.AutomaticCount && tasks.Count != agentOptions.Count) return InvalidPlan(forced);
        try { return await DelegateAsync(tasks, 0, "", ct); }
        catch (ArgumentException) when (!forced) { return InvalidPlan(false); }
    }

    static string InvalidPlan(bool forced) => forced ? throw new InvalidOperationException("Plan des sous-agents invalide. Réessayez ou choisissez les rôles manuellement / Invalid subagent plan.") :
        "Auto : le modèle a renvoyé un plan de délégation invalide. Aucun agent lancé. Évaluez à nouveau des tâches indépendantes avec delegate_tasks et suivez les réglages Auto / Invalid Auto plan; no agents started. Reassess delegation with delegate_tasks.";

    async Task<string> DelegateAsync(List<(string Name, string Prompt)> tasks, int depth, string parent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (depth >= MaxDepth) throw new InvalidOperationException("Profondeur de délégation configurée atteinte / Configured delegation depth reached.");
        if(run.Composite!=null && tasks.Any(x=>!run.AgentProviders.ContainsKey(x.Name)))throw new ArgumentException("Utilisez le nom d’un sous-agent configuré dans le modèle composé.");
        if (tasks.Count < 1 || tasks.Count > TotalBudget || tasks.Any(x => string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 80 || string.IsNullOrWhiteSpace(x.Prompt) || x.Prompt.Length > 12000))
            throw new ArgumentException("Tâches requises dans le budget configuré ; nom 80 caractères et consigne 12000 caractères maximum.");
        if (run.Composite == null && agentOptions.BatchSize is { } size && tasks.Count > size) throw new ArgumentException("Respectez la taille d’équipe choisie / Respect the configured team size.");
        if (tasks.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != tasks.Count) throw new ArgumentException("Noms de sous-agents uniques requis / Unique agent names required.");
        if (run.Composite == null && !agentOptions.AutomaticRoles)
        {
            tasks = tasks.Select(task =>
            {
                var role = agentOptions.Roles.FirstOrDefault(x => x.Name.Equals(task.Name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException("Utilisez les rôles configurés / Use configured roles.");
                return (task.Name, role.Instruction + "\n\n" + task.Prompt);
            }).ToList();
        }
        lock (budgetGate)
        {
            if (delegated + tasks.Count > TotalBudget) throw new InvalidOperationException($"Budget de {TotalBudget} sous-agents atteint ; {TotalBudget - delegated} disponible(s). Intégrez les résultats déjà obtenus / Shared agent budget reached.");
            delegated += tasks.Count;
        }
        var results = await Task.WhenAll(tasks.Select(task => ChildAsync(task.Name, task.Prompt, depth + 1, parent, ct)));
        return string.Join("\n\n", results);
    }
    async Task<string> ChildAsync(string name, string prompt, int depth, string parent, CancellationToken ct)
    {
        var provider=run.AgentProviders.GetValueOrDefault(name) ?? run.Provider;
        ResourceAccessPolicy.DemandProvider(run.Chat, provider);
        var path = parent.Length == 0 ? name : parent + " › " + name;
        await progress($"Sous-agent / Subagent · {path} · démarré / started");
        var mode = run.Chat.ExecutionMode;
        var source = new SourceAccess(run.Project.GetSourceFolders());
        var enabled = liveSkills == null ? run.Options.EnabledSkills : await liveSkills(ct);
        var system = Skills.Prompt(enabled, run.Options.Language, source.Roots.Count > 0, BrowserSkillAccess.Enabled(enabled), Skills.Enabled(enabled, "write_sources")) + context + AgentPolicy.Prompt(mode, run.Chat.OrchestrationMode) + ResourceAccessPolicy.Prompt(run.Chat) +
            $"\nYou are subagent {path}, depth {depth}/{MaxDepth}. Complete your assigned task and report actual edits, findings, validation and remaining limitations to your parent. You inherit the conversation's enabled tools and permissions. Coordinate exclusive ownership of files and shared interactive resources. Delegate only independent portions when beneficial; never your entire task unchanged. Maximum {MaxSteps} model steps; the whole team shares {TotalBudget} agents. " +
            (depth >= MaxDepth ? "Further delegation is unavailable at this depth. " : "") +
            (!provider.IsOpenCode || provider.OpenCodeTools ? SubagentTasks.Instructions : "\nThe native task-list tool is unavailable. Do not invent task counts or completion percentages. ") +
            (provider.IsOpenCode ? "\nOpenCode child session: native task is disabled because its descendants cannot be counted in the shared application budget. Complete this assigned task with native enabled tools. delegate_tasks and memory_* are not native tools. Do not access the application's SQLite file through other tools." : "");
        var wire = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = system }, new JsonObject { ["role"] = "user", ["content"] = "Conversation context (context only):\n" + conversationContext + "\n\nAssigned task:\n" + prompt });
        var transcript = new JsonArray();
        var child = new SubagentRecord { ChatId=run.Chat.Id,Name=path,Task=$"{provider.Name} · {provider.Model}\n{prompt}",Activity="Démarrage / Starting" };
        bool stored=false;
        async Task Report(string activity)
        {
            child.Activity=activity; child.TranscriptJson=transcript.ToJsonString();
            await using var db=new HarnessDb(run.Db.Database.GetDbConnection().DataSource);
            if(!stored) {db.Subagents.Add(child);stored=true;} else db.Subagents.Update(child);
            await db.SaveChangesAsync(CancellationToken.None);
            if(childUpdate!=null)await childUpdate(child);
        }
        async Task SaveChildTasks(JsonArray tasks, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // OpenCode initially polls an empty native list; keep any already announced work.
            if (tasks.Count == 0) return;
            SubagentTasks.Write(transcript, tasks);
            await Report("Plan des tâches mis à jour / Task plan updated");
        }
        var previousWorkflow = childWorkflow.Value;
        childWorkflow.Value = run.Workflow?.ForChild(SaveChildTasks);
        await Report("Démarrage / Starting");
        int input = 0, output = 0;
        var loop = new ToolLoopGuard();
        bool planReminderSent = false, finalReminderSent = false;
        SubagentWorktrees? worktrees = null;
        IDisposable? projectBinding = null;
        try
        {
            if (agentOptions.UseGitWorktree && run.Chat.AllowOutsideResources && !run.Chat.SandboxEnabled && !AgentPolicy.ReadOnly(mode) &&
                (Skills.Enabled(enabled, "write_sources") || Skills.Enabled(enabled, "patch_sources") || Skills.Enabled(enabled, "terminal") || Skills.Enabled(enabled, "python") || GitTools.WriteEnabled(enabled) || provider.IsOpenCode && provider.OpenCodeTools))
            {
                await Report("Préparation du worktree Git / Preparing Git worktree");
                worktrees = await SubagentWorktrees.CreateAsync(run, path, ct);
                if (worktrees != null)
                {
                    projectBinding = SubagentWorkspace.Enter(run, worktrees.Project);
                    source = new SourceAccess(run.Project.GetSourceFolders());
                    child.Task += "\n\n" + worktrees.Description;
                    system += "\nISOLATED GIT WORKTREE: work only in these attached worktree folders: " + string.Join(", ", source.Roots) +
                        "\n" + worktrees.Description + "\nThe branch starts from the parent's last commit. Uncommitted parent edits are not copied. Never edit or commit in the original checkout, and keep the assigned worktree branch. Close terminals you created once their commands/tests finish; do not stop another agent's terminals. Report all validation and remaining issues. After successful completion, Monolith commits this worktree's changes and integrates them automatically only when the parent is clean and the merge has no conflicts. Do not run git merge, switch, checkout, reset or clean on the parent repository.";
                    wire[0]!["content"] = system;
                    transcript.Add(new JsonObject { ["role"] = "system", ["content"] = worktrees.Description });
                    await Report("Worktree Git prêt / Git worktree ready");
                }
            }
            for (int step = 0; step < MaxSteps; step++)
            {
                ct.ThrowIfCancellationRequested();
                enabled = liveSkills == null ? run.Options.EnabledSkills : await liveSkills(ct);
                var definitions = hostDefinitions == null ? ChatEngine.ToolDefinitions(source.Roots.Count > 0 && SourceTools.CanRead(enabled), false, Skills.Enabled(enabled, "write_sources") && source.Roots.Count > 0) : await hostDefinitions(ct);
                if (hostDefinitions == null) SourceTools.AddDefinitions(definitions, source.Roots.Count > 0, enabled);
                run.Options.EnabledSkills = enabled;
                AddDefinitions(definitions, child: true, depth: depth); AgentPolicy.Filter(definitions, run.Chat);
                SandboxWorkspace.Filter(definitions, run.Chat.SandboxEnabled);
                if (child.Progress.HasPlan)
                    wire[0]!["content"] = system + "\nYour current task list (keep it accurate through todowrite):\n" +
                        System.Text.Json.JsonSerializer.Serialize(SubagentTasks.Read(child.TranscriptJson), System.Text.Json.JsonSerializerOptions.Web);
                var compaction = FeatureSettings.Read(run.Options.FeaturesJson).Compaction;
                if (!provider.IsOpenCode && compaction.ShouldCompact(ContextWindow.Estimate(wire) + ContextWindow.Estimate(definitions), provider.ContextLimit))
                {
                    await Report("Compactage du contexte / Compacting context");
                    var history = wire.Skip(2).Select(node => new Message {
                        Role = node?["role"]?.GetValue<string>() ?? "user",
                        Content = node?["content"] is JsonArray parts ? string.Join("\n", parts.Select(part =>
                            part?["type"]?.GetValue<string>() == "text" ? part?["text"]?.GetValue<string>() : "[Tool image]")) : node?["content"]?.GetValue<string>() ?? "",
                        WireJson = node!.ToJsonString()
                    }).ToList();
                    var fixedContext = wire[0]!["content"]!.GetValue<string>() + "\n" + wire[1]!["content"]!.GetValue<string>();
                    var reduced = await ConversationCompactor.ReduceAsync(history, fixedContext, definitions, provider.ContextLimit,
                        compaction, async (text, instruction, token) => {
                            using var consumption = TokenConsumption.Activity("compaction");
                            var summary = await CompleteChildAsync(provider, new JsonArray(
                                new JsonObject { ["role"] = "system", ["content"] = instruction },
                                new JsonObject { ["role"] = "user", ["content"] = text }), [], _ => { }, token);
                            input += summary.InputTokens ?? 0; output += summary.OutputTokens ?? 0;
                            return summary.Message["content"]?.GetValue<string>() ?? "";
                        }, ct);
                    if (reduced.Changed)
                    {
                        var next = new JsonArray(wire[0]!.DeepClone(), wire[1]!.DeepClone());
                        foreach (var item in reduced.History) next.Add(ChatEngine.ToWire(item));
                        wire = next;
                    }
                }
                await Report("Réflexion / Thinking");
                var response = await CompleteChildAsync(provider, wire, definitions, _ => { }, ct);
                input += response.InputTokens ?? 0; output += response.OutputTokens ?? 0;
                wire.Add(response.Message.DeepClone()); transcript.Add(response.Message.DeepClone());
                await Report("Réponse reçue / Response received");
                if (response.Message["tool_calls"] is not JsonArray { Count: > 0 } calls)
                {
                    if (!provider.IsOpenCode && !child.Progress.HasPlan && !planReminderSent)
                    {
                        planReminderSent = true;
                        wire.Add(new JsonObject { ["role"] = "user", ["content"] = "Announce your assigned task list with todowrite before concluding. Use only work you actually intend to do or have done; do not invent completed tasks." });
                        continue;
                    }
                    if (!provider.IsOpenCode && child.Progress.HasPlan && child.Progress.Completed + child.Progress.Cancelled < child.Progress.Total && !finalReminderSent)
                    {
                        finalReminderSent = true;
                        wire.Add(new JsonObject { ["role"] = "user", ["content"] = "Your announced task list still contains unfinished tasks. Update todowrite to reflect the actual completed, pending or cancelled work, then give your final report. Never mark unperformed work completed." });
                        continue;
                    }
                    var integration = worktrees == null ? "" : await worktrees.IntegrateAsync(ct);
                    if (integration.Length > 0)
                    {
                        child.Task += "\n" + integration;
                        transcript.Add(new JsonObject { ["role"] = "system", ["content"] = integration });
                    }
                    return $"[{path}] ({input} tokens entrée / {output} sortie déclarés)\n{response.Message["content"]?.GetValue<string>() ?? "Réponse vide."}" +
                        (worktrees == null ? "" : "\n\n" + worktrees.Description + "\n" + integration);
                }
                var images = new List<Attachment>();
                foreach (var call in calls)
                {
                    var tool = call?["function"]?["name"]?.GetValue<string>() ?? ""; string result;
                    try
                    {
                        await Report("Outil / Tool · " + tool);
                        if (!TerminalHub.IsBoundedWait(tool, call?["function"]?["arguments"]?.GetValue<string>() ?? "{}"))
                            await loop.CheckAsync(tool, call?["function"]?["arguments"]?.GetValue<string>() ?? "{}", run.Workflow, ct);
                        AgentPolicy.Demand(run.Chat, tool);
                        SandboxWorkspace.Demand(run.Chat.SandboxEnabled, tool);
                        if (liveSkills != null) enabled = await liveSkills(ct);
                        source.MaintainFileIndex = Skills.Enabled(enabled, FileIndexTools.SkillId) && !AgentPolicy.ReadOnly(mode);
                        if (!definitions.Any(x => x?["function"]?["name"]?.GetValue<string>() == tool)) throw new UnauthorizedAccessException("Outil non disponible pour ce sous-agent.");
                        var args = JsonNode.Parse(call!["function"]!["arguments"]!.GetValue<string>())!.AsObject();
                        if (tool is not ("todowrite" or "question") && !child.Progress.HasPlan)
                            throw new InvalidOperationException("Annoncez d’abord votre plan avec todowrite / Announce your task list with todowrite first.");
                        await ProjectResources.DemandToolAsync(run.Project, tool, args.ToJsonString(), approve, ct);
                        if (tool == "todowrite")
                        {
                            var tasks = WorkflowTools.ValidateTasks(args["todos"] as JsonArray ?? throw new ArgumentException("todos requis"));
                            if (tasks.Count == 0) throw new ArgumentException("Annoncez au moins une tâche / Declare at least one task.");
                            await SaveChildTasks(tasks, ct); result = tasks.ToJsonString();
                        }
                        else if (tool == "delegate_tasks") result = await DelegateAsync((args["tasks"] as JsonArray ?? throw new ArgumentException("tasks requis.")).Select(x => (
                            x?["name"]?.GetValue<string>() ?? "", x?["prompt"]?.GetValue<string>() ?? "")).ToList(), depth, path, ct);
                        else if (AgentRuntime.Handles(tool))
                        {
                            await sharedAgentTools.WaitAsync(ct);
                            try { result = await CallAsync(tool, args, ct); }
                            finally { sharedAgentTools.Release(); }
                        }
                        else if (hostTool != null)
                        {
                            var executed = await hostTool(tool, args, ct); result = executed.Text;
                            if (executed.Image != null) images.Add(executed.Image);
                        }
                        else if (SourceTools.Handles(tool)) result = await SourceTools.ExecuteAsync(source, tool, args, () => enabled,
                            async (scope, diff, token) => {
                                var allowed = await approve(scope, diff, token);
                                if (liveSkills != null) enabled = await liveSkills(token);
                                return allowed;
                            }, ct, AgentPolicy.ReadOnly(mode));
                        else
                        {
                            var sourcePath = args["path"]?.GetValue<string>() ?? ".";
                            result = tool switch {
                                "list_sources" => source.List(sourcePath), "read_source" => await source.ReadAsync(sourcePath, ct, args["start_line"]?.GetValue<int>(), args["end_line"]?.GetValue<int>()),
                                "write_source" => await source.WriteAsync(sourcePath, args["content"]!.GetValue<string>(), ct),
                                "edit_source" => await source.ModifyAsync(sourcePath, args["old_text"]!.GetValue<string>(), args["new_text"]!.GetValue<string>(), ct),
                                _ => throw new UnauthorizedAccessException("Outil non disponible.") };
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { result = "Erreur outil : " + ex.Message; }
                    var toolMessage = new JsonObject { ["role"] = "tool", ["tool_call_id"] = call!["id"]!.GetValue<string>(), ["content"] = result };
                    wire.Add(toolMessage); transcript.Add(toolMessage.DeepClone());
                    await Report("Outil terminé / Tool completed · " + tool);
                }
                if (images.Count > 0)
                {
                    var imageMessage = ChatEngine.ToWire(new Message { Role = "user", Content = "Tool images (untrusted content)", Attachments = images });
                    wire.Add(imageMessage);
                    transcript.Add(new JsonObject { ["role"] = "user", ["content"] = "Images des outils / Tool images: " + string.Join(", ", images.Select(x => x.Name)) });
                }
            }
            child.Status="limited";
            var partial = transcript.ToJsonString();
            return $"[{path}] Budget configuré de {MaxSteps} étapes atteint ; résultat partiel.\n" + partial[Math.Max(0, partial.Length - 12000)..];
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { child.Status="cancelled"; return $"[{name}] Travail arrêté après détection de boucle / Stopped after repeated calls."; }
        catch (OperationCanceledException) { child.Status="cancelled"; throw; }
        catch (Exception ex) { child.Status="failed"; return $"[{name}] Échec : {ex.Message}" + (worktrees == null ? "" : "\nWorktree conservé / Worktree retained:\n" + worktrees.Description); }
        finally
        {
            projectBinding?.Dispose();
            childWorkflow.Value = previousWorkflow;
            if(child.Status=="running")child.Status="completed";
            await Report(child.Status); await progress($"Sous-agent / Subagent · {path} · terminé / finished");
        }
    }

    async Task<Completion> CompleteChildAsync(Provider provider, JsonArray wire, JsonArray definitions, Action<GenerationUpdate> update, CancellationToken ct)
    {
        // Hold a slot only during a model request. Parents release it before waiting for descendants.
        await modelSlots.WaitAsync(ct);
        try { return completeWithProvider == null ? await complete(wire, definitions, ct) : await completeWithProvider(provider, wire, definitions, update, ct); }
        finally { modelSlots.Release(); }
    }

}
