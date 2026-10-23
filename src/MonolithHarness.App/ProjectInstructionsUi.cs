using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    async Task EditProjectInstructionsAsync(Project owner)
    {
        var instructions = new TextBox { Text = owner.Instructions, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 200, MaxHeight = 450, MaxLength = 32000, PlaceholderText = WorkflowText("Consignes pour les conversations de ce projet…", "Instructions for this project's conversations…") };
        ScrollViewer.SetVerticalScrollBarVisibility(instructions, ScrollBarVisibility.Auto);
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Label(WorkflowText("Appliquées aux prochains envois de ce projet, en complément des fichiers AGENTS.md. Les autorisations restent prioritaires.", "Applied to future messages in this project, alongside AGENTS.md files. Permissions still apply."), 13));
        panel.Children.Add(instructions);
        var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = WorkflowText("Instructions · ", "Instructions · ") + owner.Name,
            Content = panel, PrimaryButtonText = UiText.T("Enregistrer"), CloseButtonText = UiText.T("Annuler"), DefaultButton = ContentDialogButton.Primary };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        var value = instructions.Text.Trim();
        await using var store = new HarnessDb(db.FilePath);
        var stored = await store.Projects.SingleAsync(item => item.Id == owner.Id);
        stored.Instructions = value; await store.SaveChangesAsync();
        owner.Instructions = value;
        if (project?.Id == owner.Id) project.Instructions = value;
    }
}
