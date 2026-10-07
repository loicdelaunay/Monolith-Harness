using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    sealed class VerificationActivity(int chatId)
    {
        public int ChatId { get; } = chatId;
        public string Stage { get; set; } = "queued";
        public System.Diagnostics.Stopwatch Clock { get; } = System.Diagnostics.Stopwatch.StartNew();
    }
    readonly Dictionary<Guid, VerificationActivity> commandVerifications = [];
    readonly Border commandVerificationChip = new() { Visibility = Visibility.Collapsed, CornerRadius = new(16), Padding = new(10, 6, 10, 6), VerticalAlignment = VerticalAlignment.Center };
    readonly BusySpinner commandVerificationSpinner = new();
    readonly TextBlock commandVerificationLabel = Label("", 12);
    FrameworkElement BuildCommandVerificationRow(FrameworkElement statusView)
    {
        var shield = FluentDesign.Icon("\uE72E", 14); shield.VerticalAlignment = VerticalAlignment.Center;
        shield.Foreground = FluentDesign.Resource("AccentTextFillColorPrimaryBrush");
        commandVerificationLabel.VerticalAlignment = VerticalAlignment.Center;
        var content = Row(shield, commandVerificationLabel, commandVerificationSpinner); content.Spacing = 6;
        commandVerificationChip.Child = content; commandVerificationChip.Background = FluentDesign.Card;
        commandVerificationChip.BorderBrush = FluentDesign.Stroke; commandVerificationChip.BorderThickness = new(1);
        var row = Row(statusView, commandVerificationChip); row.Spacing = 4; row.HorizontalAlignment = HorizontalAlignment.Center;
        void FitStatus()
        {
            var width = scroll.ActualWidth > 0 ? scroll.ActualWidth : root.ActualWidth;
            if (statusChipView != null) statusChipView.MaxWidth = Math.Max(100, Math.Min(760, width - (commandVerificationChip.Visibility == Visibility.Visible ? 210 : 40)));
        }
        row.SizeChanged += (_, _) => FitStatus(); scroll.SizeChanged += (_, _) => FitStatus();
        RefreshCommandVerification(); return row;
    }
    void RefreshCommandVerification()
    {
        var visible = selectedSubagent == null && chat != null ? commandVerifications.Values.Where(x => x.ChatId == chat.Id).ToList() : [];
        commandVerificationChip.Visibility = visible.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        commandVerificationSpinner.IsActive = visible.Count > 0;
        commandVerificationLabel.Text = WorkflowText("Vérification…", "Checking…") + (visible.Count > 1 ? $" ({visible.Count})" : "");
        if (visible.Count == 0) return;
        var activity = visible.First();
        var stage = activity.Stage switch {
            "preparing" => WorkflowText("Préparation du validateur", "Preparing validator"),
            "analyzing" => WorkflowText("Analyse de la commande", "Analyzing command"),
            "checking" => WorkflowText("Contrôle de la décision", "Checking decision"),
            _ => WorkflowText("Attente du validateur", "Waiting for validator") };
        var details = stage + $" · {activity.Clock.Elapsed.TotalSeconds:F0} s" + "\n" + WorkflowText("Le validateur ne fournit pas de pourcentage de progression.", "The validator does not report a completion percentage.");
        ToolTipService.SetToolTip(commandVerificationChip, details);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(commandVerificationChip, commandVerificationLabel.Text + " · " + details);
    }
    async Task<CommandGuardResult> CheckCommandWithFeedbackAsync(CommandApproval? command, CancellationToken ct)
    {
        var id = Guid.NewGuid(); var ownerId = automaticToolRun.Value?.Chat.Id ?? chat?.Id ?? 0;
        var activity = new VerificationActivity(ownerId);
        DispatcherQueue.TryEnqueue(() => { commandVerifications[id] = activity; RefreshCommandVerification(); });
        var progress = new Progress<CommandValidationProgress>(value => DispatcherQueue.TryEnqueue(() =>
        { if (commandVerifications.TryGetValue(id, out var current)) { current.Stage = value.Stage; RefreshCommandVerification(); } }));
        try
        {
            return await CommandGuard.CheckAsync(command, FeatureSettings.Read(state.FeaturesJson),
                (providerId, _) => ReadStoreAsync(store => store.Providers.AsNoTracking().SingleOrDefault(p => p.Id == providerId)),
                (selected, _) => Task.FromResult(KeyVault.Decrypt(selected.ProtectedKey)), http, ct, progress);
        }
        finally { DispatcherQueue.TryEnqueue(() => { commandVerifications.Remove(id); RefreshCommandVerification(); }); }
    }
}
