using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Windows.Foundation;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeResponsiveSidebar(string output)
    {
        static void Click(Button button) => (new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider
            ?? throw new Exception("Button invocation unavailable")).Invoke();
        var passed = new List<string>(); var measurements = new List<object>();
        void Check(bool valid, string name) { if (!valid) throw new Exception(name); passed.Add(name); }
        Check(chat != null && project != null, "An isolated chat and project are available.");
        var fixture = new Provider { Name = "Offline responsive fixture", Kind = "openai", Model = "deepseek-flash-long-model-name", SelectedModelsJson = "[\"deepseek-flash-long-model-name\"]" };
        db.Providers.Add(fixture); await db.SaveChangesAsync(); provider = fixture; state.ProviderId = fixture.Id;
        var config = FeatureSettings.Read(state.FeaturesJson); config.ShowComposerSpeed = config.ShowComposerContext = true;
        state.FeaturesJson = config.Json(); state.PermissionMode = PermissionModes.Allow;
        recentComposerSpeeds[chat!.Id] = (1234.5, false);
        contextPercentText.Text = "92,9 %"; contextValueText.Text = "232 250 / 250 000 tokens";
        PopulateModelSelector(); RefreshComposerPermissions(); RefreshComposerModelSummary();
        ClearMessagePanel(messages); AddMessage("user", "Contrôle de l’affichage responsive.");
        AddAssistantMessage("Les commandes restent accessibles quand la fenêtre est réduite. Le débit disparaît avant le chiffre du contexte ; le cercle reste disponible.");
        var documentTile = CreateResourceTile("Notes de la conversation.txt", "offline-notes.txt", false, () => Task.CompletedTask, () => Task.CompletedTask);
        var imageTile = CreateResourceTile("Capture.png", "offline-capture.png", false, () => Task.CompletedTask, () => Task.CompletedTask);
        byte[] attachmentPng;
        using (var bitmap = new SkiaSharp.SKBitmap(32, 32))
        { bitmap.Erase(SkiaSharp.SKColors.CornflowerBlue); using var image = SkiaSharp.SKImage.FromBitmap(bitmap); using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100); attachmentPng = data.ToArray(); }
        imageTile.LoadPreview(_ => Task.FromResult(attachmentPng));
        assetsBar.Children.Add(documentTile); assetsBar.Children.Add(imageTile); RefreshComposerAttachments();
        var attachmentTiles = assetsBar.Children.OfType<ComposerResourceTile>().ToArray();
        foreach (var zoom in new[] { 100, 150 })
        {
            var stages = new HashSet<int>();
            TextZoom.Set(zoom);
            foreach (var width in new[] { 2200, 1440, 1320, 1280, 1240, 1200, 1160, 1120, 1080, 1040, 1000, 960, 920, 900, 880, 860, 840, 820, 800, 780, 760, 740, 720, 700, 680, 660, 640, 620, 600, 580, 520, 480, 420, 380, 2200 })
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = width, Height = 900 });
                await Task.Delay(250); shell.IsPaneOpen = false;
                root.UpdateLayout(); LayoutComposerFooter(); LayoutComposerAttachments(); await Task.Delay(160); root.UpdateLayout();
                var prefix = $"width={width}, root={root.ActualWidth}, footer={composerFooterViewport.ActualWidth}, zoom={zoom}, stage={composerResponsiveStage}, left={NaturalWidth(composerModeHost)}, summary={NaturalWidth(composerModelSummary)}, buttons={NaturalWidth(composerActionButtons)}";
                await Capture(root, Path.Combine(output, "last-frame.png"));
                stages.Add(composerResponsiveStage);
                Check(composerFooterGrid.RowDefinitions.Count <= 1, prefix + ": footer stays on one row.");
                foreach (var element in new FrameworkElement[] { composerModeHost, modelOptionsButton, stop, send })
                {
                    var bounds = element.TransformToVisual(composerFooterViewport).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                    Check(bounds.Left >= -.8 && bounds.Right <= composerFooterViewport.ActualWidth + .8, prefix + ": " + element.GetType().Name + " stays inside the composer.");
                }
                Check(selectedModelName.ActualWidth > 8, prefix + ": model name retains visible space.");
                Check(composerContextIndicator.Visibility == Visibility.Visible, prefix + ": context ring remains available.");
                Check(composerContextText.Visibility != Visibility.Collapsed || composerSpeedIndicator.Visibility == Visibility.Collapsed, prefix + ": speed hides before context percentage.");
                Check(composerResponsiveStage < 3 || composerContextText.Visibility == Visibility.Collapsed && composerSpeedIndicator.Visibility == Visibility.Collapsed, prefix + ": compact controls follow metric hiding.");
                foreach (var button in new FrameworkElement[] { chatNavigationButton, chatExportButton, chatToolsButton, autoScrollButton })
                {
                    var bounds = button.TransformToVisual(chatHeader).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                    Check(bounds.Left >= -.8 && bounds.Right <= chatHeader.ActualWidth + .8, prefix + ": header action stays visible.");
                }
                foreach (var tile in attachmentTiles)
                {
                    Check(Math.Abs(tile.ActualWidth - tile.ActualHeight) < .8 && tile.ActualWidth >= 47.5 && tile.ActualWidth <= 96.8,
                        prefix + ": attachments keep their responsive square size, including text zoom.");
                    var preview = tile.PreviewButton.TransformToVisual(tile).TransformBounds(new Rect(0, 0, tile.PreviewButton.ActualWidth, tile.PreviewButton.ActualHeight));
                    Check(preview.Left >= -.8 && preview.Top >= -.8 && preview.Right <= tile.ActualWidth + .8 && preview.Bottom <= tile.ActualHeight + .8,
                        prefix + ": preview content remains inside its resized tile.");
                    if (width <= 580) Check(tile.ActualHeight <= 64, prefix + ": narrow windows reduce attachment height.");
                }
                Check(imageTile.Thumbnail.Source != null, prefix + ": image preview is retained when resizing.");
                if (width == 2200) Check(attachmentTiles.All(tile => Math.Abs(tile.ActualWidth - Math.Clamp(root.ActualHeight * .14, 48, 96)) < .8), prefix + ": attachment size returns when widening.");
                if (width <= 580) Check(compactChatHeader && chatHeaderLabels.All(item => item.Label.Visibility == Visibility.Collapsed), prefix + ": header actions use icons only.");
                if (width == 2200 && zoom == 100) Check(composerResponsiveStage == 0 && composerSpeedIndicator.Visibility == Visibility.Visible && composerContextText.Visibility == Visibility.Visible, prefix + ": widening restores metrics.");
                measurements.Add(new { width, zoom, stage = composerResponsiveStage, footerWidth = composerFooterViewport.ActualWidth,
                    naturalWidth = composerFooterGrid.ActualWidth, iconsOnly = compactChatHeader, attachmentSize = attachmentTiles[0].ActualWidth });
                if (width is 1440 or 580 or 380)
                {
                    await Capture(root, Path.Combine(output, $"responsive-{width}-{zoom}.png"));
                    Click(composerAttachmentsToggle); await Task.Delay(100); root.UpdateLayout();
                    Check(assetsScroll.Visibility == Visibility.Collapsed && composerAttachmentsLabel.Visibility == Visibility.Visible &&
                        composerAttachmentsLabel.Text == $"Voir pièces jointes ({attachmentTiles.Length})", prefix + ": collapsed attachments show their exact count.");
                    var collapsedBounds = composerAttachmentsToggle.TransformToVisual(composerAttachments).TransformBounds(new Rect(0, 0, composerAttachmentsToggle.ActualWidth, composerAttachmentsToggle.ActualHeight));
                    Check(collapsedBounds.Right <= composerAttachments.ActualWidth + .8 && composerAttachmentsLabel.ActualWidth > 30,
                        prefix + ": collapsed attachment label fits the available width.");
                    await Capture(root, Path.Combine(output, $"attachments-collapsed-{width}-{zoom}.png"));
                    Click(composerAttachmentsToggle); await Task.Delay(100); root.UpdateLayout();
                    Check(assetsScroll.Visibility == Visibility.Visible && composerAttachmentsLabel.Visibility == Visibility.Collapsed &&
                        attachmentTiles.Length == assetsBar.Children.Count && imageTile.Thumbnail.Source != null, prefix + ": expanding preserves all attachment previews.");
                }
            }
            await File.WriteAllTextAsync(Path.Combine(output, "responsive-measurements.json"), System.Text.Json.JsonSerializer.Serialize(measurements, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Check(stages.Contains(1) && stages.Contains(2) && stages.Contains(3), $"All three progressive compaction stages are exercised at zoom {zoom}: " + string.Join(", ", stages));
        }
        Click(composerAttachmentsToggle); await Task.Delay(80);
        assetsBar.Children.Remove(documentTile); RefreshComposerAttachments();
        Check(composerAttachmentsLabel.Text == $"Voir pièces jointes ({attachmentTiles.Length - 1})" && assetsScroll.Visibility == Visibility.Collapsed,
            "Attachment count updates while collapsed without expanding the previews.");
        assetsBar.Children.Add(documentTile); RefreshComposerAttachments(); Click(composerAttachmentsToggle);
        AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1440, Height = 500 }); await Task.Delay(250); root.UpdateLayout(); LayoutComposerAttachments(); await Task.Delay(100); root.UpdateLayout();
        Check(attachmentTiles.All(tile => tile.ActualHeight <= Math.Max(48, root.ActualHeight * .14) + .8 && Math.Abs(tile.ActualWidth - tile.ActualHeight) < .8),
            "Short windows also reduce attachment height while preserving square previews.");
        AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 2200, Height = 900 }); await Task.Delay(250); root.UpdateLayout();
        AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 580, Height = 900 }); await Task.Delay(250);
        var extraTiles = Enumerable.Range(1, 7).Select(index => CreateResourceTile($"Document {index}.txt", $"offline-extra-{index}.txt", false, () => Task.CompletedTask, null)).ToArray();
        foreach (var tile in extraTiles) assetsBar.Children.Add(tile);
        RefreshComposerAttachments(); root.UpdateLayout(); await Task.Delay(120); root.UpdateLayout();
        Check(assetsScroll.ScrollableWidth > 0 && assetsScroll.ActualHeight <= 100,
            "Many attachments remain on a compact horizontally scrollable row.");
        Click(composerAttachmentsToggle); await Task.Delay(100);
        Check(composerAttachmentsLabel.Text == $"Voir pièces jointes ({assetsBar.Children.Count})" && assetsScroll.Visibility == Visibility.Collapsed,
            "Collapsed label counts all attachments, including those outside the visible part of the row.");
        await Capture(root, Path.Combine(output, "attachments-many-collapsed.png"));
        Click(composerAttachmentsToggle); await Task.Delay(100); await Capture(root, Path.Combine(output, "attachments-many.png"));
        foreach (var tile in extraTiles) assetsBar.Children.Remove(tile);
        RefreshComposerAttachments();
        AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 2200, Height = 900 }); await Task.Delay(250); root.UpdateLayout();
        config.ShowComposerSpeed = false; config.ShowComposerContext = false; state.FeaturesJson = config.Json(); RefreshComposerModelSummary();
        Check(composerSpeedIndicator.Visibility == Visibility.Collapsed && composerContextIndicator.Visibility == Visibility.Collapsed, "Explicitly disabled metrics stay disabled after widening.");
        config.ShowComposerSpeed = config.ShowComposerContext = true; state.FeaturesJson = config.Json(); TextZoom.Set(100);
        AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1240, Height = 900 }); await Task.Delay(250); shell.IsPaneOpen = true;
        var first = new Chat { ProjectId = project!.Id, Title = "Glisser Alpha" }; var second = new Chat { ProjectId = project.Id, Title = "Glisser Beta" };
        var third = new Chat { ProjectId = project.Id, Title = "Glisser Gamma" };
        db.Chats.AddRange(first, second, third); await db.SaveChangesAsync(); await SelectProject();
        var selectedId = chat!.Id; composer.Text = "Brouillon préservé après chaque déplacement";
        foreach (var after in new[] { false, true, false, true, false, true })
        {
            await PersistSidebarMoveAsync("chat", first.Id, false, second.Id, after);
            await FlushSidebarDropRefreshAsync(); await Task.Delay(80); root.UpdateLayout();
            var ids = visibleProjectChats.Select(item => item.Id).ToList();
            Check(ids.IndexOf(first.Id) == ids.IndexOf(second.Id) + (after ? 1 : -1), "Repeated move places the chat exactly " + (after ? "after" : "before") + " its target.");
            Check(chat?.Id == selectedId && composer.Text == "Brouillon préservé après chaque déplacement", "Reordering preserves the active chat and draft.");
            var row = (ListViewItem)chats.ContainerFromItem(allProjectChats.Single(item => item.Id == first.Id));
            Check(row.ActualHeight >= 20 && row.ActualWidth > 100, $"Reordered chat row is visible: {row.ActualWidth} x {row.ActualHeight}; parent={row.Parent?.GetType().Name}.");
            Check(row.CanDrag && row.AllowDrop, "Reordered/recycled chat containers remain draggable.");
            row.CanDrag = row.AllowDrop = false;
            EnableSidebarDrag(row, false, () => (chats.ItemFromContainer(row) as Chat)?.Id);
            Check(row.CanDrag && row.AllowDrop && sidebarDragWiring.TryGetValue(row, out _), "Re-enabling a recycled row restores drag properties with existing handlers.");
        }
        var target = (ListViewItem)chats.ContainerFromItem(allProjectChats.Single(item => item.Id == second.Id));
        Check(target.ActualHeight >= 20 && target.ActualWidth > 100, $"Drop target has visible bounds: {target.ActualWidth} x {target.ActualHeight}.");
        ShowSidebarDropMarker(target, false); var top = Canvas.GetTop(sidebarDropMarker);
        Check(sidebarDropMarker.Visibility == Visibility.Visible && !sidebarDropOverlay.IsHitTestVisible, "Insertion marker is visible without intercepting the drag.");
        root.UpdateLayout(); await Task.Delay(100);
        await Capture(root, Path.Combine(output, "sidebar-drop-before.png"));
        ShowSidebarDropMarker(target, true);
        Check(Math.Abs(Canvas.GetTop(sidebarDropMarker) - top - target.ActualHeight) < 1, "The insertion marker switches precisely between the top and bottom edge.");
        root.UpdateLayout(); await Task.Delay(100);
        await Capture(root, Path.Combine(output, "sidebar-drop-after.png")); ClearSidebarDropMarker();
        Check(sidebarDropMarker.Visibility == Visibility.Collapsed, "The drop marker clears when the operation ends.");
        var saved = await db.States.AsNoTracking().SingleAsync();
        Check(FeatureSettings.Read(saved.FeaturesJson).SidebarChatOrder.IndexOf(first.Id) > FeatureSettings.Read(saved.FeaturesJson).SidebarChatOrder.IndexOf(second.Id), "Final repeated order is persisted.");
        await File.WriteAllTextAsync(Path.Combine(output, "responsive-measurements.json"), System.Text.Json.JsonSerializer.Serialize(measurements, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllLinesAsync(Path.Combine(output, "smoke-ok.txt"), passed);
    }
}
