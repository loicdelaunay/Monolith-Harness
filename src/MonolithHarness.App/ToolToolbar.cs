using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    Button ToolAction(string glyph, string label, Func<Task> action, bool idle = false)
    {
        var button = Action(label, action, idle);
        FluentDesign.IconButton(button, glyph, label, false);
        button.Width = button.Height = 32; button.MinWidth = button.MinHeight = 0; button.Padding = new(0);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        return button;
    }
    static Grid ToolToolbar(UIElement information, params UIElement[] actions)
    {
        var bar = new Grid { ColumnSpacing = 6, MinHeight = 34 };
        bar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        if (information is FrameworkElement info) info.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(information);
        var controls = Row(actions); controls.Spacing = 4; controls.VerticalAlignment = VerticalAlignment.Center;
        foreach (var action in actions.OfType<FrameworkElement>()) action.VerticalAlignment = VerticalAlignment.Center;
        var scroll = new ScrollViewer { Content = controls, VerticalAlignment = VerticalAlignment.Center,
            HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        // Keep the information readable and actions on one row, including narrow panels.
        bar.SizeChanged += (_, e) => scroll.MaxWidth = Math.Max(96, e.NewSize.Width * .65);
        Grid.SetColumn(scroll, 1); bar.Children.Add(scroll);
        return bar;
    }
}
