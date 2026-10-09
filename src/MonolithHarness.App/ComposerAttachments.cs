using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly Grid composerAttachments = new() { ColumnSpacing = 6, Margin = new(8, 2, 8, 4), Visibility = Visibility.Collapsed };
    readonly Button composerAttachmentsToggle = new() { Padding = new(0), Width = 24, Height = 24, MinWidth = 0, MinHeight = 0,
        BorderThickness = new(0), CornerRadius = new(6), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid composerAttachmentsToggleContent = new() { ColumnSpacing = 6 };
    readonly TextBlock composerAttachmentsArrow = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock composerAttachmentsLabel = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center, Foreground = FluentDesign.Secondary };
    readonly Dictionary<int, bool> collapsedAttachments = [];
    bool resizingComposerAttachments;
    FrameworkElement BuildComposerAttachments()
    {
        assetsScroll.Margin = new(0);
        composerAttachments.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        composerAttachments.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        composerAttachmentsToggleContent.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        composerAttachmentsToggleContent.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        composerAttachmentsToggleContent.Children.Add(composerAttachmentsArrow);
        Grid.SetColumn(composerAttachmentsLabel, 1); composerAttachmentsToggleContent.Children.Add(composerAttachmentsLabel);
        composerAttachmentsToggle.Content = composerAttachmentsToggleContent;
        composerAttachmentsToggle.Click += (_, _) =>
        {
            if (chat == null) return;
            collapsedAttachments[chat.Id] = !collapsedAttachments.GetValueOrDefault(chat.Id);
            RefreshComposerAttachments();
        };
        composerAttachments.Children.Add(composerAttachmentsToggle);
        Grid.SetColumn(assetsScroll, 1); composerAttachments.Children.Add(assetsScroll);
        composerAttachments.SizeChanged += (_, _) => LayoutComposerAttachments();
        root.SizeChanged += (_, _) => LayoutComposerAttachments();
        RefreshComposerAttachments(); return composerAttachments;
    }
    void LayoutComposerAttachments()
    {
        if (resizingComposerAttachments || composerAttachments.ActualWidth <= 0) return;
        resizingComposerAttachments = true;
        try
        {
            composerAttachmentsToggle.MaxWidth = composerAttachments.ActualWidth;
            // Keep the row small relative to both available width and window height.
            // Extra attachments scroll horizontally instead of creating additional rows.
            var width = Math.Max(0, composerAttachments.ActualWidth - 24 - composerAttachments.ColumnSpacing);
            var heightLimit = root.ActualHeight > 0 ? root.ActualHeight * .14 : 96;
            var size = Math.Clamp(Math.Min(width * .18, heightLimit), 48, 96);
            foreach (var tile in assetsBar.Children.OfType<ComposerResourceTile>()) tile.SetPreviewSize(size);
        }
        finally { resizingComposerAttachments = false; }
    }
    void RefreshComposerAttachments()
    {
        var count = assetsBar.Children.Count;
        var collapsed = chat != null && collapsedAttachments.GetValueOrDefault(chat.Id);
        composerAttachments.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        assetsScroll.Visibility = count > 0 && !collapsed ? Visibility.Visible : Visibility.Collapsed;
        composerAttachmentsArrow.Text = collapsed ? "▸" : "▾";
        composerAttachmentsLabel.Text = WorkflowText($"Voir pièces jointes ({count})", $"View attachments ({count})");
        composerAttachmentsLabel.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        composerAttachmentsToggleContent.ColumnSpacing = collapsed ? 6 : 0;
        composerAttachmentsToggle.Width = collapsed ? double.NaN : 24;
        composerAttachmentsToggle.Padding = collapsed ? new Thickness(4, 0, 6, 0) : new Thickness(0);
        composerAttachmentsToggle.HorizontalContentAlignment = collapsed ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        Grid.SetColumnSpan(composerAttachmentsToggle, collapsed ? 2 : 1);
        var label = WorkflowText(collapsed ? "Voir pièces jointes" : "Replier les pièces jointes", collapsed ? "View attachments" : "Collapse attachments") + $" ({count})";
        ToolTipService.SetToolTip(composerAttachmentsToggle, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(composerAttachmentsToggle, label);
        LayoutComposerAttachments();
    }
}
