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

    Grid BuildDedicatedToolsRow()
    {
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var scheduled = Action(WorkflowText("Tâches planifiées", "Scheduled tasks"), ShowScheduledTasks);
        scheduled.FontSize = 12; scheduled.Padding = new(8, 8, 8, 8);
        scheduled.HorizontalAlignment = HorizontalAlignment.Stretch;
        var menu = new MenuFlyout();
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
        var tools = new DropDownButton { Content = WorkflowText("Outils", "Tools"), Flyout = menu, FontSize = 12, Padding = new(8, 8, 8, 8) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tools, WorkflowText("Outils dédiés", "Dedicated tools"));
        row.Children.Add(scheduled); Grid.SetColumn(tools, 1); row.Children.Add(tools);
        Closed += (_, _) => { foreach (var window in dedicatedTools.Values.ToArray()) window.Close(); dashboardWindow?.Close(); aiDetectorWindow?.Close(); };
        return row;
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
