using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MonolithHarness.App;

// The committed value is independent of the text currently being searched.
internal class SearchDropDown<T> : UserControl where T : class
{
    readonly AutoSuggestBox search;
    readonly Button browse;
    readonly TextBlock hint = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary };
    readonly Func<T, string> caption;
    List<T> choices = [];
    T? selected;
    bool syncing;
    string query = "", emptyHint = "", noMatchHint = "";
    int matchCount;

    public event EventHandler? SelectionChanged;
    public IReadOnlyList<T> Items => choices;
    public IEnumerable<T> ItemsSource
    {
        get => choices;
        set
        {
            choices = value.ToList();
            selected = choices.FirstOrDefault(x => Equals(x, selected));
            ResetSearch();
        }
    }
    public T? SelectedItem
    {
        get => selected;
        set
        {
            if (value != null && !choices.Contains(value)) return;
            var changed = !Equals(selected, value);
            selected = value;
            ResetSearch();
            ToolTipService.SetToolTip(this, selected == null ? null : caption(selected));
            if (changed) SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public DataTemplate ItemTemplate { set => search.ItemTemplate = value; }
    public double MaxSuggestionListHeight { set => search.MaxSuggestionListHeight = value; }

    protected SearchDropDown(string textMemberPath, Func<T, string> caption)
    {
        this.caption = caption;
        search = new AutoSuggestBox { TextMemberPath = textMemberPath, MaxSuggestionListHeight = 260,
            MinHeight = 38, FontSize = 14, Padding = new(10, 5, 10, 5), BorderThickness = new(0),
            Foreground = FluentDesign.Primary, Background = FluentDesign.Resource("TransparentBrush"),
            HorizontalAlignment = HorizontalAlignment.Stretch };
        browse = new Button { Width = 34, MinWidth = 0, MinHeight = 38, Padding = new(0), BorderThickness = new(0),
            Background = FluentDesign.Resource("TransparentBrush"), Content = FluentDesign.Icon("\uE70D", 12) };
        var field = new Grid();
        field.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        field.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        field.Children.Add(search); Grid.SetColumn(browse, 1); field.Children.Add(browse);
        var border = new Border { Child = field, BorderThickness = new(1), CornerRadius = new(7),
            BorderBrush = FluentDesign.Stroke, Background = FluentDesign.Card };
        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(border); content.Children.Add(hint); Content = content;
        field.GotFocus += (_, _) => border.BorderBrush = FluentDesign.Resource("AccentFillColorDefaultBrush");
        field.LostFocus += (_, _) => border.BorderBrush = FluentDesign.Stroke;
        LostFocus += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (XamlRoot == null) return;
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            for (var node = focused; node != null; node = VisualTreeHelper.GetParent(node))
                if (ReferenceEquals(node, this)) return;
            if (!search.IsSuggestionListOpen) ResetSearch();
        });
        search.GotFocus += (_, _) =>
        {
            if (query.Length != 0 || search.Text != (selected == null ? "" : caption(selected))) return;
            search.DispatcherQueue.TryEnqueue(() =>
            {
                if (query.Length == 0 && search.Text == (selected == null ? "" : caption(selected))) FindTextBox(search)?.SelectAll();
            });
        };
        search.TextChanged += (_, e) =>
        {
            if (syncing || e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            query = search.Text.Trim(); Filter(query); search.IsSuggestionListOpen = matchCount > 0;
        };
        search.QuerySubmitted += (_, e) =>
        {
            var choice = e.ChosenSuggestion as T;
            if (choice == null)
            {
                var exact = choices.Where(x => caption(x).Equals(e.QueryText.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                if (exact.Count == 1) choice = exact[0];
            }
            if (choice != null) SelectedItem = choice;
            else { query = e.QueryText.Trim(); Filter(query); search.IsSuggestionListOpen = matchCount > 0; }
        };
        search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Escape || !search.IsSuggestionListOpen) return;
            ResetSearch(); e.Handled = true;
        };
        browse.Click += (_, _) => { if (search.IsSuggestionListOpen) ResetSearch(); else OpenSuggestions(); };
    }

    protected void ConfigureLanguage(string placeholder, string empty, string noMatch, string browseName)
    {
        search.PlaceholderText = placeholder; emptyHint = empty; noMatchHint = noMatch;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, placeholder);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(browse, browseName);
        ToolTipService.SetToolTip(browse, browseName); RefreshHint();
    }
    void Filter(string text)
    {
        var terms = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var matches = choices.Where(x => terms.All(term => caption(x).Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => caption(x).StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(caption, StringComparer.OrdinalIgnoreCase).ToList();
        matchCount = matches.Count; search.ItemsSource = matches.Take(50).ToList(); RefreshHint();
    }
    void RefreshHint()
    {
        hint.Text = choices.Count == 0 ? emptyHint : query.Length > 0 && matchCount == 0 ? noMatchHint : "";
        hint.Visibility = hint.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    public void ResetSearch()
    {
        syncing = true;
        try { query = ""; search.Text = selected == null ? "" : caption(selected); Filter(""); search.IsSuggestionListOpen = false; }
        finally { syncing = false; }
    }
    public void OpenSuggestions()
    {
        syncing = true;
        try { query = ""; search.Text = ""; Filter(""); }
        finally { syncing = false; }
        search.Focus(FocusState.Programmatic); search.IsSuggestionListOpen = choices.Count > 0;
    }
    static TextBox? FindTextBox(DependencyObject node)
    {
        if (node is TextBox text) return text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (FindTextBox(VisualTreeHelper.GetChild(node, i)) is { } child) return child;
        return null;
    }
}
