using Microsoft.EntityFrameworkCore;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using MonolithHarness.Core.Remote;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

/// <summary>
/// The shared life of a conversation on a server: who wrote each message (avatar), what happens in it when somebody else drives it
/// (live reasoning and text, refreshed about twice a second), renames that reach everybody, and the number of people following a
/// conversation this device shares.
/// </summary>
public sealed partial class MainWindow
{
    // ---- Avatars ----

    /// <summary>A round avatar: the initials of the account on a color derived from its address (the same on every device and on the server pages).</summary>
    FrameworkElement BuildAvatar(ServerAuthor author, double size = 20)
    {
        var (r, g, b) = Avatar.Color(author.Email);
        var initials = new TextBlock
        {
            Text = Avatar.Initials(author.Name, author.Email), FontSize = Math.Max(8, size * 0.42), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var circle = new Border { Width = size, Height = size, CornerRadius = new(size / 2), Background = new SolidColorBrush(ColorHelper.FromArgb(255, r, g, b)), Child = initials };
        ToolTipService.SetToolTip(circle, author.Name.Length > 0 && author.Name != author.Email ? $"{author.Name} · {author.Email}" : author.Email);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(circle, author.DisplayName);
        return circle;
    }

    /// <summary>The line at the top of a user bubble in a shared project: who sent it.</summary>
    FrameworkElement BuildAuthorHeader(ServerAuthor author)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Right, Tag = "author" };
        row.Children.Add(new TextBlock { Text = author.DisplayName, FontSize = 11, Foreground = FluentDesign.Secondary, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(BuildAvatar(author, 20));
        return row;
    }

    // ---- Titles ----

    /// <summary>A conversation was renamed here: a cloud conversation is renamed on the server, a shared one for its viewers.</summary>
    void OnChatTitleChanged(int chatId, string newTitle)
    {
        if (!ServerLink.Configured) return;
        if (ServerLink.Chats.ContainsKey(chatId)) Sync.RequestPush(chatId);
        if (sharedChats.TryGetValue(chatId, out var share)) _ = ForwardSafely(share, "title", new { title = newTitle });
        if (chat?.Id == chatId) RefreshSharingIndicator();
    }

    // ---- Followers of the conversation (host side) ----

    readonly Dictionary<int, int> shareViewerCounts = [];
    Border? sharingChip;
    TextBlock? sharingChipText;

    /// <summary>The chip at the top of a conversation this device shares: how many people follow it, live.</summary>
    FrameworkElement BuildSharingChip()
    {
        sharingChipText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = FluentDesign.Primary };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(FluentDesign.Icon("", 13));
        content.Children.Add(sharingChipText);
        sharingChip = new Border
        {
            Child = content, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0), Padding = new(10, 4, 10, 4), CornerRadius = new(14),
            Background = FluentDesign.Card, BorderBrush = FluentDesign.Stroke, BorderThickness = new(1)
        };
        sharingChip.Tapped += async (_, _) => { if (chat is { } shown && sharedChats.ContainsKey(shown.Id)) await Guard(() => ManageShareAsync(shown)); };
        return sharingChip;
    }

    Task OnShareViewersAsync(int chatId, int count) => RunOnUiAsync(() =>
    {
        shareViewerCounts[chatId] = count;
        RefreshSharingChip();
        return Task.CompletedTask;
    });

    void RefreshSharingChip()
    {
        if (sharingChip == null || sharingChipText == null) return;
        var shared = chat != null && sharedChats.ContainsKey(chat.Id);
        sharingChip.Visibility = shared ? Visibility.Visible : Visibility.Collapsed;
        if (!shared) return;
        var count = shareViewerCounts.GetValueOrDefault(chat!.Id);
        sharingChipText.Text = count == 1 ? WorkflowText("1 spectateur", "1 viewer") : WorkflowText($"{count} spectateurs", $"{count} viewers");
        sharingChip.Background = count > 0 ? new SolidColorBrush(ColorHelper.FromArgb(60, 46, 158, 91)) : FluentDesign.Card;
        var tip = count == 0 ? WorkflowText("Personne ne suit cette conversation en ce moment. Cliquez pour partager le code.", "Nobody follows this conversation right now. Click to share the code.")
            : WorkflowText($"{count} personne(s) suivent cette conversation en direct. Cliquez pour gérer le partage.", $"{count} people follow this conversation live. Click to manage sharing.");
        ToolTipService.SetToolTip(sharingChip, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(sharingChip, tip);
    }

    // ---- Following a request sent from elsewhere (cloud conversations) ----

    readonly HashSet<int> followedChats = [];
    AssistantMessageUi? followBubble;
    int followBubbleChat;

    /// <summary>
    /// Another member (or another device of this account) is running a request in a conversation shown here: its reasoning and text
    /// appear as they are written, and the messages that complete replace them. Nothing is sent from here.
    /// </summary>
    Task OnRemoteProgressAsync(RemoteProgress progress) => RunOnUiAsync(async () =>
    {
        if (progress.Finished)
        {
            followedChats.Remove(progress.LocalChatId);
            if (followBubbleChat == progress.LocalChatId) followBubble = null;
            if (chat?.Id == progress.LocalChatId && !conversationRuns.ContainsKey(progress.LocalChatId)) { await RefreshDisplayedChatAsync(); ClearChatStatus(progress.LocalChatId); }
            return;
        }
        followedChats.Add(progress.LocalChatId);
        if (chat?.Id != progress.LocalChatId || conversationRuns.ContainsKey(progress.LocalChatId)) return;
        var who = progress.Requester.Length > 0 ? progress.Requester : WorkflowText("Un autre membre", "Another member");
        ShowStatus(progress.State == "queued" ? WorkflowText($"{who} · en attente sur le serveur", $"{who} · queued on the server")
            : progress.Reasoning.Length > 0 && progress.Text.Length == 0 ? WorkflowText($"{who} réfléchit…", $"{who} is thinking…") : WorkflowText($"{who} écrit…", $"{who} is writing…"), StatusKind.Activity, progress.LocalChatId);
        if (progress.Text.Length == 0 && progress.Reasoning.Length == 0) return;
        if (followBubble == null || followBubbleChat != progress.LocalChatId)
        {
            followBubble = AddAssistantMessage("", target: messages, sourceProject: project);
            followBubbleChat = progress.LocalChatId;
        }
        if (progress.Reasoning.Length > 0) followBubble.UpdateThinking(progress.Reasoning, isComplete: progress.Text.Length > 0, streaming: true);
        followBubble.UpdateContent(progress.Text, streaming: true);
        if (followChatTail) ScrollToBottom();
    });

    void ClearChatStatus(int chatId) { if (visibleStatusChatId == chatId) ClearStatus(); }

    /// <summary>
    /// Reads the displayed conversation again and redraws it where it is (no reload of the whole project): used when it changed on the
    /// server. A message still being written is left to the live bubble, so it does not appear twice.
    /// </summary>
    async Task RefreshDisplayedChatAsync()
    {
        if (chat == null || conversationRuns.ContainsKey(chat.Id)) return;
        var id = chat.Id;
        var history = await ReadStoreAsync(store => store.Messages.AsNoTracking().Where(x => x.ChatId == id).OrderBy(x => x.Id).ToList());
        if (chat?.Id != id || conversationRuns.ContainsKey(id)) return;
        var following = followedChats.Contains(id);
        if (following && history.Count > 0 && history[^1] is { Role: "assistant", State: "interrupted" }) history.RemoveAt(history.Count - 1);
        var tail = followChatTail;
        ClearMessagePanel(messages);
        conversationHistory[id] = history;
        RenderHistory(history, messages, project);
        followBubble = null;
        if (tail) ScrollToBottom();
    }
}
