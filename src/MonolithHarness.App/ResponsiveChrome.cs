using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    Grid composerFooterHost = null!;
    Viewbox composerFooterViewport = null!;
    Grid composerFooterGrid = null!;
    FrameworkElement composerModeHost = null!;
    StackPanel composerActionButtons = null!;
    bool resizingComposerFooter;
    int composerResponsiveStage;
    bool compactChatHeader;
    Grid chatHeader = null!;
    StackPanel chatHeaderActions = null!;
    Button chatExportButton = null!, chatToolsButton = null!, chatNavigationButton = null!;
    readonly List<(ButtonBase Button, TextBlock Label)> chatHeaderLabels = [];

    static double NaturalWidth(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize.Width;
    }
    void LayoutComposerFooter()
    {
        if (resizingComposerFooter || composerFooterHost == null || composerFooterHost.ActualWidth <= 0) return;
        resizingComposerFooter = true;
        try
        {
            var width = composerFooterHost.ActualWidth;
            composerFooterViewport.Width = width;
            var scale = TextZoom.ForWindow(root) / 100d;
            var settings = FeatureSettings.Read(state.FeaturesJson);
            // Start from saved preferences on every resize; width-based hiding never changes them.
            composerSpeedIndicator.Visibility = settings.ShowComposerSpeed ? Visibility.Visible : Visibility.Collapsed;
            composerContextIndicator.Visibility = settings.ShowComposerContext ? Visibility.Visible : Visibility.Collapsed;
            composerContextText.Visibility = Visibility.Visible;
            composerMetrics.Visibility = settings.ShowComposerContext || settings.ShowComposerSpeed ? Visibility.Visible : Visibility.Collapsed;
            SetComposerModeCompact(false);
            composerFooterGrid.ColumnSpacing = 8 * scale;
            composerMetrics.Spacing = 8 * scale;
            modelOptionsButton.MaxWidth = 240 * scale;
            modelOptionsButton.Padding = new(8, 0, 8, 0);
            if (composerModeHost is Border normalMode) normalMode.Padding = new(8, 1, 3, 1);
            double Needed() => NaturalWidth(composerModeHost) + NaturalWidth(composerModelSummary) + NaturalWidth(composerActionButtons) + 2 * composerFooterGrid.ColumnSpacing;
            composerResponsiveStage = 0;
            if (Needed() > width + .5)
            {
                composerResponsiveStage = 1;
                composerSpeedIndicator.Visibility = Visibility.Collapsed;
            }
            if (Needed() > width + .5)
            {
                composerResponsiveStage = 2;
                composerContextText.Visibility = Visibility.Collapsed;
            }
            if (Needed() > width + .5)
            {
                composerResponsiveStage = 3;
                SetComposerModeCompact(true);
                composerFooterGrid.ColumnSpacing = 4 * scale; composerMetrics.Spacing = 4 * scale;
                modelOptionsButton.Padding = new(4, 0, 4, 0);
                if (composerModeHost is Border compactMode) compactMode.Padding = new(4, 1, 2, 1);
                var other = NaturalWidth(composerModeHost) + NaturalWidth(composerActionButtons) + NaturalWidth(composerMetrics) + 3 * composerFooterGrid.ColumnSpacing;
                modelOptionsButton.MaxWidth = Math.Clamp(width - other, 100 * scale, 200 * scale);
            }
            // The Viewbox only scales down if even compact controls cannot fit. Giving the
            // grid its natural minimum prevents the left column or model from being clipped.
            composerFooterGrid.Width = Math.Max(width, Needed());
            RefreshRecentComposerSpeed();
            var bottom = Math.Max(58 * scale, composerFooterViewport.ActualHeight + 20 * scale);
            composer.Padding = new(14 * scale, 12 * scale, 14 * scale, bottom);
            composer.MinHeight = Math.Max(124 * scale, bottom + 60 * scale);
        }
        finally { resizingComposerFooter = false; }
    }
    void BuildResponsiveHeader(Grid header, StackPanel actions, Button navigation, Button export, Button tools)
    {
        chatHeader = header; chatHeaderActions = actions; chatNavigationButton = navigation; chatExportButton = export; chatToolsButton = tools;
        void Add(ButtonBase button, string glyph, string label)
        {
            var caption = new TextBlock { Text = UiText.T(label), Tag = label, VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(FluentDesign.Icon(glyph)); content.Children.Add(caption);
            button.Content = content; button.Tag = null;
            AutomationProperties.SetName(button, UiText.T(label)); ToolTipService.SetToolTip(button, UiText.T(label));
            chatHeaderLabels.Add((button, caption));
        }
        Add(export, "\uE74E", "Exporter"); Add(tools, "\uE90F", "Outils"); Add(autoScrollButton, "\uE74B", "Auto");
        bool resizing = false;
        void Resize()
        {
            if (resizing || header.ActualWidth <= 0) return;
            resizing = true;
            try
            {
                var scale = TextZoom.ForWindow(root) / 100d;
                foreach (var (button, label) in chatHeaderLabels)
                {
                    label.Visibility = Visibility.Visible; button.Width = double.NaN;
                    if (button is Control control) { control.MinWidth = 0; control.MinHeight = 36; control.Padding = new(10, 6, 10, 6); }
                }
                actions.Spacing = 8; header.ColumnSpacing = 12;
                navigation.Width = double.NaN; navigation.Padding = new(10, 6, 10, 6);
                compactChatHeader = NaturalWidth(actions) + NaturalWidth(navigation) + 160 * scale + 24 > header.ActualWidth;
                foreach (var (button, label) in chatHeaderLabels)
                {
                    label.Visibility = compactChatHeader ? Visibility.Collapsed : Visibility.Visible;
                    button.Width = compactChatHeader ? 36 : double.NaN;
                    if (button is Control control) control.Padding = compactChatHeader ? new Thickness(0) : new Thickness(10, 6, 10, 6);
                }
                actions.Spacing = compactChatHeader ? 4 : 8;
                header.ColumnSpacing = compactChatHeader ? 6 : 12;
                navigation.MinHeight = 36;
                navigation.Width = compactChatHeader ? 36 : double.NaN;
                if (compactChatHeader) navigation.Padding = new(0);
                else navigation.Padding = new(10, 6, 10, 6);
            }
            finally { resizing = false; }
        }
        header.SizeChanged += (_, _) => Resize(); actions.SizeChanged += (_, _) => Resize();
        header.Loaded += (_, _) => Resize();
    }
}
