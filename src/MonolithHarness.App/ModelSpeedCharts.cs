using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

sealed class ModelSpeedTimelineChart : Grid
{
    readonly Canvas plot = new(), axis = new();
    IReadOnlyList<ModelSpeedBucket> values = [];

    public ModelSpeedTimelineChart()
    {
        Height = 230; ColumnSpacing = 8;
        ColumnDefinitions.Add(new() { Width = new(56) }); ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Children.Add(axis); Grid.SetColumn(plot, 1); Children.Add(plot); plot.SizeChanged += (_, _) => Draw();
    }

    internal void SetValues(IReadOnlyList<ModelSpeedBucket> buckets) { values = buckets; Draw(); }
    static TextBlock Label(string text) => new() { Text = text, FontSize = 11, Foreground = FluentDesign.Secondary };
    static string Compact(double number) => number >= 1000 ? $"{number / 1000:0.#} k" : $"{number:0.#}";

    void Draw()
    {
        var width = plot.ActualWidth; var height = plot.ActualHeight - 36;
        if (width <= 0 || height <= 0) return;
        plot.Children.Clear(); axis.Children.Clear();
        var maximum = Math.Max(1, values.Count == 0 ? 0 : values.Max(x => x.Summary.Average ?? 0));
        for (int i = 0; i <= 4; i++)
        {
            var y = height * i / 4;
            var line = new Border { Width = width, Height = 1, Background = FluentDesign.Stroke }; Canvas.SetTop(line, y); plot.Children.Add(line);
            var label = Label(Compact(maximum * (4 - i) / 4d)); Canvas.SetTop(label, Math.Max(0, y - 8)); axis.Children.Add(label);
        }
        if (values.Count == 0) return;
        var step = width / values.Count;
        var cadence = Math.Max(1, (int)Math.Ceiling(values.Count * 66d / width));
        for (int i = 0; i < values.Count; i++)
        {
            var value = values[i]; var summary = value.Summary;
            var hit = new Grid { Width = Math.Max(1, step - 4), Height = height, Background = FluentDesign.Resource("TransparentBrush") };
            if (summary.Average.HasValue)
                hit.Children.Add(new Border { Height = height * summary.Average.Value / maximum,
                    Background = ConsumptionTimelineChart.InputBrush, VerticalAlignment = VerticalAlignment.Bottom });
            var approximate = summary.Estimates > 0 ? "≈ " : "";
            var description = value.Label + (summary.Average.HasValue
                ? $"\n{UiText.Resolve("Moyenne", "Average")}: {approximate}{summary.Average:N1} tok/s" +
                    $"\n{UiText.Resolve("Minimum", "Minimum")}: {approximate}{summary.Minimum:N1} · {UiText.Resolve("Maximum", "Maximum")}: {approximate}{summary.Maximum:N1} tok/s" +
                    $"\n{summary.Calls:N0} {UiText.Resolve("appels", "calls")}"
                : "\n" + UiText.Resolve("Aucune mesure", "No measurements"));
            AutomationProperties.SetName(hit, description); ToolTipService.SetToolTip(hit, description);
            Canvas.SetLeft(hit, step * i + 2); plot.Children.Add(hit);
            if (i % cadence != 0) continue;
            var label = Label(value.Label); label.Width = Math.Max(20, step * cadence - 4); label.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTipService.SetToolTip(label, value.Label); Canvas.SetLeft(label, step * i); Canvas.SetTop(label, height + 10); plot.Children.Add(label);
        }
    }
}
