using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using Windows.Foundation;

namespace MonolithHarness.App;

/// <summary>Fits the application name without truncation, using the active UI font.</summary>
sealed class AdaptiveApplicationTitle : Panel
{
    readonly TextBlock name = new() { FontSize = 20, TextTrimming = TextTrimming.None, Foreground = FluentDesign.Primary };
    readonly TextBlock subtitle = new() { Text = "Harness", FontSize = 12, TextTrimming = TextTrimming.None, Foreground = FluentDesign.Secondary, Visibility = Visibility.Collapsed };
    readonly TextBlock measure = new() { TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.None };
    string text = BrandingAssets.DefaultName;
    bool twoLines = true;
    double scale = 1;
    double SubtitleTop => Math.Max(0, name.DesiredSize.Height - 4 * scale);

    public AdaptiveApplicationTitle()
    {
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(name); Children.Add(subtitle);
        name.RegisterPropertyChangedCallback(TextBlock.FontFamilyProperty, (_, _) => InvalidateMeasure());
        name.RegisterPropertyChangedCallback(TextBlock.FontWeightProperty, (_, _) => InvalidateMeasure());
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, text);
    }

    public string Text
    {
        get => text;
        set
        {
            if (text == value) return;
            text = value;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, value);
            InvalidateMeasure();
        }
    }

    public bool TwoLines
    {
        get => twoLines;
        set
        {
            if (twoLines == value) return;
            twoLines = value; InvalidateMeasure();
        }
    }

    // TextZoom delegates this heading to its adaptive layout so it cannot undo
    // a fitted font size during its next typography pass.
    public void SetScale(double value)
    {
        if (Math.Abs(scale - value) < .001) return;
        scale = value; InvalidateMeasure();
    }

    double WidthOf(string value, double fontSize)
    {
        measure.Text = value; measure.FontSize = fontSize;
        measure.FontFamily = name.FontFamily; measure.FontWeight = name.FontWeight;
        measure.FontStyle = name.FontStyle; measure.FontStretch = name.FontStretch;
        measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return measure.DesiredSize.Width;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : double.PositiveInfinity;
        if (width <= 0) return new Size(0, 0);
        var fittingWidth = double.IsFinite(width) ? Math.Max(0, width - 2) : width;
        var preferred = 20 * scale;
        var natural = WidthOf(text, preferred);
        var fitted = natural > fittingWidth ? preferred * fittingWidth / Math.Max(1, natural) : preferred;
        var parts = text.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool stacked = twoLines && parts.Length == 2;
        bool singleLine = !stacked && fitted >= 12 * scale && WidthOf(text, fitted) <= fittingWidth;

        name.Text = stacked ? parts[0] : text;
        name.TextWrapping = stacked || singleLine ? TextWrapping.NoWrap : TextWrapping.Wrap;
        if (stacked)
        {
            var titleWidth = WidthOf(parts[0], preferred);
            name.FontSize = titleWidth <= fittingWidth ? preferred : Math.Max(1, preferred * fittingWidth / Math.Max(1, titleWidth));
        }
        else name.FontSize = singleLine ? fitted : 18 * scale;
        subtitle.Text = stacked ? parts[1] : "";
        var subtitleSize = 12 * scale;
        var subtitleWidth = stacked ? WidthOf(parts[1], subtitleSize) : 0;
        subtitle.FontSize = subtitleWidth <= fittingWidth ? subtitleSize : Math.Max(1, subtitleSize * fittingWidth / Math.Max(1, subtitleWidth));
        subtitle.TextWrapping = TextWrapping.NoWrap;
        subtitle.Visibility = stacked ? Visibility.Visible : Visibility.Collapsed;

        name.Measure(new Size(width, double.PositiveInfinity));
        if (stacked) subtitle.Measure(new Size(width, double.PositiveInfinity));
        return new Size(Math.Min(width, Math.Max(name.DesiredSize.Width, stacked ? subtitle.DesiredSize.Width : 0)),
            stacked ? SubtitleTop + subtitle.DesiredSize.Height : name.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        name.Arrange(new Rect(0, 0, finalSize.Width, name.DesiredSize.Height));
        if (subtitle.Visibility == Visibility.Visible)
            subtitle.Arrange(new Rect(0, SubtitleTop, finalSize.Width, subtitle.DesiredSize.Height));
        return finalSize;
    }
}
