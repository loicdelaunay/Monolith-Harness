using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MonolithHarness.App;

public sealed class SettingsNavigation : Grid
{
    readonly NavigationView navigation = new()
    {
        PaneDisplayMode = NavigationViewPaneDisplayMode.Left, IsPaneOpen = true,
        IsSettingsVisible = false, IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
        IsPaneToggleButtonVisible = false, OpenPaneLength = 204, IsTitleBarAutoPaddingEnabled = false
    };
    readonly ScrollViewer body = new()
    { Name = "SettingsPageBody", HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(24, 0, 24, 24) };
    readonly TextBlock heading = new() { FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    readonly ContentPresenter header = new() { Visibility = Visibility.Collapsed };
    readonly ContentPresenter viewport = new() { Margin = new(24, 0, 24, 8), Visibility = Visibility.Collapsed };
    readonly List<bool> fillsViewport = [];
    public TextBlock SaveStatus { get; } = new() { Name = "SettingsSaveStatus", FontSize = 12, TextWrapping = TextWrapping.Wrap,
        Foreground = FluentDesign.Secondary, VerticalAlignment = VerticalAlignment.Center };
    readonly List<UIElement> pages = [];
    readonly List<UIElement?> headers = [];
    readonly List<NavigationViewItem> items = [];
    readonly DispatcherTimer searchDelay = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly List<SettingsSearch.Entry> matches = [];
    public TextBox SearchBox { get; } = new()
    {
        PlaceholderText = UiText.Resolve("Rechercher un paramètre…", "Search settings…"),
        HorizontalAlignment = HorizontalAlignment.Stretch, MaxLength = 160
    };
    public int SelectedIndex
    {
        get => navigation.SelectedItem is NavigationViewItem item ? items.IndexOf(item) : -1;
        set
        {
            if (value < 0 || value >= pages.Count) return;
            if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = "";
            navigation.SelectedItem = items[value]; ShowSelected();
        }
    }
    public SettingsNavigation()
    {
        var content = new Grid();
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var pageHeader = new SettingsHeaderPanel { Margin = new(24, 20, 24, 16), Spacing = 16, RightAlignLast = true };
        pageHeader.Children.Add(heading); pageHeader.Children.Add(header); content.Children.Add(pageHeader);
        SetRow(body, 1); content.Children.Add(body);
        SetRow(viewport, 1); content.Children.Add(viewport);
        navigation.Content = content; Children.Add(navigation);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SearchBox,
            UiText.Resolve("Rechercher un paramètre", "Search settings"));
        navigation.SelectionChanged += (_, _) =>
        {
            if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = "";
            ShowSelected();
        };
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); Search(); };
        SearchBox.TextChanged += (_, _) =>
        {
            searchDelay.Stop();
            if (string.IsNullOrWhiteSpace(SearchBox.Text)) ShowSelected(); else searchDelay.Start();
        };
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            { SearchBox.Text = ""; e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Enter && !string.IsNullOrWhiteSpace(SearchBox.Text))
            { searchDelay.Stop(); Search(); if (matches.FirstOrDefault() is { } match) Open(match); e.Handled = true; }
        };
        Unloaded += (_, _) => searchDelay.Stop();
        SizeChanged += (_, e) =>
        {
            var compact = e.NewSize.Width < 660;
            navigation.PaneDisplayMode = compact ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.Left;
            navigation.IsPaneOpen = !compact;
            navigation.IsPaneToggleButtonVisible = compact;
        };
    }
    void ShowSelected()
    {
        if (SelectedIndex < 0) return;
        heading.Text = items[SelectedIndex].Content?.ToString() ?? "";
        header.Content = headers[SelectedIndex];
        header.Visibility = header.Content == null ? Visibility.Collapsed : Visibility.Visible;
        body.Content = null; viewport.Content = null;
        body.Visibility = fillsViewport[SelectedIndex] ? Visibility.Collapsed : Visibility.Visible;
        viewport.Visibility = fillsViewport[SelectedIndex] ? Visibility.Visible : Visibility.Collapsed;
        if (fillsViewport[SelectedIndex]) viewport.Content = pages[SelectedIndex]; else body.Content = pages[SelectedIndex];
        ScrollToTop();
    }

    void Search()
    {
        var query = SearchBox.Text.Trim();
        if (query.Length == 0) { ShowSelected(); return; }
        matches.Clear();
        for (var i = 0; i < pages.Count; i++)
            matches.AddRange(SettingsSearch.Entries(pages[i], i, items[i].Content?.ToString() ?? "")
                .Where(entry => SettingsSearch.Matches(entry, query)));
        heading.Text = UiText.Resolve("Résultats de recherche", "Search results") + " (" + matches.Count + ")";
        header.Content = null; header.Visibility = Visibility.Collapsed;
        var results = new StackPanel { Spacing = 10 };
        if (matches.Count == 0) results.Children.Add(new TextBlock
        {
            Text = UiText.Resolve("Aucun paramètre trouvé. Essayez un autre mot.", "No settings found. Try another word."),
            TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary
        });
        foreach (var entry in matches)
        {
            var copy = new StackPanel { Spacing = 4 };
            copy.Children.Add(new TextBlock { Text = entry.Section, FontSize = 12, Foreground = FluentDesign.Secondary });
            copy.Children.Add(new TextBlock { Text = entry.Title, TextWrapping = TextWrapping.Wrap,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            if (entry.Description.Length > 0) copy.Children.Add(new TextBlock
            { Text = entry.Description, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = FluentDesign.Secondary });
            var result = new Button { Content = copy, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(16, 12, 16, 12) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(result, entry.Section + " · " + entry.Title);
            result.Click += (_, _) => Open(entry); results.Children.Add(result);
        }
        viewport.Content = null; viewport.Visibility = Visibility.Collapsed; body.Visibility = Visibility.Visible;
        body.Content = results; ScrollToTop();
    }

    void Open(SettingsSearch.Entry entry)
    {
        searchDelay.Stop(); SelectedIndex = entry.Page;
        foreach (var expander in entry.Expanders) expander.IsExpanded = true;
        if (pages[entry.Page] is FrameworkElement providerPage)
        {
            // Hidden provider tabs must be opened before focusing a search result.
            for (DependencyObject? current = entry.Target; current != null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
                if (current is ProviderConnectionLayout layout)
                {
                    if (IsInside(entry.Target, layout.Models)) layout.ShowPage(1);
                    else if (IsInside(entry.Target, layout.Advanced)) layout.ShowPage(2);
                    else layout.ShowPage(0);
                    break;
                }
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            body.UpdateLayout();
            var target = entry.Target;
            Control? focus = entry.Control;
            // Conditional skill details keep their enabled state; reveal their parent setting.
            for (FrameworkElement? ancestor = target; ancestor != null; ancestor = ancestor.Parent as FrameworkElement)
                if (ancestor.Visibility == Visibility.Collapsed)
                { target = ancestor.Parent as FrameworkElement ?? target; focus = null; }
            target.StartBringIntoView(); focus?.Focus(FocusState.Programmatic);
        });
    }

    static bool IsInside(FrameworkElement target, DependencyObject parent)
    {
        for (DependencyObject? item = target; item != null; item = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(item)) if (ReferenceEquals(item, parent)) return true;
        return false;
    }

    public void ScrollToTop() => body.ChangeView(null, 0, null, true);

    public int Add(string title,UIElement page, string? icon = null, bool footer = false, UIElement? fixedHeader = null, bool fillViewport = false)
    {
        string[] icons = ["\uE713", "\uE968", "\uE945", "\uE8F1", "\uE8D4", "\uE8A5", "\uE72E", "\uE774"];
        var item = new NavigationViewItem { Content = title, Icon = FluentDesign.Icon(icon ?? icons[Math.Min(pages.Count, icons.Length - 1)]) };
        items.Add(item); pages.Add(page); headers.Add(fixedHeader); fillsViewport.Add(fillViewport);
        if (footer) navigation.FooterMenuItems.Add(item); else navigation.MenuItems.Add(item);
        if (pages.Count == 1) SelectedIndex = 0;
        return pages.Count - 1;
    }
}
