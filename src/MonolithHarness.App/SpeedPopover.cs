using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly SpeedPopover speedPopover = new();

    void AttachSpeedPopover(StackPanel anchor)
    {
        anchor.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        AttachHoverPopover(anchor, speedPopover, new Flyout(), () => RefreshSpeedPopover(currentSpeedTracker));
    }

    void RefreshSpeedPopover(GenerationSpeedTracker? activeTracker = null)
    {
        var replies = chat == null ? [] : VisibleHistory()
            .Where(x => x.ChatId == chat.Id && x.Role == "assistant" && x.State == "complete"
                && (x.OutputTokens ?? 0) > 0 && x.Seconds > 0).ToList();
        double? average = null, minimum = null, maximum = null;
        if (activeTracker != null)
        {
            if (activeTracker.AverageSpeed > 0)
            {
                average = activeTracker.AverageSpeed;
                minimum = activeTracker.MinSpeed ?? average;
                maximum = activeTracker.MaxSpeed ?? average;
            }
        }
        else if (replies.Count > 0)
        {
            var last = replies[^1];
            average = last.OutputTokens!.Value / Math.Max(.1, last.Seconds);
            messageTrackers.TryGetValue(last.Id, out var tracker);
            // Historical averages are persisted; instantaneous extrema are session-only.
            minimum = tracker?.MinSpeed;
            maximum = tracker?.MaxSpeed;
        }
        var discussion = activeTracker != null || replies.Count > 1
            ? SpeedStats.Compute(replies.Select(x => (x.OutputTokens!.Value, x.Seconds))) : null;
        var data = new SpeedPopoverData(activeTracker != null,
            activeTracker != null && ActiveRun?.Update?.OutputTokens == null,
            average, minimum, maximum, replies.Count, discussion);
        speedPopover.Update(data, WorkflowText, root.ActualWidth, root.ActualHeight, TextZoom.ForWindow(root));
    }

    readonly record struct SpeedPopoverData(bool Live, bool Estimated, double? Average, double? Minimum,
        double? Maximum, int ReplyCount, (double Min, double Max, double Avg)? Discussion);

    sealed class SpeedPopover : Grid
    {
        readonly TextBlock heading = Text(18, bold: true);
        readonly TextBlock intro = Text(12, secondary: true);
        readonly TextBlock source = Text(11);
        readonly TextBlock averageTitle = Text(12, secondary: true);
        readonly TextBlock averageValue = Text(28, bold: true);
        readonly TextBlock emptyNote = Text(12, secondary: true);
        readonly TextBlock note = Text(12, secondary: true);
        readonly Border icon;
        readonly ScrollViewer scroll;
        readonly StatsCard response = new();
        readonly StatsCard discussion = new();

        public SpeedPopover()
        {
            Width = 400; RowSpacing = 14;
            RowDefinitions.Add(new() { Height = GridLength.Auto });
            RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
            Children.Add(heading);
            var details = new StackPanel { Spacing = 16 };
            details.Children.Add(intro);
            var overview = new Grid { ColumnSpacing = 14 };
            overview.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            overview.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var glyph = FluentDesign.Icon("\uE9D9", 30);
            glyph.Foreground = FluentDesign.Resource("AccentTextFillColorPrimaryBrush");
            icon = new Border { Child = glyph, Width = 64, Height = 64, CornerRadius = new(12),
                Background = FluentDesign.Resource("ControlSelectedBrush"), VerticalAlignment = VerticalAlignment.Center };
            overview.Children.Add(icon);
            var totals = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            totals.Children.Add(new Border { Child = source, Padding = new(7, 3, 7, 3), CornerRadius = new(5),
                Background = FluentDesign.Resource("ControlSelectedBrush"), HorizontalAlignment = HorizontalAlignment.Left });
            totals.Children.Add(averageTitle);
            averageValue.Foreground = FluentDesign.Resource("AccentTextFillColorPrimaryBrush");
            totals.Children.Add(averageValue);
            totals.Children.Add(emptyNote);
            Grid.SetColumn(totals, 1); overview.Children.Add(totals); details.Children.Add(overview);
            details.Children.Add(response);
            details.Children.Add(discussion);
            details.Children.Add(note);
            scroll = new ScrollViewer { Content = details, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroll, 1); Children.Add(scroll);
            TextZoom.Observe(this);
        }

        public void Update(SpeedPopoverData data, Func<string, string, string> text, double windowWidth,
            double windowHeight, int zoom)
        {
            var scale = zoom / 100d;
            Width = Math.Max(180, Math.Min(400 * scale, windowWidth - 64));
            scroll.MaxHeight = Math.Max(100, Math.Min(520 * scale, windowHeight - 150 * scale));
            var narrow = Width < 330 * scale;
            icon.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            heading.Text = text("Débit de génération", "Generation speed");
            intro.Text = text("Vitesse à laquelle le modèle génère ses tokens.", "How quickly the model generates tokens.");
            source.Text = data.Live
                ? data.Estimated ? text("En cours · estimation", "Live · estimate") : text("En cours", "Live")
                : data.Average.HasValue ? text("Dernière réponse", "Last response") : text("En attente", "Waiting");
            averageTitle.Text = text("Débit moyen", "Average speed");
            var approximation = data.Estimated && data.Average.HasValue ? "≈ " : "";
            averageValue.Text = approximation + Format(data.Average) + " tok/s";
            emptyNote.Text = data.Live ? text("En attente des premiers tokens…", "Waiting for the first tokens…")
                : text("Les statistiques apparaîtront après une réponse.", "Statistics will appear after a response.");
            emptyNote.Visibility = data.Average.HasValue ? Visibility.Collapsed : Visibility.Visible;
            response.Update(data.Live ? text("Réponse en cours · tok/s", "Current response · tok/s")
                : data.Average.HasValue ? text("Dernière réponse · tok/s", "Last response · tok/s")
                : text("Réponse · tok/s", "Response · tok/s"),
                data.Minimum, data.Average, data.Maximum, narrow, text);
            discussion.Visibility = data.Discussion.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (data.Discussion is { } stats)
            {
                var count = data.ReplyCount;
                var replies = count == 1 ? text("réponse", "reply") : text("réponses", "replies");
                discussion.Update(text("Discussion", "Conversation") + $" · {count} {replies} · tok/s",
                    stats.Min, stats.Avg, stats.Max, narrow, text);
                ToolTipService.SetToolTip(discussion, text(
                    "Minimum et maximum des débits moyens par réponse terminée. La moyenne globale est pondérée par la durée des réponses.",
                    "Minimum and maximum of each completed reply’s average speed. The overall average is weighted by reply duration."));
            }
            note.Text = text("Les mesures en cours peuvent être estimées. Les extrema de la dernière réponse ne sont pas conservés après redémarrage.",
                "Live measurements may be estimated. Last-response extrema are not retained after restart.");
            TextZoom.SetWindow(this, zoom);
        }

        static string Format(double? value) => value?.ToString("F1") ?? "—";
        static TextBlock Text(double size = 13, bool secondary = false, bool bold = false) => new()
        {
            FontSize = size, Foreground = secondary ? FluentDesign.Secondary : FluentDesign.Primary,
            FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap
        };

        sealed class StatsCard : UserControl
        {
            readonly TextBlock title = Text(13, bold: true);
            readonly Grid metrics = new() { ColumnSpacing = 12, RowSpacing = 8 };
            readonly Grid[] cells = new Grid[3];
            readonly TextBlock[] labels = new TextBlock[3];
            readonly TextBlock[] values = new TextBlock[3];

            public StatsCard()
            {
                var content = new StackPanel { Spacing = 12 };
                content.Children.Add(title); content.Children.Add(metrics);
                Content = new Border { Child = content, Padding = new(12), CornerRadius = new(8), BorderThickness = new(1),
                    Background = FluentDesign.Card, BorderBrush = FluentDesign.Stroke };
                for (var i = 0; i < 3; i++)
                {
                    metrics.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                    metrics.RowDefinitions.Add(new() { Height = GridLength.Auto });
                    var cell = cells[i] = new Grid { RowSpacing = 4, ColumnSpacing = 8 };
                    cell.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                    cell.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    cell.RowDefinitions.Add(new() { Height = GridLength.Auto });
                    cell.RowDefinitions.Add(new() { Height = GridLength.Auto });
                    labels[i] = Text(12, secondary: true); values[i] = Text(16, bold: i == 1);
                    if (i == 1) values[i].Foreground = FluentDesign.Resource("AccentTextFillColorPrimaryBrush");
                    cell.Children.Add(labels[i]); cell.Children.Add(values[i]); metrics.Children.Add(cell);
                }
            }

            public void Update(string heading, double? minimum, double? average, double? maximum, bool narrow,
                Func<string, string, string> text)
            {
                title.Text = heading;
                metrics.RowSpacing = narrow ? 8 : 0;
                labels[0].Text = text("Minimum", "Minimum");
                labels[1].Text = text("Moyenne", "Average");
                labels[2].Text = text("Maximum", "Maximum");
                values[0].Text = Format(minimum); values[1].Text = Format(average); values[2].Text = Format(maximum);
                for (var i = 0; i < 3; i++)
                {
                    Grid.SetColumn(cells[i], narrow ? 0 : i); Grid.SetRow(cells[i], narrow ? i : 0);
                    Grid.SetColumnSpan(cells[i], narrow ? 3 : 1);
                    Grid.SetColumnSpan(labels[i], narrow ? 1 : 2);
                    Grid.SetColumnSpan(values[i], narrow ? 1 : 2);
                    Grid.SetColumn(values[i], narrow ? 1 : 0); Grid.SetRow(values[i], narrow ? 0 : 1);
                    values[i].HorizontalAlignment = narrow ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                }
            }
        }
    }
}
