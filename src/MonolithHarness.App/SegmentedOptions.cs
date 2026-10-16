using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MonolithHarness.App;

// Native toggle buttons retain focus, keyboard navigation and accessible checked states.
internal sealed class SegmentedOptions : UserControl
{
    readonly Grid grid = new();
    readonly List<ToggleButton> buttons = [];
    readonly bool solid;
    int selected = -1, columns;
    public event EventHandler? SelectionChanged;
    public int SelectedIndex
    {
        get => selected;
        set
        {
            if (value < -1 || value >= buttons.Count) return;
            var changed = selected != value; selected = value;
            for (var i = 0; i < buttons.Count; i++) buttons[i].IsChecked = i == selected;
            if (changed) SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public SegmentedOptions(bool solid = false)
    {
        this.solid = solid; Content = grid;
        grid.SizeChanged += (_, _) => Reflow();
        PreviewKeyDown += (_, e) =>
        {
            if (!IsEnabled || buttons.Count == 0) return;
            var next = e.Key switch
            {
                Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Up => Math.Max(0, selected - 1),
                Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Down => Math.Min(buttons.Count - 1, selected + 1),
                Windows.System.VirtualKey.Home => 0,
                Windows.System.VirtualKey.End => buttons.Count - 1,
                _ => -1
            };
            if (next < 0) return;
            SelectedIndex = next; buttons[next].Focus(FocusState.Keyboard); e.Handled = true;
        };
    }
    public void SetLabels(params string[] labels)
    {
        if (buttons.Count != labels.Length)
        {
            buttons.Clear(); grid.Children.Clear(); columns = 0;
            for (var i = 0; i < labels.Length; i++)
            {
                var index = i;
                var button = new ToggleButton { MinWidth = 0, MinHeight = 36, Padding = new(7, 5, 7, 5), FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
                    Background = FluentDesign.Card, Foreground = FluentDesign.Primary, BorderBrush = FluentDesign.Stroke, BorderThickness = new(1) };
                foreach (var state in new[] { "Checked", "CheckedPointerOver", "CheckedPressed" })
                {
                    button.Resources["ToggleButtonBackground" + state] = FluentDesign.Resource(solid ? "AccentFillColorDefaultBrush" : "ControlSelectedBrush");
                    button.Resources["ToggleButtonForeground" + state] = FluentDesign.Resource(solid ? "TextOnAccentFillColorPrimaryBrush" : "AccentTextFillColorPrimaryBrush");
                    button.Resources["ToggleButtonBorderBrush" + state] = FluentDesign.Resource("AccentFillColorDefaultBrush");
                }
                button.Click += (_, _) => SelectedIndex = index;
                button.RegisterPropertyChangedCallback(Control.FontSizeProperty, (_, _) => Reflow());
                buttons.Add(button); grid.Children.Add(button);
            }
        }
        for (var i = 0; i < labels.Length; i++)
        {
            buttons[i].Content = labels[i];
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(buttons[i], labels[i]);
            buttons[i].IsChecked = i == selected;
        }
        Reflow();
    }
    void Reflow()
    {
        if (buttons.Count == 0) return;
        var scale = Math.Max(1, buttons[0].FontSize / 13d);
        var next = grid.ActualWidth <= 0 ? buttons.Count : Math.Clamp((int)(grid.ActualWidth / (72 * scale)), 1, buttons.Count);
        if (next == columns) return;
        columns = next; grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var rows = (buttons.Count + columns - 1) / columns;
        for (var i = 0; i < rows; i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (var i = 0; i < buttons.Count; i++)
        {
            var row = i / columns; var col = i % columns;
            Grid.SetColumn(buttons[i], col); Grid.SetRow(buttons[i], row);
            buttons[i].CornerRadius = new(col == 0 ? 6 : 0, col == columns - 1 || i == buttons.Count - 1 ? 6 : 0,
                col == columns - 1 || i == buttons.Count - 1 ? 6 : 0, col == 0 ? 6 : 0);
            buttons[i].Margin = new(col == 0 ? 0 : -1, row == 0 ? 0 : 3, 0, 0);
        }
    }
}
