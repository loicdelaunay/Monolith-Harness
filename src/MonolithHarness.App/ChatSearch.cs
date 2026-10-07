using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Input;
using MonolithHarness.Core;
using Windows.System;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly TextBox chatFindInput = new() { MinWidth = 150, PlaceholderText = "Ctrl+F" };
    readonly TextBlock chatFindCount = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = FluentDesign.Secondary };
    readonly Grid chatFindBar = new() { Visibility = Visibility.Collapsed, ColumnSpacing = 8 };
    readonly Dictionary<int, WeakReference<Border>> chatSearchAnchors = [];
    List<Message> chatFindMatches = [];
    int chatFindIndex = -1, chatFindRevision;
    Border? highlightedSearchCard;
    Brush? originalSearchBrush;
    UIElement BuildChatFindBar()
    {
        chatFindBar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        chatFindBar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        chatFindBar.Children.Add(chatFindInput);
        var previous = new Button(); FluentDesign.IconButton(previous, "\uE70E", WorkflowText("Résultat précédent", "Previous result"), false);
        var next = new Button(); FluentDesign.IconButton(next, "\uE70D", WorkflowText("Résultat suivant", "Next result"), false);
        var close = new Button(); FluentDesign.IconButton(close, "\uE711", WorkflowText("Fermer la recherche", "Close search"), false);
        var actions = Row(chatFindCount, previous, next, close); Grid.SetColumn(actions, 1); chatFindBar.Children.Add(actions);
        previous.Click += async (_, _) => await Guard(() => NavigateChatFindAsync(-1));
        next.Click += async (_, _) => await Guard(() => NavigateChatFindAsync(1));
        close.Click += (_, _) => CloseChatFind();
        chatFindInput.TextChanged += async (_, _) => await Guard(RefreshChatFindAsync);
        chatFindInput.KeyDown += async (_, e) =>
        {
            if (e.Key == VirtualKey.Escape) { CloseChatFind(); e.Handled = true; }
            else if (e.Key == VirtualKey.Enter)
            {
                var shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
                e.Handled = true; await Guard(() => NavigateChatFindAsync(shift ? -1 : 1));
            }
        };
        root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key != VirtualKey.F || (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) == 0) return;
            e.Handled = true; chatFindBar.Visibility = Visibility.Visible;
            chatFindInput.PlaceholderText = WorkflowText("Rechercher dans cette conversation", "Find in this conversation");
            chatFindInput.Focus(FocusState.Programmatic); chatFindInput.SelectAll();
            _ = Guard(RefreshChatFindAsync);
        }), true);
        return chatFindBar;
    }
    void RestoreSearchHighlight()
    {
        if (highlightedSearchCard != null) highlightedSearchCard.BorderBrush = originalSearchBrush;
        highlightedSearchCard = null;
    }
    void CloseChatFind()
    {
        chatFindRevision++; chatFindBar.Visibility = Visibility.Collapsed; RestoreSearchHighlight();
        chatFindMatches.Clear(); chatFindIndex = -1; RefreshChatTextHighlights(); composer.Focus(FocusState.Programmatic);
    }
    async Task RefreshChatFindAsync()
    {
        var revision = ++chatFindRevision;
        var selected = chat; var query = chatFindInput.Text;
        RestoreSearchHighlight(); chatFindMatches = []; chatFindIndex = -1;
        if (selected == null || query.Length == 0 || chatFindBar.Visibility != Visibility.Visible) { RefreshChatTextHighlights(); chatFindCount.Text = "0 / 0"; return; }
        await Task.Delay(180);
        if (revision != chatFindRevision) return;
        RefreshChatTextHighlights();
        var history = await ReadStoreAsync(store => store.Messages.AsNoTracking().Where(x => x.ChatId == selected.Id && (x.Role == "user" || x.Role == "assistant")).OrderBy(x => x.Id).ToList());
        if (revision != chatFindRevision || chat?.Id != selected.Id) return;
        if (conversationRuns.TryGetValue(selected.Id, out var run))
        {
            var live = run.Db.Messages.Local.Where(x => x.ChatId == selected.Id && x.Role is "user" or "assistant").ToDictionary(x => x.Id);
            history = history.Where(x => !live.ContainsKey(x.Id)).Concat(live.Values).OrderBy(x => x.Id).ToList();
        }
        chatFindMatches = history.Where(x => x.Content.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (chatFindMatches.Count > 0) await NavigateChatFindAsync(1); else chatFindCount.Text = "0 / 0";
    }
    async Task NavigateChatFindAsync(int direction)
    {
        if (chatFindMatches.Count == 0) return;
        var revision = chatFindRevision; var panel = messages;
        chatFindIndex = chatFindIndex < 0 ? direction < 0 ? chatFindMatches.Count - 1 : 0 : (chatFindIndex + direction + chatFindMatches.Count) % chatFindMatches.Count;
        var target = chatFindMatches[chatFindIndex];
        chatFindCount.Text = $"{chatFindIndex + 1} / {chatFindMatches.Count}";
        SetChatFollow(false);
        Border? Anchor() => chatSearchAnchors.TryGetValue(target.Id, out var reference) && reference.TryGetTarget(out var element) && ContainsVisual(panel, element) ? element : null;
        if (virtualConversations.TryGetValue(panel, out var view)) await view.RevealAsync(target.Id);
        if (revision != chatFindRevision || !ReferenceEquals(panel, messages)) return;
        if (Anchor() is { } card)
        {
            RestoreSearchHighlight(); highlightedSearchCard = card; originalSearchBrush = card.BorderBrush;
            card.BorderBrush = FluentDesign.Resource("AccentFillColorDefaultBrush");
            RefreshChatTextHighlights();
            scroll.UpdateLayout(); card.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = .4, AnimationDesired = true });
        }
    }
}
