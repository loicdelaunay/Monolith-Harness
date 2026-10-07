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
    { PermissionModes.Deny => 0, PermissionModes.Allow => 2, _ => 1 };

    string PermissionCaption(string mode) => mode switch
    {
        PermissionModes.Deny => WorkflowText("Refuser tout", "Deny all"),
        PermissionModes.Allow => WorkflowText("Automatique", "Automatic"),
        _ => WorkflowText("Demander", "Ask")
    };
    string PermissionDescription(string mode) => mode switch
    {
        PermissionModes.Deny => WorkflowText("Refuse les actions qui nécessitent une autorisation.", "Denies actions that require permission."),
        PermissionModes.Allow => WorkflowText("Analyse les commandes avec LANCET localement. Les commandes signalées demandent votre accord ; les autres autorisations sont automatiques. Les règles du projet restent appliquées.", "Analyzes commands locally with LANCET. Flagged commands require your approval; other permissions are automatic. Project rules still apply."),
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
        var brush = mode == PermissionModes.Allow ? FluentDesign.Adapt(230, 157, 57)
            : mode == PermissionModes.Deny ? FluentDesign.Secondary : FluentDesign.Resource("AccentTextFillColorPrimaryBrush");
        composerPermissionShield.Foreground = brush; composerPermissionLabel.Foreground = brush;
        var description = WorkflowText("Autorisations · ", "Permissions · ") + PermissionCaption(mode) + "\n" + PermissionDescription(mode)
            + "\n" + WorkflowText("Réglage global partagé avec Paramètres / Autorisations.", "Global setting shared with Settings / Permissions.");
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
        foreach (var mode in new[] { PermissionModes.Ask, PermissionModes.Allow, PermissionModes.Deny })
        {
            var label = mode == PermissionModes.Ask ? WorkflowText("Demander l’approbation (par défaut)", "Ask for approval (default)") : PermissionCaption(mode);
            var title = Label(label, 13); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            var description = Label(PermissionDescription(mode), 12); description.Foreground = FluentDesign.Secondary;
            var text = new StackPanel { Spacing = 2 }; text.Children.Add(title); text.Children.Add(description);
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var icon = FluentDesign.Icon("\uE72E", 15); icon.VerticalAlignment = VerticalAlignment.Center;
            if (mode == PermissionModes.Allow) { icon.Foreground = FluentDesign.Adapt(230, 157, 57); title.Foreground = icon.Foreground; }
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
        var details = Action(WorkflowText("Paramètres / Autorisations", "Settings / Permissions"), async () =>
        { flyout.Hide(); await Settings(showProviders: false, showPermissions: true); });
        details.HorizontalAlignment = HorizontalAlignment.Stretch; panel.Children.Add(details);
        flyout.Content = panel;
    }
    async Task ChangeComposerPermissionAsync(string value)
    {
        if (changingComposerPermission || editingSettings || databaseMaintenanceBusy || conversationRetentionBusy) return;
        var mode = PermissionModes.Normalize(value);
        if (mode == PermissionModes.Allow && !await EnsureCommandGuardInstalledAsync()) return;
        if (mode == PermissionModes.Normalize(state.PermissionMode)) return;
        changingComposerPermission = true; RefreshComposerPermissions();
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
        finally { changingComposerPermission = false; RefreshComposerPermissions(); }
    }
}
