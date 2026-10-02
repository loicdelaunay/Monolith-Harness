using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeConversationFileWorkspace(string output)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed.Add(name); }
        async Task Wait(Func<bool> ready)
        {
            for (var i = 0; i < 200; i++) { if (ready()) return; await Task.Delay(25); }
            throw new TimeoutException("Conversation workspace did not become ready.");
        }
        UiText.Language = state.Language = "fr"; ApplyTheme("fluent-dark");
        var settings = FeatureSettings.Read(state.FeaturesJson); settings.GuiUpdateMode = AutomaticUpdateMode.Disabled;
        state.FeaturesJson = settings.Json(); ConfigureAutomaticUpdates();
        var fixture = new Project { Name = "Folderless conversations", IsInbox = true,
            Chats = [new Chat { Title = "Files A" }, new Chat { Title = "Files B" }] };
        db.Projects.Add(fixture); await db.SaveChangesAsync();
        projects.ItemsSource = new[] { fixture }; projects.SelectedItem = fixture; await SelectProject();
        var first = fixture.Chats.OrderBy(x => x.Id).First(); var second = fixture.Chats.OrderBy(x => x.Id).Last();
        chats.SelectedItem = first; await SelectChat(); await Wait(() => conversationReady && chat?.Id == first.Id);
        var firstRoot = ConversationWorkspace.DirectoryPath(first.Id);
        Check(project!.GetSourceFolders().SequenceEqual([firstRoot]) && Directory.Exists(firstRoot), "Selecting a folderless conversation creates its private file workspace.");
        Check(sourceLabel.Text.Contains("Espace de travail de la conversation", StringComparison.Ordinal)
            && sourceLabel.Text.Contains(firstRoot, StringComparison.Ordinal), "The source label identifies the conversation workspace and its path.");
        var automaticChip = assetsBar.Children.OfType<Border>().Single();
        Check(automaticChip.Child is StackPanel chip && !chip.Children.OfType<Button>().Any(), "The automatic workspace chip cannot be detached as a project source.");
        Check(RequireDirectory() == firstRoot, "Manual terminals use the current conversation workspace.");
        await new SourceAccess(project.GetSourceFolders()).WriteAsync("result.txt", "A conversation file", default);
        await LoadFilesAsync(firstRoot);
        Check(fileDirectory == firstRoot, "The Files tab can open the automatic workspace.");
        await Capture(root, Path.Combine(output, "conversation-workspace.png"));

        chats.SelectedItem = second; await SelectChat(); await Wait(() => conversationReady && chat?.Id == second.Id);
        var secondRoot = ConversationWorkspace.DirectoryPath(second.Id);
        Check(project!.GetSourceFolders().SequenceEqual([secondRoot]) && RequireDirectory() == secondRoot,
            "Switching conversations switches both source tools and terminal directories.");
        Check(firstRoot != secondRoot && !File.Exists(Path.Combine(secondRoot, "result.txt")), "A second conversation cannot inherit the first conversation’s files.");
        chats.SelectedItem = first; await SelectChat(); await Wait(() => conversationReady && chat?.Id == first.Id);
        Check(await new SourceAccess(project!.GetSourceFolders()).ReadAsync("result.txt", default) == "A conversation file",
            "Returning to a conversation restores its existing files.");
        var explicitFolder = Path.Combine(output, "chosen-project"); Directory.CreateDirectory(explicitFolder);
        await SaveConversationResourcesAsync(CurrentResourcePaths().Append(explicitFolder));
        Check(project!.GetSourceFolders().SequenceEqual([explicitFolder]) && RequireDirectory() == explicitFolder,
            "Attaching a real folder restores normal project-root behavior.");
        await SaveConversationResourcesAsync([]);
        Check(project!.GetSourceFolders().SequenceEqual([firstRoot]) && File.Exists(Path.Combine(firstRoot, "result.txt")),
            "Removing the last attached folder returns to the same conversation workspace.");
        Check(fixture.SourceFolder == "", "Conversation file workspaces never mutate the shared inbox project defaults.");
        UiText.Language = state.Language = "en"; UpdateSourceLabel(); UpdateFloatingAssets(); ApplyTheme("fluent-light");
        Check(sourceLabel.Text.StartsWith("Conversation workspace:", StringComparison.Ordinal), "The workspace label follows the UI language.");
        await Wait(() => assetsScroll.Visibility == Visibility.Visible && assetsBar.Children.OfType<Border>().Any(x => x.ActualWidth > 0));
        await Capture(root, Path.Combine(output, "conversation-workspace-light.png"));
        await File.WriteAllLinesAsync(Path.Combine(output, "smoke-ok.txt"), passed);
    }
}
