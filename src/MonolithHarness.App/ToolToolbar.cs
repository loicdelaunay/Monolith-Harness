using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    static void StyleChromeAction(Button button)
    {
        button.Width = button.Height = 28; button.MinWidth = button.MinHeight = 0;
        button.Padding = new(0); button.CornerRadius = new(6); button.BorderThickness = new(0);
        button.Background = FluentDesign.Resource("TransparentBrush"); button.Foreground = FluentDesign.Secondary;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center; button.VerticalAlignment = VerticalAlignment.Center;
    }
    Button ToolAction(string glyph, string label, Func<Task> action, bool idle = false)
    {
        var button = Action(label, action, idle);
        FluentDesign.IconButton(button, glyph, label, false); StyleChromeAction(button);
        return button;
    }
    static void CompactToolTabs(TabView tabs)
    {
        tabs.MinHeight = 0; tabs.Padding = new(0);
        tabs.Resources["TabViewItemHeaderBackground"] = FluentDesign.Resource("TransparentBrush");
        tabs.Resources["TabViewItemHeaderBackgroundSelected"] = FluentDesign.Resource("ControlPressedBrush");
        tabs.Resources["TabViewItemHeaderForeground"] = FluentDesign.Secondary;
        tabs.Resources["TabViewItemHeaderForegroundSelected"] = FluentDesign.Primary;
        tabs.Resources["TabViewItemHeaderBackgroundPointerOver"] = FluentDesign.Resource("ControlHoverBrush");
        tabs.Resources["TabViewItemHeaderPadding"] = new Thickness(10, 4, 8, 4);
        tabs.Resources["TabViewItemMinWidth"] = 80d;
        tabs.Resources["TabViewItemMaxWidth"] = 190d;
        tabs.Resources["TabViewItemHeaderMinHeight"] = 30d;
    }
    static Grid ToolToolbar(UIElement information, params UIElement[] actions)
    {
        var bar = new Grid { ColumnSpacing = 8, MinHeight = 40, Padding = new(10, 5, 8, 5),
            BorderBrush = FluentDesign.Stroke, BorderThickness = new(0, 0, 0, 1) };
        bar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        if (information is FrameworkElement info) info.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(information);
        var controls = Row(actions); controls.Spacing = 3; controls.VerticalAlignment = VerticalAlignment.Center;
        foreach (var action in actions.OfType<FrameworkElement>())
        {
            action.VerticalAlignment = VerticalAlignment.Center;
            if (action is Control control) control.CornerRadius = new(6);
        }
        var scroll = new ScrollViewer { Content = controls, VerticalAlignment = VerticalAlignment.Center,
            HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        // Preserve a readable title while the one-row action strip scrolls in narrow panels.
        bar.SizeChanged += (_, e) => scroll.MaxWidth = Math.Max(56, (e.NewSize.Width - 28) * .72);
        Grid.SetColumn(scroll, 1); bar.Children.Add(scroll);
        return bar;
    }
}
