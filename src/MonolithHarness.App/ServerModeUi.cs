using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using MonolithHarness.Core;
using MonolithHarness.Core.Remote;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

/// <summary>
/// The server mode in one click: a power button beside the connection chip turns everything that talks to the server on or off (synchronization,
/// live channel, cloud view, shared providers, cloud requests) without signing out; the account and the links are kept. And what a local
/// conversation waiting in the server's queue shows: its position.
/// </summary>
public sealed partial class MainWindow
{
    ToggleButton? serverModeToggle;
    bool settingServerMode;

    FrameworkElement BuildServerModeToggle()
    {
        serverModeToggle = new ToggleButton
        {
            Content = FluentDesign.Icon("", 14), Padding = new(8, 8, 8, 8), VerticalAlignment = VerticalAlignment.Stretch, Visibility = Visibility.Collapsed
        };
        serverModeToggle.Click += async (_, _) =>
        {
            if (settingServerMode) return;
            var wanted = serverModeToggle.IsChecked == true;
            await Guard(() => SetServerModeAsync(wanted));
            RefreshServerModeToggle();
        };
        RefreshServerModeToggle();
        return serverModeToggle;
    }

    /// <summary>Visible once an account is signed in; pressed = the server mode is on.</summary>
    void RefreshServerModeToggle()
    {
        if (serverModeToggle == null) return;
        var link = Server.Link;
        serverModeToggle.Visibility = link.Configured && link.SignedIn ? Visibility.Visible : Visibility.Collapsed;
        settingServerMode = true;
        try { serverModeToggle.IsChecked = link.Enabled; }
        finally { settingServerMode = false; }
        var label = link.Enabled ? WorkflowText("Mode serveur activé : cliquez pour le désactiver", "Server mode on: click to turn it off")
            : WorkflowText("Mode serveur désactivé : cliquez pour l'activer", "Server mode off: click to turn it on");
        ToolTipService.SetToolTip(serverModeToggle, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(serverModeToggle, label);
    }

    /// <summary>
    /// Off: no cloud request may be running, the conversations hosted here go offline, the live channel closes and the sidebar shows the Local
    /// view only. On: a complete synchronization starts and the shared conversations attach again.
    /// </summary>
    async Task SetServerModeAsync(bool enabled)
    {
        if (Server.Link.Enabled == enabled) return;
        if (!enabled)
        {
            if (conversationRuns.Values.Any(x => IsCloudChat(x.Chat))) throw new InvalidOperationException(WorkflowText("Terminez les requêtes cloud en cours avant de désactiver le mode serveur.", "Finish the running cloud requests before turning the server mode off."));
            foreach (var chatId in sharedChats.Keys.ToList()) if (sharedChats.Remove(chatId, out var share)) await Bridge.Detach(share);
        }
        await Sync.SetEnabledAsync(enabled);
        InvalidateServerLink();
        if (enabled && Server.Link is { Configured: true, SignedIn: true })
        {
            Sync.Start();
            _ = ResumeSharesAsync();
        }
        RefreshServerChip();
        RefreshAnnouncement();
        RebuildProjectNavigation();
        ShowStatus(enabled ? WorkflowText("Mode serveur activé.", "Server mode on.") : WorkflowText("Mode serveur désactivé : l'application travaille en local.", "Server mode off: the application works locally."), StatusKind.Notice);
    }

    // ---- Calls waiting in the server's queue ----

    readonly HashSet<int> relayQueuedChats = [];

    /// <summary>
    /// A call of a local conversation waits in the server's queue for a shared provider: the position is the status of the conversation that made it
    /// (those running with that provider, oldest call first), and the status goes back to normal when the call starts.
    /// </summary>
    void ShowRelayQueue(IReadOnlyList<RelayWait> waits)
    {
        var link = ServerLink;
        var shown = new HashSet<int>();
        var candidates = conversationRuns.Values.Where(x => !IsCloudChat(x.Chat)).Select(x => (Run: x, Server: ServerSync.ServerProviderOf(link, x.Provider)))
            .Where(x => x.Server != null).OrderBy(x => x.Run.Chat.Id).ToList();
        foreach (var group in candidates.GroupBy(x => x.Server!.Value.ServerProvider))
        {
            var theirs = waits.Where(x => x.ProviderId == group.Key).OrderBy(x => x.SinceUtc).ToList();
            var runs = group.ToList();
            for (var i = 0; i < Math.Min(theirs.Count, runs.Count); i++)
            {
                shown.Add(runs[i].Run.Chat.Id);
                var position = theirs[i].Position;
                SetRunStatus(runs[i].Run, WorkflowText($"En attente sur le serveur (position {position})", $"Queued on the server (position {position})"));
            }
        }
        foreach (var chatId in relayQueuedChats.Except(shown).ToList())
            if (conversationRuns.TryGetValue(chatId, out var run)) SetRunStatus(run, T("Le modèle réfléchit…"));
        relayQueuedChats.Clear();
        relayQueuedChats.UnionWith(shown);
    }
}
