using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly Dictionary<ModelToolKind, ModelToolsWindow> dedicatedTools = [];
    readonly HashSet<ModelToolKind> openingTools = [];
    DashboardWindow? dashboardWindow;
    AiDetectorWindow? aiDetectorWindow;
    DropDownButton dedicatedToolsMenuButton = null!;

    Grid BuildDedicatedToolsRow(Button settingsButton)
    {
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        row.RowSpacing = 6;
        // A shared footer surface keeps the server state separate from Settings and the existing Tools menu.
        var separator = new Border { Height = 1, Background = FluentDesign.Stroke, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        Grid.SetColumnSpan(separator, 2); row.Children.Add(separator);
        var serverState = BuildServerChip();
        var modeToggle = BuildServerModeToggle();
        row.Children.Add(serverState); Grid.SetColumn(modeToggle, 1); row.Children.Add(modeToggle);
        var menu = new MenuFlyout(); AddDedicatedToolMenuItems(menu);
        dedicatedToolsMenuButton = new DropDownButton
        {
            Content = WorkflowText("Outils", "Tools"), Flyout = menu, FontSize = 13, Padding = new(8, 6, 8, 6),
            MinHeight = 36, Background = FluentDesign.Resource("TransparentBrush"), BorderThickness = new(0), CornerRadius = new(6)
        };
        settingsButton.FontSize = 13; settingsButton.MinHeight = 36; settingsButton.Padding = new(8, 6, 8, 6);
        settingsButton.Background = FluentDesign.Resource("TransparentBrush"); settingsButton.BorderThickness = new(0); settingsButton.CornerRadius = new(6);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dedicatedToolsMenuButton, WorkflowText("Outils dédiés", "Dedicated tools"));
        Grid.SetRow(settingsButton, 1); row.Children.Add(settingsButton);
        Grid.SetRow(dedicatedToolsMenuButton, 1); Grid.SetColumn(dedicatedToolsMenuButton, 1); row.Children.Add(dedicatedToolsMenuButton);
        Closed += (_, _) => { foreach (var window in dedicatedTools.Values.ToArray()) window.Close(); dashboardWindow?.Close(); aiDetectorWindow?.Close(); diagnosticsWindow?.Close(); foreach (var approval in authorizationWindows.ToArray()) approval.Close(); };
        return row;
    }

    void AddDedicatedToolMenuItems(MenuFlyout menu)
    {
        var scheduled = new MenuFlyoutItem { Text = WorkflowText("Tâches planifiées", "Scheduled tasks"), Icon = FluentDesign.Icon("") };
        scheduled.Click += async (_, _) => await Guard(ShowScheduledTasks);
        menu.Items.Add(scheduled); menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var kind in Enum.GetValues<ModelToolKind>())
        {
            var item = new MenuFlyoutItem { Text = ModelToolsWindow.TitleFor(kind), Icon = FluentDesign.Icon(kind switch
                { ModelToolKind.Translator => "\uE8C1", ModelToolKind.Proofreader => "\uE8F2", _ => "\uE9D9" }) };
            item.Click += async (_, _) => await Guard(() => ShowDedicatedTool(kind));
            menu.Items.Add(item);
        }
        var dashboard = new MenuFlyoutItem { Text = "Dashboard", Icon = FluentDesign.Icon("\uE9D2") };
        dashboard.Click += async (_, _) => await Guard(ShowDashboardAsync); menu.Items.Add(dashboard);
        var detector = new MenuFlyoutItem { Text = "AI Generated detector", Icon = FluentDesign.Icon("\uE9F5") };
        detector.Click += async (_, _) => await Guard(ShowAiDetectorAsync); menu.Items.Add(detector);
        var debug = new MenuFlyoutItem { Text = WorkflowText("Débug · Diagnostics", "Debug · Diagnostics"), Icon = FluentDesign.Icon("\uE9D9") };
        debug.Click += async (_, _) => await Guard(ShowDiagnosticsAsync); menu.Items.Add(new MenuFlyoutSeparator()); menu.Items.Add(debug);
    }

    async Task ShowDashboardAsync()
    {
        if (dashboardWindow is { } existing) { existing.Activate(); await existing.RefreshAsync(); return; }
        var window = new DashboardWindow(db.FilePath, root.RequestedTheme) { Title = DisplayApplicationName + " · Dashboard" };
        dashboardWindow = window; window.Closed += (_, _) => dashboardWindow = null;
        ApplyBrandingIcon(window); ObserveTextZoom(window.Panel); window.Activate(); await window.RefreshAsync();
    }

    async Task ShowAiDetectorAsync()
    {
        if (aiDetectorWindow is { } existing) { existing.Activate(); return; }
        Task<List<Provider>> Models() => ReadStoreAsync(store => store.Providers.AsNoTracking().Where(p => p.Kind != "composite").OrderBy(p => p.Name).ToList());
        var configured = await Models();
        if (aiDetectorWindow is { } reopened) { reopened.Activate(); return; }
        var settings = FeatureSettings.Read(state.FeaturesJson);
        var window = new AiDetectorWindow(settings.AiDetectorEndpoint, root.RequestedTheme,
            async address => { var features = FeatureSettings.Read(state.FeaturesJson); features.AiDetectorEndpoint = address; state.FeaturesJson = features.Json(); await db.SaveChangesAsync(); }, InitializePicker,
            configured, settings.AiDetectorProviderId, settings.AiDetectorModel, Models,
            async (selected, ct) => { if (selected.IsOpenCode) await EnsureOpenCodeServerAsync(selected, KeyVault.Decrypt(selected.ProtectedKey), ct); },
            async (id, model) => { var features = FeatureSettings.Read(state.FeaturesJson); features.AiDetectorProviderId = id; features.AiDetectorModel = model; state.FeaturesJson = features.Json(); await db.SaveChangesAsync(); },
            () => FeatureSettings.Read(state.FeaturesJson), db.FilePath)
            { Title = DisplayApplicationName + " · AI Generated detector" };
        aiDetectorWindow = window; window.Closed += (_, _) => aiDetectorWindow = null;
        ApplyBrandingIcon(window); ObserveTextZoom(window.Panel); window.Activate();
    }

    async Task ShowDedicatedTool(ModelToolKind kind)
    {
        if (dedicatedTools.TryGetValue(kind, out var existing)) { existing.Activate(); return; }
        if (!openingTools.Add(kind)) return;
        try
        {
            Task<List<Provider>> Models() => ReadStoreAsync(store => store.Providers.AsNoTracking().Where(p => p.Kind != "composite").OrderBy(p => p.Name).ToList());
            var configured = await Models();
            var window = new ModelToolsWindow(kind, configured, provider?.Id, provider?.Model, root.RequestedTheme, Models,
                async (selected, ct) => { if (selected.IsOpenCode) await EnsureOpenCodeServerAsync(selected, KeyVault.Decrypt(selected.ProtectedKey), ct); }, () => FeatureSettings.Read(state.FeaturesJson), db.FilePath);
            dedicatedTools.Add(kind, window);
            window.Closed += (_, _) => dedicatedTools.Remove(kind);
            window.Title = DisplayApplicationName + " · " + ModelToolsWindow.TitleFor(kind);
            ApplyBrandingIcon(window); ObserveTextZoom(window.Panel);
            window.Activate();
        }
        finally { openingTools.Remove(kind); }
    }
}
