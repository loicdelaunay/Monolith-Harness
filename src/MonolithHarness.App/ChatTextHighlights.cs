using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace MonolithHarness.App;

static class ChatTextHighlights
{
    sealed class State { public string Query = "", Text = ""; public TextHighlighter? Highlight; }
    static readonly ConditionalWeakTable<TextBlock, State> states = new();
    internal static void Apply(DependencyObject element, string query)
    {
        if (element is AdvancedMarkdownView advanced) { advanced.SetSearchQuery(query); return; }
        if (element is TextBlock text)
        {
            if (!text.IsTextSelectionEnabled) return;
            if (query.Length == 0 && !states.TryGetValue(text, out _)) return;
            var state = states.GetOrCreateValue(text); var content = text.Text ?? "";
            if (state.Query == query && state.Text == content) return;
            if (state.Highlight != null) text.TextHighlighters.Remove(state.Highlight);
            state.Highlight = null; state.Query = query; state.Text = content;
            if (query.Length == 0 || content.Length == 0) return;
            var mark = new TextHighlighter {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 191, 36)),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 24, 24, 24)) };
            var offset = 0;
            while (offset <= content.Length - query.Length)
            {
                var found = content.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
                if (found < 0) break;
                mark.Ranges.Add(new TextRange { StartIndex = found, Length = query.Length }); offset = found + query.Length;
            }
            if (mark.Ranges.Count > 0) { text.TextHighlighters.Add(mark); state.Highlight = mark; }
            return;
        }
        if (element is Panel panel) { foreach (var child in panel.Children) Apply(child, query); return; }
        if (element is Border border && border.Child != null) { Apply(border.Child, query); return; }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Apply(VisualTreeHelper.GetChild(element, i), query);
    }
}

public sealed partial class MainWindow
{
    void ApplyChatTextHighlights(DependencyObject body)
    {
        var query = chatFindBar.Visibility == Visibility.Visible && (ReferenceEquals(body, messages) || ContainsVisual(messages, body)) ? chatFindInput.Text : "";
        ChatTextHighlights.Apply(body, query);
    }
    void RefreshChatTextHighlights() => ApplyChatTextHighlights(messages);
}
