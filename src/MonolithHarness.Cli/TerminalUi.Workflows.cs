using MonolithHarness.Core;
using MonolithHarness.Core.Hosting;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Cli;

public sealed partial class TerminalUi
{
    async Task Queue(int id)
    {
        var items = (await client.Call("inbox.list", new { chatId = id }))!.AsArray();
        var key = await Prompt("File dâ€™attente / Queue", choices: items.Select(q => new Choice(I(q, "id").ToString(), S(q, "text"), S(q, "mode"))).Prepend(new("resume", "Reprendre la file / Resume queue")).ToList());
        if (key == null) return;
        if (key == "resume") { Post(() => { halted.Remove(id); if (!Running(id)) StartRun(id, async () => { await client.Call("inbox.resume", new { chatId = id }, lifetime.Token); }); }); return; }
        int itemId = int.Parse(key); var item = items.Single(q => I(q, "id") == itemId)!;
        var action = await Prompt("Message #" + key, S(item, "text"), [new("edit", "Modifier / Edit"), new("steer", "Injecter dans le tour actif / Steer"), new("delete", "Supprimer / Delete"), new("up", "Monter / Move up"), new("down", "Descendre / Move down")]);
        if (action is "up" or "down") await client.Call("inbox.move", new { id = itemId, chatId = id, direction = action == "up" ? -1 : 1 });
        if (action is "up" or "down") await client.Call("inbox.move", new { id = itemId, chatId = id, direction = action == "up" ? -1 : 1 });
        if (action == "delete") await client.Call("inbox.delete", new { id = itemId, chatId = id });
        if (action is "edit" or "steer")
        {
            var text = action == "edit" ? await Prompt("Modifier / Edit", initial: S(item, "text")) : S(item, "text");
            if (!string.IsNullOrWhiteSpace(text)) await client.Call("inbox.update", new { id = itemId, chatId = id, expectedText = S(item, "text"), text, steer = action == "steer" });
        }
    }
    async Task QueueAction(int id, int itemId, string action)
    {
        var items = (await client.Call("inbox.list", new { chatId = id }))!.AsArray();
        var item = items.FirstOrDefault(q => I(q, "id") == itemId);
        if (item == null) return;
        if (action == "delete") await client.Call("inbox.delete", new { id = itemId, chatId = id });
        else
        {
            var text = action == "edit" ? await Prompt("Modifier / Edit", initial: S(item, "text")) : S(item, "text");
            if (!string.IsNullOrWhiteSpace(text)) await client.Call("inbox.update", new { id = itemId, chatId = id, expectedText = S(item, "text"), text, steer = action == "steer" });
        }
    }
    async Task Agents(int id, Chat chat)
    {
        var children = (await client.Call("subagents", new { chatId = id }))!.Deserialize<List<SubagentRecord>>(HarnessService.Json)!;
        var key = await Prompt("Sous-agents / Subagents", "Mode : " + chat.OrchestrationMode, children.Select(c => new Choice(c.Id, c.Name, c.Status + " Â· " + c.Activity)).Prepend(new("mode", "Orchestration : Disabled / Auto / Forced")).ToList());
        if (key == "mode") { var mode = await Prompt("Orchestration", choices: [new("disabled", "DÃ©sactivÃ©e / Disabled"), new("auto", "Auto"), new("forced", "ForcÃ©e / Forced")]); if (mode != null) { await client.Call("chat.modes", new { id, orchestrationMode = mode }); await Refresh(); } }
        else if (key != null)
        {
            var child = children.Single(c => c.Id == key);
            var transcript = JsonNode.Parse(child.TranscriptJson)!.AsArray();
            await Show(child.Name + " Â· " + child.Status, child.Task + "\n\n" + string.Join("\n\n", transcript.Select(m => S(m, "role").ToUpperInvariant() + "\n" + (m?["content"] is JsonValue value && value.TryGetValue<string>(out var content) ? content : m?.ToJsonString()))));
        }
    }
    async Task Git(int id, int projectId)
    {
        var data = await client.Call("git.files", new { chatId = id, projectId }); var files = data!["files"]!.Deserialize<List<GitChangedFile>>(HarnessService.Json)!;
        if (files.Count == 0) { await Show("Git", L("Aucun fichier modifiÃ© (ou aucun dÃ©pÃ´t .git attachÃ©).", "No changed files (or no attached .git repository).")); return; }
        var key = await Prompt("Git Â· " + files.Count, choices: files.Select((f, i) => new Choice(i.ToString(), f.Path, $"{f.Status} +{f.Added ?? 0} âˆ’{f.Removed ?? 0}")).ToList());
        if (key == null) return; var file = files[int.Parse(key)];
        var diff = await client.Call("git.diff", new { chatId = id, projectId, repository = file.Repository, path = file.Path }); await Show(file.Path, diff!.GetValue<string>());
    }
    async Task Terminal(int id, string command)
    {
        if (command.Length > 0)
        {
            var terminal = await client.Call("terminals.create", new { chatId = id, name = TerminalText.Fit(command, 40) });
            await client.Call("terminals.start", new { chatId = id, terminalId = S(terminal, "id"), command });
        }
        while (true)
        {
            var terminals = (await client.Call("terminals.list", new { chatId = id }))!.Deserialize<List<TerminalHub.View>>(HarnessService.Json)!;
            var key = await Prompt("Terminaux / Terminals", "/terminal commande : lancer en arriÃ¨re-plan / start in background",
                terminals.Select(t => new Choice(t.Id, t.Name, t.Status)).ToList());
            if (key == null) return;
            var terminal = terminals.Single(t => t.Id == key);
            var action = await Prompt(terminal.Name + " Â· " + terminal.Status, terminal.Directory + "\n$ " + terminal.Command + "\n\n" + terminal.Output,
                [new("refresh", "Actualiser / Refresh"), new("stop", "ArrÃªter / Stop"), new("delete", "Fermer / Close terminal")]);
            if (action == "stop") await client.Call("terminals.stop", new { chatId = id, terminalId = key });
            if (action == "delete") await client.Call("terminals.delete", new { chatId = id, terminalId = key });
            if (action == null) return;
        }
    }
    async Task Memory(int projectId, int id, string query)
    {
        var store = new MemoryStore(client.Database); var access = new MemoryAccess(projectId, id);
        var page = await store.SearchAsync(access, query, ct: lifetime.Token);
        var key = await Prompt("MÃ©moire / Memory", "/memory mot-clÃ© Â· 20 rÃ©sultats maximum", page.Items.Select(m => new Choice(m.Id.ToString(), m.Title, m.Scope + " / " + m.Category)).ToList());
        if (key != null) { var entry = await store.ReadAsync(access, int.Parse(key), lifetime.Token); await Show(entry.Title, entry.Content); }
    }
    async Task Mcp(WorkspaceSnapshot snapshot)
    {
        var key = await Prompt("MCP", "MCP.json : " + McpConfigFile.FilePath, snapshot.Servers.Select(s => new Choice(s.Id.ToString(), (s.Enabled ? "[x] " : "[ ] ") + s.Name)).Prepend(new("import", "Importer un fichier JSON / Import JSON file")).ToList());
        if (key == null) return;
        if (key == "import")
        {
            var path = await Prompt("Fichier MCP JSON / MCP JSON file"); if (string.IsNullOrWhiteSpace(path)) return;
            var current = await client.Call("mcp.json.get"); var content = await File.ReadAllTextAsync(Path.GetFullPath(path.Trim('"')), lifetime.Token);
            if (await Prompt("Remplacer MCP.json / Replace MCP.json?", content, [new("no", "Annuler / Cancel"), new("yes", "Importer / Import")]) == "yes")
                await client.Call("mcp.json.save", new { content, expected = S(current, "content") });
        }
        else { int id = int.Parse(key); await client.Call("mcp.toggle", new { id, enabled = !snapshot.Servers.Single(s => s.Id == id).Enabled }); }
        await Refresh();
    }
    async Task Sandbox(int id, Chat chat)
    {
        var action = await Prompt("Sandbox", L("Docker/Podman requis. Les modifications restent dans une copie isolÃ©e jusquâ€™Ã  validation.", "Requires Docker/Podman. Changes stay in an isolated copy until approved."), [new("toggle", chat.SandboxEnabled ? "DÃ©sactiver / Disable" : "Activer / Enable"), new("review", "Examiner les modifications / Review changes")]);
        if (action == "toggle") { await client.Call("chat.modes", new { id, sandboxEnabled = !chat.SandboxEnabled }); await Refresh(); }
        if (action == "review")
        {
            var review = await client.Call("sandbox.review", new { chatId = id });
            var token = S(review, "token");
            try
            {
                var decision = await Prompt("Sandbox Â· " + I(review, "count"), S(review, "diff"),
                    [new("close", "Fermer / Close"), new("apply", "Appliquer au projet rÃ©el / Apply to real project")]);
                if (decision == "apply") await client.Call("sandbox.apply", new { chatId = id, token });
            }
            finally { await client.Call("sandbox.close", new { token }); }
        }
    }
    async Task Settings(WorkspaceSnapshot snapshot)
    {
        var action = await Prompt("RÃ©glages / Settings", client.Database, [new("language", "Langue / Language"), new("theme", "ThÃ¨me / Theme"), new("font", "Police et CRT / Font and CRT"), new("thinking", "RÃ©flexion / Thinking"), new("style", "Style de rÃ©ponse / Response style"), new("compaction", "Compactage / Compaction"), new("web", "Recherche web Â· HTTP / Web research Â· HTTP"), new("artifacts", "Entretien Â· Artefacts / Maintenance Â· Artifacts"), new("permissions", "Autorisations / Permissions"), new("continue", "Auto-continue : " + snapshot.State.AutoContinue), new("naming", "Nommage des conversations / Conversation naming"), new("vision", "Bypass image AI"), new("information", "Informations · date et PC / Information · date and computer"), new("logs", "Logs"), new("updates", "Mises Ã  jour GitHub / GitHub updates")]);
        if (action == "style")
        {
            var current = FeatureSettings.Read(snapshot.State.FeaturesJson).ResponseStyle;
            var value = await Prompt(L("Style de rÃ©ponse", "Response style"), L("AppliquÃ© aux prochains envois. DEFAULT conserve le comportement habituel.", "Applies to subsequent messages. DEFAULT keeps the usual behavior."),
                ResponseStyles.All.Select(style => new Choice(style.Id, L(style.French, style.English), style.Id == current ? "âœ“" : "")).ToList());
            if (value != null) await client.State(s => { var settings = FeatureSettings.Read(s.FeaturesJson); settings.ResponseStyle = value; s.FeaturesJson = settings.Json(); });
        }
        if (action == "updates") await UpdateSettings();
        if (action == "compaction") await CompactionPreferences(snapshot);
        if (action == "web") await WebHttpPreferences(snapshot);
        if (action == "artifacts") await ArtifactPreferences(snapshot);
        if (action == "naming") await NamingSettings(snapshot);
        if (action == "font") await ConfigureTerminalFont(snapshot);
        if (action == "vision") await VisionSettings(snapshot);
        if (action == "information") await InformationPreferences();
        if (action == "logs") await LogSettings(snapshot);
        if (action == "continue") await client.State(s => s.AutoContinue = !s.AutoContinue);
        if (action == "language") { var value = await Prompt("Language", choices: [new("fr", "FranÃ§ais"), new("en", "English")]); if (value != null) await client.State(s => s.Language = value); }
        if (action == "thinking") { var value = await Prompt("Thinking", choices: new[] { "auto", "low", "medium", "high", "none" }.Select(s => new Choice(s, s)).ToList()); if (value != null) await client.State(s => { if (ConversationModes.IsChat(CurrentChat?.InteractionMode)) s.ChatThinkingLevel = value; else s.ThinkingLevel = value; }); }
        if (action == "theme") await ChooseCliTheme(snapshot);
        if (action == "permissions")
        {
            var value = await Prompt("Permissions", L("Ce réglage s’applique au workspace partagé. Tout autoriser accepte sans analyse ; les règles explicites du projet restent appliquées.", "Applies to the shared workspace. Allow all accepts without analysis; explicit project rules still apply."),
                [new(PermissionModes.Ask, "Demander / Ask"), new(PermissionModes.Deny, "Tout refuser / Deny all"), new(PermissionModes.Allow, "Automatique · validateur configuré / Automatic · configured validator"), new(PermissionModes.Full, "⚠ Tout autoriser / Allow all"), new("validator", "Validation des commandes · installation et modèle / Command validation · installation and model")]);
            if (value == "validator") await CommandGuardPreferences(snapshot);
            else if (value != null) await client.State(s => s.PermissionMode = value);
        }
        await Refresh();
    }
}
