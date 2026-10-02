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
    readonly List<UIElement> pages = [];
    readonly List<UIElement?> headers = [];
    readonly List<NavigationViewItem> items = [];
    public int SelectedIndex
    {
        get => navigation.SelectedItem is NavigationViewItem item ? items.IndexOf(item) : -1;
        set { if (value >= 0 && value < pages.Count) navigation.SelectedItem = items[value]; }
    }
    public SettingsNavigation()
    {
        var content = new Grid();
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var pageHeader = new SettingsHeaderPanel { Margin = new(24, 20, 24, 16), Spacing = 16, RightAlignLast = true };
        pageHeader.Children.Add(heading); pageHeader.Children.Add(header); content.Children.Add(pageHeader);
        SetRow(body, 1); content.Children.Add(body);
        navigation.Content = content; Children.Add(navigation);
        navigation.SelectionChanged += (_, _) =>
        {
            if (SelectedIndex < 0) return;
            heading.Text = ((NavigationViewItem)navigation.SelectedItem).Content?.ToString() ?? "";
            header.Content = headers[SelectedIndex];
            header.Visibility = header.Content == null ? Visibility.Collapsed : Visibility.Visible;
            body.Content = pages[SelectedIndex]; ScrollToTop();
        };
        SizeChanged += (_, e) =>
        {
            var compact = e.NewSize.Width < 660;
            navigation.PaneDisplayMode = compact ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.Left;
            navigation.IsPaneOpen = !compact;
            navigation.IsPaneToggleButtonVisible = compact;
        };
    }
    public void ScrollToTop() => body.ChangeView(null, 0, null, true);

    public int Add(string title,UIElement page, string? icon = null, bool footer = false, UIElement? fixedHeader = null)
    {
        string[] icons = ["\uE713", "\uE968", "\uE945", "\uE8F1", "\uE8D4", "\uE8A5", "\uE72E", "\uE774"];
        var item = new NavigationViewItem { Content = title, Icon = FluentDesign.Icon(icon ?? icons[Math.Min(pages.Count, icons.Length - 1)]) };
        items.Add(item); pages.Add(page); headers.Add(fixedHeader);
        if (footer) navigation.FooterMenuItems.Add(item); else navigation.MenuItems.Add(item);
        if (pages.Count == 1) SelectedIndex = 0;
        return pages.Count - 1;
    }
}
