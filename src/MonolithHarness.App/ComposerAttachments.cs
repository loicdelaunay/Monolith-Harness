using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly Grid composerAttachments = new() { ColumnSpacing = 6, Margin = new(8, 2, 8, 4), Visibility = Visibility.Collapsed };
    readonly Button composerAttachmentsToggle = new() { Padding = new(0), Width = 24, Height = 24, MinWidth = 0, MinHeight = 0,
        BorderThickness = new(0), CornerRadius = new(6), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    readonly Dictionary<int, bool> collapsedAttachments = [];
    FrameworkElement BuildComposerAttachments()
    {
        assetsScroll.Margin = new(0);
        composerAttachments.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        composerAttachments.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        composerAttachmentsToggle.Click += (_, _) =>
        {
            if (chat == null) return;
            collapsedAttachments[chat.Id] = !collapsedAttachments.GetValueOrDefault(chat.Id);
            RefreshComposerAttachments();
        };
        composerAttachments.Children.Add(composerAttachmentsToggle);
        Grid.SetColumn(assetsScroll, 1); composerAttachments.Children.Add(assetsScroll);
        RefreshComposerAttachments(); return composerAttachments;
    }
    void RefreshComposerAttachments()
    {
        var count = assetsBar.Children.Count;
        var collapsed = chat != null && collapsedAttachments.GetValueOrDefault(chat.Id);
        composerAttachments.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        assetsScroll.Visibility = count > 0 && !collapsed ? Visibility.Visible : Visibility.Collapsed;
        composerAttachmentsToggle.Content = collapsed ? "▸" : "▾";
        var label = WorkflowText(collapsed ? "Afficher les pièces jointes" : "Replier les pièces jointes", collapsed ? "Show attachments" : "Collapse attachments") + $" ({count})";
        ToolTipService.SetToolTip(composerAttachmentsToggle, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(composerAttachmentsToggle, label);
    }
}
