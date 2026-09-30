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
    { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(24, 0, 24, 24) };
    readonly TextBlock heading = new() { FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new(24, 20, 24, 20) };
    readonly List<UIElement> pages = [];
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
        content.Children.Add(heading); SetRow(body, 1); content.Children.Add(body);
        navigation.Content = content; Children.Add(navigation);
        navigation.SelectionChanged += (_, _) =>
        {
            if (SelectedIndex < 0) return;
            heading.Text = ((NavigationViewItem)navigation.SelectedItem).Content?.ToString() ?? "";
            body.Content = pages[SelectedIndex]; body.ChangeView(null, 0, null);
        };
        SizeChanged += (_, e) =>
        {
            var compact = e.NewSize.Width < 660;
            navigation.PaneDisplayMode = compact ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.Left;
            navigation.IsPaneOpen = !compact;
            navigation.IsPaneToggleButtonVisible = compact;
        };
    }
    public int Add(string title,UIElement page, string? icon = null, bool footer = false)
    {
        string[] icons = ["\uE713", "\uE968", "\uE945", "\uE8F1", "\uE8D4", "\uE8A5", "\uE72E", "\uE774"];
        var item = new NavigationViewItem { Content = title, Icon = FluentDesign.Icon(icon ?? icons[Math.Min(pages.Count, icons.Length - 1)]) };
        items.Add(item); pages.Add(page);
        if (footer) navigation.FooterMenuItems.Add(item); else navigation.MenuItems.Add(item);
        if (pages.Count == 1) SelectedIndex = 0;
        return pages.Count - 1;
    }
}
