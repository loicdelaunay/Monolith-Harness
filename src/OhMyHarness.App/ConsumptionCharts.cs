using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;

namespace OhMyHarness.App;

sealed class ConsumptionTimelineChart : Grid
{
    internal static Brush InputBrush => FluentDesign.Resource("AccentFillColorDefaultBrush");
    internal static Brush OutputBrush => FluentDesign.Resource("ToolMessageTitleBrush");
    readonly Canvas plot = new();
    readonly Canvas axis = new();
    IReadOnlyList<TokenBucket> values = [];

    public ConsumptionTimelineChart()
    {
        Height = 230; ColumnSpacing = 8;
        ColumnDefinitions.Add(new() { Width = new(56) });
        ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Children.Add(axis); Grid.SetColumn(plot, 1); Children.Add(plot);
        plot.SizeChanged += (_, _) => Draw();
    }

    internal void SetValues(IReadOnlyList<TokenBucket> buckets) { values = buckets; Draw(); }
    static TextBlock Label(string text) => new() { Text = text, FontSize = 11, Foreground = FluentDesign.Secondary };
    static string Compact(double number) => number >= 1_000_000 ? $"{number / 1_000_000:0.#} M" : number >= 1000 ? $"{number / 1000:0.#} k" : $"{number:0}";

    void Draw()
    {
        var width = plot.ActualWidth; var height = plot.ActualHeight - 36;
        if (width <= 0 || height <= 0) return;
        plot.Children.Clear(); axis.Children.Clear();
        var maximum = Math.Max(1, values.Count == 0 ? 0 : values.Max(x => x.Total));
        for (int i = 0; i <= 4; i++)
        {
            var y = height * i / 4;
            var line = new Border { Width = width, Height = 1, Background = FluentDesign.Stroke };
            Canvas.SetTop(line, y); plot.Children.Add(line);
            var label = Label(Compact(maximum * (4 - i) / 4d));
            Canvas.SetTop(label, Math.Max(0, y - 8)); axis.Children.Add(label);
        }
        if (values.Count == 0) return;
        var step = width / values.Count;
        var cadence = Math.Max(1, (int)Math.Ceiling(values.Count * 66d / width));
        for (int i = 0; i < values.Count; i++)
        {
            var value = values[i];
            var outputHeight = height * value.Output / maximum;
            var inputHeight = height * value.Input / maximum;
            var bar = new StackPanel { Width = Math.Max(1, step - 4), Background = FluentDesign.Resource("TransparentBrush") };
            bar.Children.Add(new Border { Height = outputHeight, Background = OutputBrush });
            bar.Children.Add(new Border { Height = inputHeight, Background = InputBrush });
            var description = value.Label + $"\n{UiText.Resolve("Entrée", "Input")}: {value.Input:N0}\n{UiText.Resolve("Sortie", "Output")}: {value.Output:N0}\n" +
                $"{UiText.Resolve("Total", "Total")}: {value.Total:N0} tokens · {value.Calls:N0} {UiText.Resolve("appels", "calls")}";
            AutomationProperties.SetName(bar, description); ToolTipService.SetToolTip(bar, description);
            Canvas.SetLeft(bar, step * i + 2); Canvas.SetTop(bar, height - inputHeight - outputHeight); plot.Children.Add(bar);
            if (i % cadence != 0) continue;
            var label = Label(value.Label); label.Width = Math.Max(20, step * cadence - 4); label.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTipService.SetToolTip(label, value.Label);
            Canvas.SetLeft(label, step * i); Canvas.SetTop(label, height + 10); plot.Children.Add(label);
        }
    }
}
