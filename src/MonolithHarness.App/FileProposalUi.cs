using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;
public sealed partial class MainWindow
{
    readonly Dictionary<int, ProposalReviewWindow> proposalReviewWindows = [];
    readonly Dictionary<int, TaskCompletionSource<bool>> proposalCommits = [];
    readonly HashSet<int> proposalReviewLoading = [];

    void AddProposalReviewButton(StackPanel host, int ownerId, ProposalBatch batch, IReadOnlyList<ProposalDiff> diffs)
    {
        var button = host.Children.OfType<Button>().FirstOrDefault(item => Equals(item.Tag, "proposal-review"));
        if (button == null)
        {
            button = Action("", () => ReviewFileProposalsAsync(ownerId));
            button.Tag = "proposal-review";
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Padding = new(16, 12, 16, 12); button.MinHeight = 56; button.CornerRadius = new(10);
            button.Background = FluentDesign.Card; button.BorderBrush = FluentDesign.Resource("ControlStrokeColorDefaultBrush"); button.BorderThickness = new(1);
            button.Margin = new(0, 8, 0, 8); host.Children.Add(button);
        }
        var content = new Grid { ColumnSpacing = 16 };
        content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var summary = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        summary.Children.Add(FluentDesign.Icon("\uE8A5", 18));
        var count = new TextBlock { Text = batch.Files.Count == 1 ? WorkflowText("1 fichier proposé", "1 proposed file") : WorkflowText($"{batch.Files.Count} fichiers proposés", $"{batch.Files.Count} proposed files"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = FluentDesign.Primary, VerticalAlignment = VerticalAlignment.Center };
        summary.Children.Add(count);
        summary.Children.Add(new TextBlock { Text = "+" + diffs.Sum(diff => diff.Added), Foreground = FluentDesign.Resource("DiffAddedTextBrush"), VerticalAlignment = VerticalAlignment.Center });
        summary.Children.Add(new TextBlock { Text = "−" + diffs.Sum(diff => diff.Removed), Foreground = FluentDesign.Resource("DiffRemovedTextBrush"), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(summary);
        var action = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var reviewLabel = new TextBlock { Text = WorkflowText("Réviser", "Review"), Foreground = FluentDesign.Resource("AccentTextFillColorPrimaryBrush") };
        action.Children.Add(reviewLabel);
        action.Children.Add(FluentDesign.Icon("\uE76C", 12)); Grid.SetColumn(action, 1); content.Children.Add(action);
        content.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 420;
            reviewLabel.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            count.Text = narrow ? WorkflowText($"{batch.Files.Count} fichiers", $"{batch.Files.Count} files")
                : batch.Files.Count == 1 ? WorkflowText("1 fichier proposé", "1 proposed file") : WorkflowText($"{batch.Files.Count} fichiers proposés", $"{batch.Files.Count} proposed files");
        };
        button.Content = content;
        var label = WorkflowText($"Réviser {batch.Files.Count} fichiers proposés, {diffs.Sum(diff => diff.Added)} lignes ajoutées et {diffs.Sum(diff => diff.Removed)} lignes supprimées", $"Review {batch.Files.Count} proposed files, {diffs.Sum(diff => diff.Added)} added lines and {diffs.Sum(diff => diff.Removed)} removed lines");
        AutomationProperties.SetName(button, label); ToolTipService.SetToolTip(button, label);
    }
    Task ReviewFileProposalsAsync() => chat == null ? Task.CompletedTask : ReviewFileProposalsAsync(chat.Id);
    async Task ReviewFileProposalsAsync(int ownerId)
    {
        if (proposalReviewWindows.TryGetValue(ownerId, out var existing)) { existing.Activate(); return; }
        if (!proposalReviewLoading.Add(ownerId)) return;
        try
        {
            var database = db.FilePath;
            var batch = await FileProposals.ReadAsync(database, ownerId);
            if (batch == null || batch.Files.Count == 0) { ShowStatus(WorkflowText("Aucune proposition en attente", "No pending proposal")); return; }
            if (conversationRuns.ContainsKey(ownerId)) throw new InvalidOperationException(WorkflowText("Attendez la fin de la génération avant de revoir les fichiers.", "Wait for generation to finish before reviewing files."));
            var diffs = await Task.Run(() => batch.Files.Select(ProposalDiff.Create).ToList());
            await using var reviewDb = new HarnessDb(database);
            var owner = await reviewDb.Chats.AsNoTracking().SingleAsync(item => item.Id == ownerId);
            var window = new ProposalReviewWindow(owner.Title, batch, diffs, root.RequestedTheme, WorkflowText,
                decisions => FileProposals.SaveReviewAsync(database, ownerId, batch.Revision, decisions),
                async decisions =>
                {
                    if (conversationRuns.ContainsKey(ownerId)) throw new InvalidOperationException(WorkflowText("Attendez la fin de la génération avant d’appliquer les fichiers.", "Wait for generation to finish before applying files."));
                    var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    proposalCommits.Add(ownerId, completed);
                    try
                    {
                        await using var store = new HarnessDb(database);
                        var current = await store.Chats.AsNoTracking().SingleAsync(item => item.Id == ownerId);
                        var storedProject = await store.Projects.AsNoTracking().SingleAsync(item => item.Id == current.ProjectId);
                        var source = ProjectResources.Effective(current, storedProject);
                        await FileProposals.CompleteReviewAsync(database, ownerId, batch.Revision, decisions, new SourceAccess(source.GetSourceFolders()), CancellationToken.None);
                    }
                    finally { proposalCommits.Remove(ownerId); completed.TrySetResult(true); }
                    var accepted = decisions.Count(decision => decision.Accepted);
                    ShowStatus(WorkflowText($"Revue terminée · {accepted} blocs appliqués", $"Review finished · {accepted} blocks applied"));
                    if (chat?.Id == ownerId)
                    {
                        foreach (var button in messages.Children.OfType<Button>().Where(item => Equals(item.Tag, "proposal-review")).ToArray()) messages.Children.Remove(button);
                        try { await RefreshGitAsync(CancellationToken.None); }
                        catch (Exception ex) { AppLog.Write(AppLogLevel.Warning, "proposals.git_refresh_failed", ex); }
                    }
                });
            proposalReviewWindows.Add(ownerId, window);
            window.Closed += (_, _) => proposalReviewWindows.Remove(ownerId);
            ApplyBrandingIcon(window); ObserveTextZoom(window.ReviewRoot); window.Activate();
        }
        finally { proposalReviewLoading.Remove(ownerId); }
    }
}
