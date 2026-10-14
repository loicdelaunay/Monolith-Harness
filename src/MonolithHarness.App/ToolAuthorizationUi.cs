using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly HashSet<ToolAuthorizationWindow> authorizationWindows = [];
    (Button Button, Action<ToolApprovalEvidence[]> Update) BuildToolAuthorizationBadge(string toolName)
    {
        var button = new Button { Width = 30, Height = 28, Padding = new(4), Visibility = Visibility.Collapsed,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0) };
        var icon = FluentDesign.Icon("\uEA18"); icon.FontSize = 16; button.Content = icon;
        ToolApprovalEvidence[] evidence = [];
        ToolAuthorizationWindow? window = null;
        void Update(ToolApprovalEvidence[] next)
        {
            evidence = next; button.Visibility = next.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            icon.Foreground = ToolAuthorizationWindow.Color(next);
            var summary = ToolAuthorizationWindow.Summary(next);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, WorkflowText("Voir la validation : ", "View authorization: ") + summary);
            ToolTipService.SetToolTip(button, summary); window?.Update(next);
        }
        button.Click += (_, _) =>
        {
            if (window != null) { window.Activate(); return; }
            window = new ToolAuthorizationWindow(toolName, evidence, root.RequestedTheme);
            var opened = window; authorizationWindows.Add(opened);
            opened.Closed += (_, _) => { authorizationWindows.Remove(opened); window = null; };
            ApplyBrandingIcon(opened); ObserveTextZoom(opened.Panel); opened.Activate();
        };
        return (button, Update);
    }
}
