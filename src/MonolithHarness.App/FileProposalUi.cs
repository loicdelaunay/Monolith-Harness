using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;
public sealed partial class MainWindow
{
    void AddProposalReviewButton(StackPanel host)
    {
        if (host.Children.OfType<Button>().Any(b => Equals(b.Tag, "proposal-review"))) return;
        var button = Action(WorkflowText("Réviser les fichiers proposés", "Review proposed files"), ReviewFileProposalsAsync);
        button.Tag = "proposal-review"; button.HorizontalAlignment = HorizontalAlignment.Right; host.Children.Add(button);
    }
    async Task ReviewFileProposalsAsync()
    {
        if (chat == null || project == null) return;
        var ownerId = chat.Id; var batch = await FileProposals.ReadAsync(db.FilePath, ownerId);
        if (batch == null || batch.Files.Count == 0) { ShowStatus(WorkflowText("Aucune proposition en attente", "No pending proposal")); return; }
        if (conversationRuns.ContainsKey(ownerId)) throw new InvalidOperationException(WorkflowText("Attendez la fin de la génération avant d’appliquer les fichiers.", "Wait for generation to finish before applying files."));
        var panel = new StackPanel { Spacing = 12, Width = Math.Max(260, Math.Min(700, root.ActualWidth - 120)) };
        panel.Children.Add(Label(WorkflowText("Examinez les différences et cochez les fichiers à appliquer. Les autres resteront en attente.", "Review the differences and select files to apply. Others remain pending."), 13));
        var choices = new List<(FileProposal File, CheckBox Choice)>();
        foreach (var file in batch.Files)
        {
            var choice = new CheckBox { Content = file.Path, IsChecked = false }; choices.Add((file, choice));
            panel.Children.Add(choice);
            var diff = new TextBlock { Text = FileProposals.Diff(file), IsTextSelectionEnabled = true, TextWrapping = TextWrapping.NoWrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas"), FontSize = 12 };
            panel.Children.Add(new Expander { Header = WorkflowText("Voir les différences", "View diff"), Content = new ScrollViewer { Content = diff, MaxHeight = 260,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = WorkflowText("Revue des modifications proposées", "Review proposed changes"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 460 }, PrimaryButtonText = WorkflowText("Appliquer la sélection", "Apply selection"),
            CloseButtonText = T("Annuler"), DefaultButton = ContentDialogButton.Close };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        var selected = choices.Where(c => c.Choice.IsChecked == true).Select(c => c.File.Path).ToList(); if (selected.Count == 0) return;
        if (chat?.Id != ownerId || conversationRuns.ContainsKey(ownerId)) throw new InvalidOperationException("La conversation a changé / Conversation changed.");
        await FileProposals.ApplyAsync(db.FilePath, ownerId, batch.Revision, selected, new SourceAccess(project.GetSourceFolders()), CancellationToken.None);
        ShowStatus(WorkflowText($"{selected.Count} fichiers appliqués", $"{selected.Count} files applied")); await RefreshGitAsync(CancellationToken.None);
    }
}
