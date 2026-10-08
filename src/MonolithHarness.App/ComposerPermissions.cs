using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly DropDownButton composerPermissionsButton = new()
    {
        Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(5, 0, 4, 0),
        BorderThickness = new(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
    };
    readonly TextBlock composerPermissionLabel = new()
    {
        FontSize = 11, MaxWidth = 95, TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center
    };
    readonly FontIcon composerPermissionShield = FluentDesign.Icon("\uE72E", 13);
    bool changingComposerPermission;

    static int PermissionModeIndex(string? mode) => PermissionModes.Normalize(mode) switch
    { PermissionModes.Deny => 0, PermissionModes.Allow => 2, PermissionModes.Full => 3, _ => 1 };

    string PermissionCaption(string mode) => mode switch
    {
        PermissionModes.Deny => WorkflowText("Refuser tout", "Deny all"),
        PermissionModes.Allow => WorkflowText("Automatique", "Automatic"),
        PermissionModes.Full => WorkflowText("Tout autoriser", "Allow all"),
        _ => WorkflowText("Demander", "Ask")
    };
    string PermissionDescription(string mode) => mode switch
    {
        PermissionModes.Deny => WorkflowText("Refuse les actions qui nécessitent une autorisation.", "Denies actions that require permission."),
        PermissionModes.Allow => WorkflowText("Analyse les commandes avec le validateur choisi dans Paramètres / Autorisations. Une analyse incertaine demande votre accord. Les règles du projet restent appliquées.", "Analyzes commands with the validator chosen in Settings / Permissions. Uncertain analysis requires your approval. Project rules still apply."),
        PermissionModes.Full => WorkflowText("Accepte les demandes sans validation par un modèle. Les règles explicites du projet restent appliquées.", "Accepts requests without model validation. Explicit project rules still apply."),
        _ => WorkflowText("Demande une approbation pour les accès sans autorisation mémorisée. Les règles du projet restent appliquées.", "Asks for approval for access without a saved grant. Project rules still apply.")
    };
    FrameworkElement BuildComposerPermissions()
    {
        var content = Row(composerPermissionShield, composerPermissionLabel); content.Spacing = 4;
        content.VerticalAlignment = VerticalAlignment.Center; composerPermissionShield.VerticalAlignment = VerticalAlignment.Center;
        composerPermissionsButton.Content = content;
        var flyout = new Flyout { Placement = FlyoutPlacementMode.TopEdgeAlignedLeft };
        flyout.Opening += (_, _) => BuildComposerPermissionMenu(flyout);
        composerPermissionsButton.Flyout = flyout;
        RefreshComposerPermissions();
        return composerPermissionsButton;
    }
    void RefreshComposerPermissions()
    {
        var mode = PermissionModes.Normalize(state.PermissionMode);
        composerPermissionLabel.Text = PermissionCaption(mode);
        composerPermissionShield.Glyph = mode == PermissionModes.Full ? "\uE7BA" : "\uE72E";
        var brush = mode == PermissionModes.Full ? FluentDesign.Adapt(230, 157, 57)
            : mode == PermissionModes.Deny ? FluentDesign.Secondary : FluentDesign.Resource("AccentTextFillColorPrimaryBrush");
        composerPermissionShield.Foreground = brush; composerPermissionLabel.Foreground = brush;
        var description = WorkflowText("Autorisations · ", "Permissions · ") + PermissionCaption(mode) + "\n" + PermissionDescription(mode)
            + "\n" + WorkflowText("Réglage global partagé avec Paramètres / Autorisations.", "Global setting shared with Settings / Permissions.")
            + (chat == null ? "" : "\n" + (chat.AllowOutsideResources
                ? WorkflowText("Cette conversation : accès hors pièces jointes autorisé selon le mode choisi.", "This conversation: outside access allowed according to the selected permission mode.")
                : WorkflowText("Cette conversation : accès limité aux pièces jointes.", "This conversation: access restricted to attachments.")));

        ToolTipService.SetToolTip(composerPermissionsButton, description);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(composerPermissionsButton, description);
        composerPermissionsButton.IsEnabled = conversationReady && !conversationLoading && !changingComposerPermission
            && !databaseMaintenanceBusy && !conversationRetentionBusy;
        resizeComposerFooter?.Invoke();
    }
    void BuildComposerPermissionMenu(Flyout flyout)
    {
        var panel = new StackPanel { Spacing = 4, Width = 340, MaxWidth = Math.Max(220, Math.Min(340, root.ActualWidth - 48)) };
        panel.Children.Add(Label(WorkflowText("Autorisations", "Permissions"), 16));
        var current = PermissionModes.Normalize(state.PermissionMode);
        foreach (var mode in new[] { PermissionModes.Ask, PermissionModes.Allow, PermissionModes.Full, PermissionModes.Deny })
        {
            var label = mode == PermissionModes.Ask ? WorkflowText("Demander l’approbation (par défaut)", "Ask for approval (default)") : PermissionCaption(mode);
            var title = Label(label, 13); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            var description = Label(PermissionDescription(mode), 12); description.Foreground = FluentDesign.Secondary;
            var text = new StackPanel { Spacing = 2 }; text.Children.Add(title); text.Children.Add(description);
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var icon = FluentDesign.Icon(mode == PermissionModes.Full ? "\uE7BA" : "\uE72E", 15); icon.VerticalAlignment = VerticalAlignment.Center;
            if (mode == PermissionModes.Full) { icon.Foreground = FluentDesign.Adapt(230, 157, 57); title.Foreground = icon.Foreground; }
            row.Children.Add(icon); Grid.SetColumn(text, 1); row.Children.Add(text);
            var check = FluentDesign.Icon("\uE73E", 13); check.VerticalAlignment = VerticalAlignment.Center;
            check.Visibility = current == mode ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(check, 2); row.Children.Add(check);
            var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(8), BorderThickness = new(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), IsEnabled = !editingSettings && !changingComposerPermission };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label + " · " + description.Text);
            button.Click += async (_, _) => { flyout.Hide(); await Guard(() => ChangeComposerPermissionAsync(mode)); };
            panel.Children.Add(button);
        }
        var note = Label(editingSettings
            ? WorkflowText("Réglages ouverts : choisissez le mode dans l’onglet Autorisations.", "Settings are open: choose the mode in the Permissions tab.")
            : WorkflowText("Ce choix s’applique à toutes les conversations et aux prochaines demandes d’accès. Les autorisations mémorisées se gèrent dans les réglages.", "Applies to all conversations and future access requests. Manage saved grants in settings."), 12);
        note.Foreground = FluentDesign.Secondary; note.Margin = new(8, 6, 8, 4); panel.Children.Add(note);
        panel.Children.Add(new Border { Height = 1, Background = FluentDesign.Secondary, Opacity = .25, Margin = new(8, 8, 8, 4) });
        var outside = new ToggleSwitch
        {
            Header = WorkflowText("Travailler hors des pièces jointes", "Work outside attachments"),
            IsOn = chat?.AllowOutsideResources ?? true,
            OnContent = WorkflowText("Autorisé", "Allowed"), OffContent = WorkflowText("Limité aux pièces jointes", "Restricted to attachments"),
            IsEnabled = chat != null && ActiveRun == null && !changingComposerPermission && !conversationLoading,
            Margin = new(8, 4, 8, 0)
        };
        var ownerId = chat?.Id;
        ToolTipService.SetToolTip(outside, WorkflowText("Réglage enregistré pour cette conversation, indépendant du mode d’approbation.", "Saved for this conversation, independently of the approval mode."));
        bool restoringBoundary = false;
        outside.Toggled += async (_, _) =>
        {
            if (restoringBoundary || ownerId == null || chat?.Id != ownerId) return;
            var wanted = outside.IsOn;
            outside.IsEnabled = false;
            await Guard(() => ChangeComposerResourceBoundaryAsync(ownerId.Value, wanted));
            restoringBoundary = true;
            try { outside.IsOn = chat?.AllowOutsideResources ?? true; }
            finally { restoringBoundary = false; }
            outside.IsEnabled = chat?.Id == ownerId && ActiveRun == null && !conversationLoading && !changingComposerPermission;
        };
        panel.Children.Add(outside);
        var boundaryNote = Label(WorkflowText(
            "Ce choix concerne cette conversation et ses sous-agents. Désactivé : seuls les fichiers et dossiers joints, ainsi que l’espace de travail de la conversation, sont accessibles. Le terminal local, Python, GIT, le navigateur, le bureau, MCP et les agents OpenCode / ACP sont bloqués. La sandbox permet les commandes isolées. Les outils manuels restent disponibles.",
            "Applies to this conversation and its subagents. When off, only attached files/folders and the conversation workspace are accessible. Local terminal, Python, GIT, browser, desktop, MCP and OpenCode / ACP agents are blocked. Sandbox mode allows isolated commands. Manual tools remain available."), 12);
        boundaryNote.Foreground = FluentDesign.Secondary; boundaryNote.Margin = new(8, 2, 8, 8); panel.Children.Add(boundaryNote);
        if (ActiveRun != null)
        {
            var busyNote = Label(WorkflowText("Arrêtez la réponse pour changer ce périmètre.", "Stop the response to change this boundary."), 12);
            busyNote.Foreground = FluentDesign.Secondary; busyNote.Margin = new(8, 0, 8, 4); panel.Children.Add(busyNote);
        }
        var details = Action(WorkflowText("Paramètres / Autorisations", "Settings / Permissions"), async () =>
        { flyout.Hide(); await Settings(showProviders: false, showPermissions: true); });
        details.HorizontalAlignment = HorizontalAlignment.Stretch; panel.Children.Add(details);
        flyout.Content = new ScrollViewer { Content = panel,
            MaxHeight = Math.Max(220, Math.Min(620, root.ActualHeight - 80)),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    async Task ChangeComposerResourceBoundaryAsync(int ownerId, bool value)
    {
        if (chat?.Id != ownerId || ActiveRun != null || changingComposerPermission || conversationLoading
            || databaseMaintenanceBusy || conversationRetentionBusy) return;
        var owner = chat;
        if (owner.AllowOutsideResources == value) return;
        changingComposerPermission = true; RefreshComposerPermissions(); RefreshGenerationControls();
        try
        {
            await using var store = new HarnessDb();
            var changed = await store.Chats.Where(item => item.Id == ownerId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.AllowOutsideResources, value));
            if (changed != 1) throw new InvalidOperationException(WorkflowText("Conversation introuvable.", "Conversation not found."));
            owner.AllowOutsideResources = value;
            db.Entry(owner).Property(item => item.AllowOutsideResources).OriginalValue = value;
            ShowStatus(value
                ? WorkflowText("Accès hors pièces jointes autorisé pour cette conversation.", "Outside access enabled for this conversation.")
                : WorkflowText("Cette conversation est limitée à ses pièces jointes.", "This conversation is restricted to its attachments."), StatusKind.Notice);
        }
        finally { changingComposerPermission = false; RefreshComposerPermissions(); RefreshGenerationControls(); }
    }
    async Task ChangeComposerPermissionAsync(string value)
    {
        if (changingComposerPermission || editingSettings || databaseMaintenanceBusy || conversationRetentionBusy) return;
        var mode = PermissionModes.Normalize(value);
        if (mode == PermissionModes.Normalize(state.PermissionMode)) return;
        changingComposerPermission = true; RefreshComposerPermissions(); RefreshGenerationControls();
        try
        {
            // Use a separate context while conversations may be writing messages.
            await using var store = new HarnessDb();
            var changed = await store.States.Where(item => item.Id == state.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.PermissionMode, mode));
            if (changed != 1) throw new InvalidOperationException(WorkflowText("Réglage d’autorisation introuvable.", "Permission setting not found."));
            state.PermissionMode = mode;
            db.Entry(state).Property(item => item.PermissionMode).OriginalValue = mode;
            ShowStatus(WorkflowText("Autorisations : ", "Permissions: ") + PermissionCaption(mode), StatusKind.Notice);
        }
        finally { changingComposerPermission = false; RefreshComposerPermissions(); RefreshGenerationControls(); }
    }
}
