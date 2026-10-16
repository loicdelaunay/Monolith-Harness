using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeConversationEnhancements(string output)
    {
        var fixture = new Project { Name = "Conversation UX", Chats = [new Chat { Title = "Lecture stable pendant le streaming" }, new Chat { Title = "Conversation favorite" }] };
        db.Projects.Add(fixture); await db.SaveChangesAsync();
        projects.ItemsSource = new[] { fixture }; projects.SelectedItem = fixture; await SelectProject();
        var favorite = allProjectChats.Single(c => c.Title == "Conversation favorite");
        await ToggleFavoriteAsync(favorite);
        if (chats.Items[0] is not Chat { IsFavorite: true }) throw new Exception("Favorite was not pinned first.");
        await Task.Delay(150); RefreshConversationProgress();
        if (chats.ContainerFromItem(favorite) is not ListViewItem container || FindConversationElement<Button>(container, "conversation-favorite") is not { Opacity: 1 }) throw new Exception("Favorite star unavailable.");
        if (chats.SelectionMode != ListViewSelectionMode.Extended || archivedChats.SelectionMode != ListViewSelectionMode.Extended)
            throw new Exception("Shift range selection is unavailable.");
        chats.SelectedItem = favorite;
        await Task.Delay(100);
        var openChat = chat?.Id;
        var second = allProjectChats.Single(c => c.Id != favorite.Id);
        chats.SelectedItems.Add(second);
        await Task.Delay(100);
        if (chats.SelectedItems.Count != 2 || chat?.Id != openChat || !chatCount.Text.Contains("2"))
            throw new Exception("Multi-selection reloaded the conversation or lost its selection count.");
        await ArchiveChatsAsync(chats.SelectedItems.OfType<Chat>().ToArray(), true);
        if (chats.Items.Count != 0 || archivedChats.Items.Count != 2 || !archiveExpander.IsExpanded)
            throw new Exception("Archived conversations were not moved to the project subgroup.");
        await Task.Delay(100); await Capture(root, Path.Combine(output, "conversation-archive.png"));
        await SelectProject();
        if (archivedChats.Items.Count != 2 || chat?.Id != openChat || !allProjectChats.All(c => c.IsArchived))
            throw new Exception("Archive state or current conversation was lost after a project reload.");
        await ArchiveChatsAsync(visibleArchivedChats.ToArray(), false);
        if (chats.Items.Count != 2 || archivedChats.Items.Count != 0)
            throw new Exception("Restored conversations did not return to the active list.");
        var target = chat!;
        var pending = new PendingInput { ChatId = target.Id, ProviderId = state.ProviderId, Text = "Corrige les problèmes suivants dans l’UI et CLI : vérifier les tests puis résumer les modifications.\nConserver les fonctionnalités existantes." };
        db.PendingInputs.Add(pending); await db.SaveChangesAsync(); await RefreshInboxAsync();
        var queueCard = inboxPanel.Children.OfType<Border>().Single();
        if (queueCard.Child is not Grid grid || grid.ColumnDefinitions.Count != 3 || grid.Children.OfType<StackPanel>().Single().Children.OfType<Button>().Count() != 5) throw new Exception("Queue actions are not five inline buttons.");
        AddMessage("user", "Je lis le début de la réponse pendant que l’agent continue.", [], messages);
        var assistant = AddAssistantMessage("## Résultat\n\nPremier paragraphe terminé.\n\nRéponse en cours", target: messages);
        static string InlineText(Microsoft.UI.Xaml.Documents.Inline inline) => inline switch
        {
            Microsoft.UI.Xaml.Documents.Run run => run.Text,
            Microsoft.UI.Xaml.Documents.Span span => string.Concat(span.Inlines.Select(InlineText)),
            _ => ""
        };
        static string DisplayedText(UIElement element) => element switch
        {
            TextBlock block => block.Text + string.Concat(block.Inlines.Select(InlineText)),
            Panel panel => string.Concat(panel.Children.Select(DisplayedText)),
            _ => ""
        };
        var first = assistant.BodyContainer.Children[0];
        var oldLast = assistant.BodyContainer.Children[^1];
        var pausedText = DisplayedText(assistant.BodyContainer);
        SetChatFollow(false);
        assistant.UpdateContent("## Résultat\n\nPremier paragraphe terminé.\n\nRéponse en cours, nouveau texte", streaming: true);
        if (!ReferenceEquals(first, assistant.BodyContainer.Children[0]) || !ReferenceEquals(oldLast, assistant.BodyContainer.Children[^1]) || DisplayedText(assistant.BodyContainer) != pausedText) throw new Exception("Streaming repainted paused reading content.");
        SetChatFollow(true);
        if (!ReferenceEquals(first, assistant.BodyContainer.Children[0]) || !DisplayedText(assistant.BodyContainer).Contains("nouveau texte")) throw new Exception("Resuming did not preserve stable blocks and flush the latest text.");
        // Streaming parses completed blocks once; the final render must equal a full render.
        const string streamedReply = "## Plan\n\n- un\n- deux\n\n- trois\n\nTexte **gras** et `code`.\n\n```csharp\nvar x = 1;\n\nvar y = 2;\n```\n\n1. premier\n\n2. second\n\nConclusion avec [lien](https://example.com).";
        var streamed = AddAssistantMessage("", target: messages);
        UIElement? firstStreamed = null;
        for (int length = 4; length < streamedReply.Length; length += 5)
        {
            streamed.UpdateContent(streamedReply[..length], streaming: true);
            if (length > 40 && firstStreamed == null) firstStreamed = streamed.BodyContainer.Children[0];
        }
        streamed.UpdateContent(streamedReply, streaming: true);
        if (firstStreamed == null || !ReferenceEquals(firstStreamed, streamed.BodyContainer.Children[0])) throw new Exception("Streaming rebuilt completed leading blocks.");
        streamed.UpdateContent(streamedReply);
        var reference = AddAssistantMessage(streamedReply, target: messages);
        if (DisplayedText(streamed.BodyContainer) != DisplayedText(reference.BodyContainer) || streamed.BodyContainer.Children.Count != reference.BodyContainer.Children.Count)
            throw new Exception($"The final streamed render differs from a full render of the same reply.\n[{streamed.BodyContainer.Children.Count}] {DisplayedText(streamed.BodyContainer)}\n[{reference.BodyContainer.Children.Count}] {DisplayedText(reference.BodyContainer)}");
        static string VisibleText(TextBlock block) => block.Inlines.Count > 0 ? string.Concat(block.Inlines.Select(InlineText)) : block.Text;
        streamed.UpdateThinking("Première étape.", streaming: true);
        streamed.UpdateThinking("Première étape. Deuxième étape.", streaming: true);
        streamed.UpdateThinking("Première étape. Deuxième étape. Troisième.", streaming: true);
        if (VisibleText(streamed.ThinkingBody) != "Première étape. Deuxième étape. Troisième." || streamed.ThinkingBody.Inlines.Count < 2 || streamed.ThinkingBody.Text != VisibleText(streamed.ThinkingBody))
            throw new Exception("Streamed reasoning was not appended to the existing text.");
        streamed.UpdateThinking("Texte remplacé.", streaming: true);
        if (VisibleText(streamed.ThinkingBody) != "Texte remplacé.") throw new Exception("Replaced reasoning kept stale runs.");
        foreach (var extra in new[] { streamed, reference })
        {
            if (extra.Container is { } card) MessageHost(messages).Children.Remove(card);
            extra.ThinkingCard.Visibility = Visibility.Collapsed;
        }
        assistant.SetDuration(73.2);
        if (!assistant.Duration.Text.Contains("1 min")) throw new Exception("Duration missing from response footer.");
        if (assistant.Duration.Visibility != Visibility.Visible || assistant.Duration.Opacity != 0) throw new Exception("Response duration should reserve its space but remain hidden until hover.");
        var assistantBubble = assistant.Container ?? throw new Exception("Assistant bubble missing.");
        assistantBubble.UpdateLayout();
        var heightBeforeHover = assistantBubble.ActualHeight;
        assistant.SetHovered(true);
        assistantBubble.UpdateLayout();
        if (assistant.Duration.Opacity != 1) throw new Exception("Response duration did not appear on hover.");
        if (Math.Abs(assistantBubble.ActualHeight - heightBeforeHover) > 0.5) throw new Exception("Hover changed the assistant bubble height.");
        assistant.SetHovered(false);
        assistantBubble.UpdateLayout();
        if (assistant.Duration.Opacity != 0 || Math.Abs(assistantBubble.ActualHeight - heightBeforeHover) > 0.5) throw new Exception("Response duration remained visible or resized the bubble after hover.");
        var preferences = BuildConversationPreferences(); var settings = FeatureSettings.Read(state.FeaturesJson); preferences.Save(settings);
        if (!preferences.Validate() || settings.LogRetentionDays != 7) throw new Exception("Conversation preferences defaults invalid.");
        await Task.Delay(200); await Capture(root, Path.Combine(output, "conversation-enhancements.png"));
        var overlay = new Border { Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush"), Padding = new(24), Width = 650, HorizontalAlignment = HorizontalAlignment.Center, Child = new ScrollViewer { Content = preferences.Panel, MaxHeight = 650 } };
        root.Children.Add(overlay); await Task.Delay(100); await Capture(root, Path.Combine(output, "conversation-preferences.png")); root.Children.Remove(overlay);
        File.WriteAllText(Path.Combine(output, "smoke-ok.txt"), "Favorites, multi-selection, persistent archive/restore, inline queue actions, stable streaming, duration footer and settings defaults passed.");
    }
}
