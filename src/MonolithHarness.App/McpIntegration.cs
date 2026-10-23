using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    async Task SyncMcpFile()
    {
        try { await Task.Run(async () => { await using var context = new HarnessDb(); await McpConfigFile.SyncAsync(context, (s, _) => Task.FromResult(KeyVault.Encrypt(s)), decrypt: (s, _) => Task.FromResult(KeyVault.Decrypt(s))); }); }
        catch (Exception ex) { ShowStatus("MCP.json : " + ex.Message, StatusKind.Error); }
    }
    McpSession CreateMcpSession(int chatId) => new(async ct =>
    {
        await using var context = new HarnessDb();
        await SyncMcpFile();
        var list = await context.McpServers.AsNoTracking().ToListAsync(ct);
        var features = FeatureSettings.Read((await context.States.AsNoTracking().SingleAsync(ct)).FeaturesJson);
        if(features.BrowserMode == "chrome") list.Add(features.ChromeServer(chatId));
        return list;
    }, (secret, _) => Task.FromResult(KeyVault.Decrypt(secret)),
        (scope, title, details, ct) => RequestAccessAsync(scope, title, details, title, ct));

    async Task OpenMcpSettingsAsync()
    {
        if (settingsNavigation != null && settingsMcpTab >= 0)
        { settingsNavigation.SelectedIndex = settingsMcpTab; settingsWindow?.Activate(); return; }
        mcpSettingsRequested = true; await Settings();
    }
    async Task ManageChatMcpAsync(Chat owner, string argument)
    {
        argument = argument.ToLowerInvariant();
        if (argument is "on" or "off")
        {
            if (conversationRuns.ContainsKey(owner.Id)) throw new InvalidOperationException(WorkflowText("Arrêtez la réponse avant de changer l’accès MCP.", "Stop the response before changing MCP access."));
            var options = ConversationAgents.Read(owner.AgentOptionsJson); options.McpEnabled = argument == "on";
            owner.AgentOptionsJson = options.Json(); await db.SaveChangesAsync();
            ShowStatus(WorkflowText("MCP · ", "MCP · ") + (options.McpEnabled ? WorkflowText("activé", "enabled") : WorkflowText("désactivé", "disabled")), StatusKind.Notice);
            return;
        }
        if (argument is not ("" or "list" or "settings")) throw new ArgumentException("/mcp list|on|off|settings");
        if (argument == "settings") { await OpenMcpSettingsAsync(); return; }
        await SyncMcpFile();
        var servers = await db.McpServers.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        var panel = new StackPanel { Spacing = 12 };
        var available = McpAccess.Allowed(owner) && !NativeChatProvider;
        var reason = !McpAccess.Enabled(owner) ? WorkflowText("Désactivé pour cette conversation. Tapez /mcp on pour l’activer.", "Disabled for this chat. Type /mcp on to enable.") :
            NativeChatProvider ? WorkflowText("Ce fournisseur utilise ses propres outils. Les serveurs MCP Monolith nécessitent un fournisseur direct.", "This provider uses its own tools. Monolith MCP servers require a direct provider.") :
            !owner.AllowOutsideResources ? WorkflowText("Activez l’accès aux ressources extérieures pour utiliser MCP.", "Enable access to outside resources to use MCP.") :
            owner.SandboxEnabled ? WorkflowText("MCP est indisponible en sandbox.", "MCP is unavailable in sandbox.") :
            !available ? WorkflowText("MCP est indisponible en mode Plan ou Proposition.", "MCP is unavailable in Plan or Proposal mode.") :
            WorkflowText("Les serveurs activés seront connectés au prochain envoi, après autorisation. Leurs outils seront alors transmis au modèle.", "Enabled servers will connect on the next message, after approval. Their tools will then be sent to the model.");
        panel.Children.Add(Label(reason, 13));
        if (servers.Count == 0) panel.Children.Add(Label(WorkflowText("Aucun serveur configuré.", "No configured servers."), 13));
        foreach (var server in servers) panel.Children.Add(Label((server.Enabled ? "● " : "○ ") + server.Name + " · " + server.Transport + " · " +
            (server.Enabled ? WorkflowText("activé", "enabled") : WorkflowText("désactivé", "disabled")), 13));
        var result = await ShowDialogAsync(new ContentDialog { XamlRoot = root.XamlRoot, Title = "MCP",
            Content = new ScrollViewer { Content = panel, MaxHeight = 450 }, PrimaryButtonText = WorkflowText("Configurer les serveurs", "Configure servers"), CloseButtonText = UiText.T("Fermer") });
        if (result == ContentDialogResult.Primary) await OpenMcpSettingsAsync();
    }

    void SetPendingMcpImage(Attachment image)
    {
        pendingToolScreenshot = image.Data; pendingToolScreenshotLabel = image.Name;
        pendingToolScreenshotMime = image.Mime; pendingToolScreenshotWidth = pendingToolScreenshotHeight = 0;
    }
}
