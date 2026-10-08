using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeComposerPresentation(string output)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed.Add(name); }
        static void Click(Button button) => (new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider
            ?? throw new Exception("Invoke unavailable")).Invoke();
        static async Task Wait(Func<bool> ready)
        {
            for (int i = 0; i < 100 && !ready(); i++) await Task.Delay(50);
            if (!ready()) throw new Exception("UI operation did not complete.");
        }
        shell.IsPaneOpen = true;
        Check(chat != null && project != null, "An isolated conversation is available.");
        var fixture = new Provider { Name = "Offline fixture", Kind = "openai", Model = "fast", SelectedModelsJson = "[\"fast\",\"deep\"]" };
        db.Providers.Add(fixture); await db.SaveChangesAsync(); provider = fixture; state.ProviderId = fixture.Id;
        loading = true; providers.ItemsSource = db.Providers.Local.ToList(); providers.SelectedItem = fixture; loading = false;
        chat!.InteractionMode = "agent"; state.ThinkingLevel = "low"; state.ChatThinkingLevel = "none";
        var settings = FeatureSettings.Read(state.FeaturesJson);
        settings.QuickModelLevelsEnabled = true; settings.QuickModelSelectionMode = "simple";
        var fast = new QuickModelLevel { ProviderId = fixture.Id, Model = "fast", Name = "Rapide", ThinkingLevel = "low", InteractionMode = "agent" };
        var deep = new QuickModelLevel { ProviderId = fixture.Id, Model = "deep", Name = "Approfondi", ThinkingLevel = "high", InteractionMode = "chat" };
        settings.QuickModelLevels = [fast, deep]; state.FeaturesJson = settings.Json();
        await db.SaveChangesAsync(); PopulateModelSelector(); PopulateThinkingSelector(); RefreshConversationMode();
        RefreshComposerModelSummary();
        OpenModelOptions(); await Wait(() => modelOptionsIsOpen);
        Check(composerLevelSlider.Visibility == Visibility.Visible && modelOptionsIsOpen, "Quick levels appear in the model popup.");
        composerLevelSlider.Value = 2;
        await Wait(() => !applyingQuickLevel && fixture.Model == "deep");
        var saved = await db.States.AsNoTracking().SingleAsync();
        Check(chat.InteractionMode == "chat" && state.ChatThinkingLevel == "high" && saved.ChatThinkingLevel == "high" && state.ThinkingLevel == "low", "Changing a composer level saves model, mode and thinking without changing the other mode preference.");
        composerLevelSlider.Value = 1;
        await Wait(() => !applyingQuickLevel && fixture.Model == "fast");
        modelOptionsFlyout?.Hide();
        ClearMessagePanel(messages);
        var small = AddMessage("user", "Salut ça va ?"); var smallActions = EnsureMessageActionMenu(small);
        var large = AddMessage("user", string.Join(" ", Enumerable.Repeat("Voici un message plus long qui doit rester lisible et revenir à la ligne.", 12)));
        AddAssistantMessage("Les réponses restent dans la colonne de lecture.");
        await Task.Delay(180); root.UpdateLayout();
        Check(small.HorizontalAlignment == HorizontalAlignment.Right && small.ActualWidth > 0 && small.ActualWidth < large.ActualWidth * .7, "Short user bubbles size to their contents and align right.");
        var width = small.ActualWidth; var height = small.ActualHeight;
        smallActions.Button.Opacity = 1; smallActions.Button.IsHitTestVisible = true; root.UpdateLayout();
        Check(Math.Abs(width - small.ActualWidth) < .5 && Math.Abs(height - small.ActualHeight) < .5, "Showing message actions preserves bubble width and height.");
        Check(large.ActualWidth <= large.MaxWidth + 1 && large.ActualHeight > small.ActualHeight, "Long user messages wrap within their responsive limit.");
        smallActions.Button.Opacity = 0; smallActions.Button.IsHitTestVisible = false;
        var resources = Path.Combine(output, "resources"); Directory.CreateDirectory(resources);
        var text = Path.Combine(resources, "notes.txt"); await File.WriteAllTextAsync(text, "preview fixture");
        byte[] png;
        using (var bitmap = new SkiaSharp.SKBitmap(32, 32))
        { bitmap.Erase(SkiaSharp.SKColors.CornflowerBlue); using var image = SkiaSharp.SKImage.FromBitmap(bitmap); using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100); png = data.ToArray(); }
        var picture = Path.Combine(resources, "picture.png"); await File.WriteAllBytesAsync(picture, png);
        await SaveConversationResourcesAsync([resources, text, picture]);
        pendingImages.Add(new() { Name = "capture.png", Mime = "image/png", Data = png }); UpdateAttachments();
        await Task.Delay(200); root.UpdateLayout();
        var tile = assetsBar.Children.OfType<ComposerResourceTile>().Single(x => Equals(x.Tag, "capture.png"));
        Check(assetsScroll.Visibility == Visibility.Visible && ContainsVisual(root, assetsScroll) && Math.Abs(tile.ActualWidth - tile.ActualHeight) < .5, "Square previews stay visible when information is collapsed.");
        await Wait(() => tile.Thumbnail.Source != null);
        tile.SetRemoveVisible(true); root.UpdateLayout();
        Check(Math.Abs(tile.ActualWidth - tile.ActualHeight) < .5 && tile.RemoveButton.Opacity == 1, "Hover removal does not resize the preview.");
        await Capture(root, Path.Combine(output, "composer-presentation.png"));
        Click(tile.PreviewButton); await Wait(() => imagePreviewWindows.Count == 1);
        var viewer = imagePreviewWindows.Single();
        Check(viewer.AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen, "Clicking an image opens a fullscreen viewer.");
        await Task.Delay(120); await Capture(viewer.Content as FrameworkElement ?? throw new Exception("Image viewer content unavailable."), Path.Combine(output, "image-fullscreen.png")); viewer.Close();
        Click(tile.RemoveButton); await Wait(() => pendingImages.Count == 0);
        Check(File.Exists(picture), "Removing an attachment preserves the source image.");
        composer.Text = ""; conversationDrafts.Clear();
        var update = new GitHubUpdate("9.0.0", "v9.0.0", GitHubUpdates.Repository + "/releases/tag/v9.0.0", "fixture.zip", GitHubUpdates.Repository + "/fixture.zip", new string('a', 64), 100);
        guiUpdate = update; var gate = new TaskCompletionSource<string>(); var installed = new TaskCompletionSource<bool>();
        var executable = Path.Combine(output, "update-fixture.exe"); await File.WriteAllTextAsync(executable, "offline update");
        smokeGuiUpdateDownload = async (_, progress) => { progress.Report(37); return await gate.Task; };
        smokeGuiUpdateInstall = _ => installed.TrySetResult(true);
        RefreshGuiUpdateButton(); var statusBefore = status.Text;
        Click(guiUpdateButton); await Wait(() => updatingApplication && guiUpdateProgress.Value == 37);
        Check(guiUpdateProgress.Visibility == Visibility.Visible && !guiUpdateProgress.IsIndeterminate && guiUpdateProgressText.Text.Contains("37%") && status.Text == statusBefore, "Update progress is shown under its button without writing to the chat status chip.");
        await Task.Delay(100); root.UpdateLayout();
        Check(guiUpdateProgress.TransformToVisual(guiUpdatePanel).TransformPoint(new()).Y >= guiUpdateButton.ActualHeight, "The progress bar is physically below the update button.");
        await Capture(root, Path.Combine(output, "update-progress.png"));
        gate.TrySetResult(executable); await installed.Task.WaitAsync(TimeSpan.FromSeconds(5)); await Wait(() => !updatingApplication);
        Check(guiUpdateProgress.Visibility == Visibility.Collapsed, "The progress indicator disappears when the update operation finishes.");
        File.WriteAllLines(Path.Combine(output, "smoke-ok.txt"), passed);
    }
}
