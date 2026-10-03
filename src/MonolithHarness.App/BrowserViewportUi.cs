using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using System.Text.Json.Nodes;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly ComboBox browserViewportMode = new() { Width = 122, MinWidth = 0, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    bool syncingBrowserViewport;
    void InitializeBrowserViewportSelector()
    {
        browserViewportMode.Items.Add("🖥 Desktop"); browserViewportMode.Items.Add("📱 Mobile");
        RefreshBrowserViewportSelector("desktop");
        ToolTipService.SetToolTip(browserViewportMode, WorkflowText("Affichage responsive · mobile : largeur 390 px", "Responsive display · mobile: 390 px wide"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(browserViewportMode, WorkflowText("Affichage Web : Desktop / Mobile", "Web display: Desktop / Mobile"));
        browserViewportMode.SelectionChanged += async (_, _) =>
        {
            if (syncingBrowserViewport || chat == null) return;
            using var scope = BrowserScope(chat.Id);
            await Guard(async () => { await SetBrowserViewportAsync(browserViewportMode.SelectedIndex == 1 ? "mobile" : "desktop", CancellationToken.None); });
        };
    }
    void RefreshBrowserViewportSelector(string mode)
    {
        if (browserViewportMode.Items.Count == 0) return;
        syncingBrowserViewport = true;
        try { browserViewportMode.SelectedIndex = mode == "mobile" ? 1 : 0; }
        finally { syncingBrowserViewport = false; }
    }
    static void ApplyBrowserViewport(ConversationBrowser owner)
    {
        var mobile = owner.ViewportMode == "mobile";
        owner.View.Width = mobile ? MonolithHarness.Core.BrowserViewport.MobileWidth : double.NaN;
        owner.View.HorizontalAlignment = mobile ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        owner.View.VerticalAlignment = VerticalAlignment.Stretch;
        owner.Frame.HorizontalScrollMode = mobile ? ScrollMode.Enabled : ScrollMode.Disabled;
        owner.Frame.HorizontalScrollBarVisibility = mobile ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        owner.Frame.HorizontalContentAlignment = mobile ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        owner.Frame.VerticalContentAlignment = VerticalAlignment.Stretch;
    }
    async Task<string> SetBrowserViewportAsync(string mode, CancellationToken ct)
    {
        mode = MonolithHarness.Core.BrowserViewport.Normalize(mode);
        await EnsureBrowser(); ct.ThrowIfCancellationRequested();
        var owner = CurrentBrowser; owner.ViewportMode = mode; ApplyBrowserViewport(owner);
        if (owner.Id == chat?.Id && owner.TabId == SelectedBrowserTab(owner.Id)) RefreshBrowserViewportSelector(mode);
        // Let the native view resize before reporting the page's actual CSS viewport.
        await Task.Delay(80, ct);
        var measured = await owner.View.ExecuteScriptAsync("({width:innerWidth,height:innerHeight,device_pixel_ratio:devicePixelRatio})");
        return new JsonObject { ["mode"] = mode, ["tab_id"] = owner.TabId, ["viewport"] = JsonNode.Parse(measured),
            ["responsive_preview"] = true, ["device_emulation"] = false }.ToJsonString();
    }
}
