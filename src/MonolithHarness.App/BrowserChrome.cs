using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    Button browserBack = null!, browserForward = null!, browserReload = null!, panelSizeButton = null!;
    readonly ProgressBar browserLoading = new() { Height = 2, IsIndeterminate = true, Visibility = Visibility.Collapsed,
        VerticalAlignment = VerticalAlignment.Bottom };

    Grid BuildBrowserPage()
    {
        var web = new Grid();
        web.RowDefinitions.Add(new() { Height = GridLength.Auto });
        web.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        browserBack = ToolAction("\uE72B", WorkflowText("Page précédente", "Go back"), () => {
            if (ShownBrowser() is { Ready: true } owner && owner.View.CoreWebView2.CanGoBack) owner.View.CoreWebView2.GoBack();
            return Task.CompletedTask;
        });
        browserForward = ToolAction("\uE72A", WorkflowText("Page suivante", "Go forward"), () => {
            if (ShownBrowser() is { Ready: true } owner && owner.View.CoreWebView2.CanGoForward) owner.View.CoreWebView2.GoForward();
            return Task.CompletedTask;
        });
        browserReload = ToolAction("\uE72C", WorkflowText("Actualiser la page", "Reload page"), () => {
            if (ShownBrowser() is { Ready: true } owner) owner.View.CoreWebView2.Reload();
            return Task.CompletedTask;
        });
        var go = ToolAction("\uE72A", WorkflowText("Ouvrir l’adresse", "Open address"), NavigateAddressAsync, true);
        var navigation = new Grid { ColumnSpacing = 4, Padding = new(6, 4, 8, 6), MinHeight = 38,
            BorderBrush = FluentDesign.Stroke, BorderThickness = new(0, 0, 0, 1) };
        navigation.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        navigation.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        navigation.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var history = Row(browserBack, browserForward, browserReload); history.Spacing = 1;
        navigation.Children.Add(history); Grid.SetColumn(address, 1); navigation.Children.Add(address);
        Grid.SetColumn(go, 2); navigation.Children.Add(go);
        AutomationProperties.SetName(address, WorkflowText("Adresse du navigateur", "Browser address"));
        address.IsSpellCheckEnabled = false;
        address.KeyDown += async (_, e) => {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true; await Guard(NavigateAddressAsync);
        };
        InitializeBrowserViewportSelector();
        web.Children.Add(navigation); web.Children.Add(browserLoading);
        CompactToolTabs(webTabs); webTabs.Height = 34;
        webTabs.AddTabButtonClick += async (_, _) => await Guard(() => { NewBrowserTab(chat?.Id ?? 0); return Task.CompletedTask; });
        webTabs.SelectionChanged += (_, _) => {
            if (populatingWebTabs || webTabs.SelectedItem is not TabViewItem { Tag: string tabId }) return;
            SelectBrowserTab(chat?.Id ?? 0, tabId);
        };
        webTabs.TabCloseRequested += (_, e) => { if (e.Tab.Tag is string tabId) CloseBrowserTab(chat?.Id ?? 0, tabId); };
        Grid.SetRow(browserHost, 1); web.Children.Add(browserHost);
        idleOnly.Add(address); RefreshBrowserNavigation();
        return web;
    }

    ConversationBrowser? ShownBrowser() => conversationBrowsers.GetValueOrDefault((chat?.Id ?? 0, SelectedBrowserTab(chat?.Id ?? 0)));

    async Task NavigateAddressAsync()
    {
        var url = address.Text.Trim();
        if (url.Length == 0) return;
        if (!Path.IsPathFullyQualified(url) && !Uri.TryCreate(url, UriKind.Absolute, out _)) url = "https://" + url;
        await NavigateAsync(url, CancellationToken.None);
    }

    void RefreshBrowserNavigation()
    {
        if (browserBack == null) return;
        var owner = ShownBrowser();
        var core = owner?.Ready == true && !owner.Closed ? owner.View.CoreWebView2 : null;
        browserBack.IsEnabled = core?.CanGoBack == true;
        browserForward.IsEnabled = core?.CanGoForward == true;
        browserReload.IsEnabled = core != null;
        browserLoading.Visibility = owner?.IsNavigating == true && browserVisible && toolTabs.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshBrowserChromeLanguage()
    {
        if (browserBack == null) return;
        foreach (var (button, caption) in new[] { (browserBack, WorkflowText("Page précédente", "Go back")),
            (browserForward, WorkflowText("Page suivante", "Go forward")), (browserReload, WorkflowText("Actualiser la page", "Reload page")) })
        { AutomationProperties.SetName(button, caption); ToolTipService.SetToolTip(button, caption); }
    }

    void RefreshPanelSizeAction()
    {
        if (panelSizeButton == null) return;
        var caption = toolsMaximized ? WorkflowText("Restaurer le panneau", "Restore panel") : WorkflowText("Agrandir le panneau", "Expand panel");
        FluentDesign.IconButton(panelSizeButton, toolsMaximized ? "\uE923" : "\uE740", caption, false);
    }

    MenuFlyout BuildPanelOptionsMenu()
    {
        var menu = new MenuFlyout();
        var tools = new MenuFlyoutSubItem { Text = WorkflowText("Ouvrir un outil", "Open a tool"), Icon = FluentDesign.Icon("\uE90F") };
        AddWorkspaceToolMenuItems(tools.Items); menu.Items.Add(tools);
        if (toolTabs.SelectedIndex == 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            var file = new MenuFlyoutItem { Text = UiText.T("Ouvrir un fichier local"), Icon = FluentDesign.Icon("\uE8B7") };
            file.Click += async (_, _) => await Guard(PickPreviewAsync); menu.Items.Add(file);
            var viewport = new MenuFlyoutSubItem { Text = WorkflowText("Format d’affichage", "Viewport"), Icon = FluentDesign.Icon("\uE770") };
            foreach (var mode in new[] { "desktop", "mobile" })
            {
                var item = new ToggleMenuFlyoutItem { Text = mode == "mobile" ? "Mobile · 390 px" : "Desktop",
                    IsChecked = (ShownBrowser()?.ViewportMode ?? "desktop") == mode };
                item.Click += async (_, _) => await Guard(async () => { await SetBrowserViewportAsync(mode, CancellationToken.None); });
                viewport.Items.Add(item);
            }
            menu.Items.Add(viewport);
        }
        return menu;
    }
}
