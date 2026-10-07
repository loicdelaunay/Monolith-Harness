using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly StackPanel composerAttachments = new() { Spacing = 2, Visibility = Visibility.Collapsed };
    readonly Button composerAttachmentsToggle = new() { Padding = new(6, 2, 6, 2), MinHeight = 24, HorizontalAlignment = HorizontalAlignment.Left };
    readonly Dictionary<int, bool> collapsedAttachments = [];
    FrameworkElement BuildComposerAttachments()
    {
        assetsScroll.Margin = new(8, 0, 8, 4);
        composerAttachmentsToggle.Margin = new(8, 2, 8, 0);
        composerAttachmentsToggle.Click += (_, _) =>
        {
            if (chat == null) return;
            collapsedAttachments[chat.Id] = !collapsedAttachments.GetValueOrDefault(chat.Id);
            RefreshComposerAttachments();
        };
        composerAttachments.Children.Add(composerAttachmentsToggle); composerAttachments.Children.Add(assetsScroll);
        RefreshComposerAttachments(); return composerAttachments;
    }
    void RefreshComposerAttachments()
    {
        var count = assetsBar.Children.Count;
        var collapsed = chat != null && collapsedAttachments.GetValueOrDefault(chat.Id);
        composerAttachments.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        assetsScroll.Visibility = count > 0 && !collapsed ? Visibility.Visible : Visibility.Collapsed;
        var label = WorkflowText("Pièces jointes et ressources", "Attachments and resources") + $" ({count})";
        composerAttachmentsToggle.Content = (collapsed ? "▸ " : "▾ ") + label;
        ToolTipService.SetToolTip(composerAttachmentsToggle, WorkflowText(collapsed ? "Afficher les pièces jointes" : "Replier les pièces jointes", collapsed ? "Show attachments" : "Collapse attachments"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(composerAttachmentsToggle, label + " · " + WorkflowText(collapsed ? "Repliées" : "Dépliées", collapsed ? "Collapsed" : "Expanded"));
    }
}
