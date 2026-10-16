using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MonolithHarness.App;

/// <summary>Switch pages without recreating native views or their conversation sessions.</summary>
internal sealed class ToolTabHost : Grid
{
    readonly Grid headerBar = new() { ColumnSpacing = 4, Padding = new(6, 3, 4, 3), MinHeight = 38,
        BorderBrush = FluentDesign.Stroke, BorderThickness = new(0, 0, 0, 1) };
    readonly Grid heading = new();
    readonly StackPanel caption = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly FontIcon icon = FluentDesign.Icon("\uE774", 14);
    readonly TextBlock label = new() { FontSize = 12, Foreground = FluentDesign.Secondary, VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    readonly List<(string Title, string Glyph, FrameworkElement Page, FrameworkElement? Header)> tabs = [];
    int selectedIndex;

    public event EventHandler? SelectionChanged;
    public int Count => tabs.Count;
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < 0 || value >= tabs.Count || value == selectedIndex) return;
            selectedIndex = value; UpdateSelection(); SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToolTabHost()
    {
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        headerBar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        headerBar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        caption.Children.Add(icon); caption.Children.Add(label); heading.Children.Add(caption);
        headerBar.Children.Add(heading); Children.Add(headerBar);
    }

    public void SetHeaderActions(FrameworkElement actions)
    {
        actions.VerticalAlignment = VerticalAlignment.Center; SetColumn(actions, 1); headerBar.Children.Add(actions);
    }

    public void AddTab(string title, FrameworkElement page, string glyph, FrameworkElement? header = null)
    {
        SetRow(page, 1); Children.Add(page);
        if (header != null) heading.Children.Add(header);
        tabs.Add((title, glyph, page, header)); UpdateSelection();
    }

    public void SetTitle(int index, string title)
    {
        if ((uint)index >= tabs.Count) return;
        var tab = tabs[index]; tabs[index] = (title, tab.Glyph, tab.Page, tab.Header); UpdateSelection();
    }

    void UpdateSelection()
    {
        for (var i = 0; i < tabs.Count; i++)
        {
            tabs[i].Page.Visibility = i == selectedIndex ? Visibility.Visible : Visibility.Collapsed;
            if (tabs[i].Header is { } custom) custom.Visibility = i == selectedIndex ? Visibility.Visible : Visibility.Collapsed;
        }
        if (tabs.Count == 0) return;
        var selected = tabs[selectedIndex];
        caption.Visibility = selected.Header == null ? Visibility.Visible : Visibility.Collapsed;
        icon.Glyph = selected.Glyph; icon.Foreground = FluentDesign.Secondary; label.Text = selected.Title;
    }
}
