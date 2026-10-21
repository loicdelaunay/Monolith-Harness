using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using Windows.ApplicationModel.DataTransfer;

namespace MonolithHarness.App;

public sealed class DiagnosticsWindow : Window
{
    public Grid Panel { get; } = new() { Padding = new(24), RowSpacing = 12 };
    readonly ApplicationDiagnostics sampler = new();
    readonly CancellationTokenSource lifetime = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly DispatcherTimer filterDelay = new() { Interval = TimeSpan.FromMilliseconds(180) };
    readonly TextBlock stateText = Text(""), cpu = Text("—", 26), ram = Text("—", 26), privateRam = Text("—", 26), managedRam = Text("—", 26), notice = Text("", 12), logDetail = Text("");
    readonly TextBox search = new() { PlaceholderText = L("Filtrer les journaux…", "Filter logs…"), MaxLength = 180, MinWidth = 120 };
    readonly ComboBox severity = new() { MinWidth = 140 }, source = new() { MinWidth = 160 };
    readonly ListView logRows = new() { SelectionMode = ListViewSelectionMode.Single };
    readonly StackPanel errors = new() { Spacing = 10 };
    readonly Func<string> uiState;
    readonly TextBlock idleMeasurement = Text("");
    bool measuring, active = true;
    readonly string database;
    string[] persisted = [];
    string lastLogs = "", lastErrors = "";
    bool refreshing, closed;
    int ticks;
    sealed record LogRow(string Json, string Caption, string Level, DateTimeOffset Time)
    { public override string ToString() => Caption; }
    static string L(string french, string english) => UiText.Resolve(french, english);
    static TextBlock Text(string value, double size = 13) => new()
    { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Foreground = FluentDesign.Primary };
    static StackPanel Stack(params UIElement[] children)
    { var stack = new StackPanel { Spacing = 8 }; foreach (var child in children) stack.Children.Add(child); return stack; }
    public DiagnosticsWindow(string database, ElementTheme theme, Func<string> uiState, Func<CancellationToken, Task<string>> measureIdle)
    {
        this.database = database; this.uiState = uiState;
        Title = "Monolith · " + L("Débug · Diagnostics", "Debug · Diagnostics");
        Panel.RequestedTheme = theme; Panel.Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush");
        Panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); Panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); Panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Panel.Children.Add(Stack(Text(L("Débug · Diagnostics", "Debug · Diagnostics"), 26),
            Text(L("CPU/RAM du processus et état actualisés toutes les 2 secondes. Les erreurs de génération sont relues toutes les 10 secondes.",
                "Process CPU/RAM and state refresh every 2 seconds. Generation errors refresh every 10 seconds."), 12)));
        Grid.SetRow(notice, 1); Panel.Children.Add(notice);
        var tabs = new TabView { CanDragTabs = false, CanReorderTabs = false, IsAddTabButtonVisible = false };
        var statePanel = new StackPanel { Spacing = 14 };
        var stats = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        var cards = new[] { Card("CPU", cpu), Card(L("RAM utilisée", "Working set"), ram), Card(L("Mémoire privée", "Private memory"), privateRam), Card(L("Mémoire .NET", ".NET memory"), managedRam) };
        foreach (var card in cards) stats.Children.Add(card);
        int columns = 0;
        stats.SizeChanged += (_, e) =>
        {
            int count = e.NewSize.Width >= 800 ? 4 : e.NewSize.Width >= 450 ? 2 : 1;
            if (columns == count) return; columns = count; stats.ColumnDefinitions.Clear(); stats.RowDefinitions.Clear();
            for (int i = 0; i < count; i++) stats.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (int i = 0; i < (cards.Length + count - 1) / count; i++) stats.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (int i = 0; i < cards.Length; i++) { Grid.SetColumn(cards[i], i % count); Grid.SetRow(cards[i], i / count); }
        };
        statePanel.Children.Add(stats); statePanel.Children.Add(FluentDesign.Surface(stateText, 16));
        statePanel.Children.Add(idleMeasurement);
        statePanel.Children.Add(Text(L("CPU exprimé en pourcentage de la capacité totale des processeurs logiques. Les mesures concernent Monolith ; les processus externes (modèles locaux, serveurs, terminaux) ne sont pas inclus.",
            "CPU is a percentage of total logical processor capacity. Measurements cover Monolith; external processes (local models, servers, terminals) are not included."), 12));
        tabs.TabItems.Add(new TabViewItem { Header = L("État", "State"), IsClosable = false, Content = Scroll(statePanel) });
        var logsPanel = new Grid { RowSpacing = 10 };
        logsPanel.RowDefinitions.Add(new() { Height = GridLength.Auto }); logsPanel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); logsPanel.RowDefinitions.Add(new() { Height = new(200) });
        severity.ItemsSource = new[] { L("Tous les niveaux", "All levels"), "Debug", "Information", "Warning", "Error", "Critical" }; severity.SelectedIndex = 0;
        source.ItemsSource = new[] { L("Cette session", "This session"), L("Journaux conservés", "Retained logs") }; source.SelectedIndex = 0;
        var filters = new Grid { ColumnSpacing = 8 }; filters.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        filters.Children.Add(search); Grid.SetColumn(severity, 1); filters.Children.Add(severity); Grid.SetColumn(source, 2); filters.Children.Add(source);
        logsPanel.Children.Add(filters); Grid.SetRow(logRows, 1); logsPanel.Children.Add(logRows);
        var detail = FluentDesign.Surface(Scroll(logDetail), 12); Grid.SetRow(detail, 2); logsPanel.Children.Add(detail);
        tabs.TabItems.Add(new TabViewItem { Header = L("Journaux", "Logs"), IsClosable = false, Content = logsPanel });
        tabs.TabItems.Add(new TabViewItem { Header = L("Erreurs et interruptions", "Errors and interruptions"), IsClosable = false, Content = Scroll(errors) });
        Grid.SetRow(tabs, 2); Panel.Children.Add(tabs);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(Action(L("Copier le diagnostic", "Copy diagnostics"), () =>
        { var data = new DataPackage(); data.SetText(stateText.Text + "\n\n" + idleMeasurement.Text + "\n\n" + logDetail.Text); Clipboard.SetContent(data); }));
        actions.Children.Add(Action(L("Dossier des logs", "Logs folder"), () =>
        { try { if (Directory.Exists(AppLog.DirectoryPath)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = AppLog.DirectoryPath, UseShellExecute = true }); } catch (Exception ex) { notice.Text = ToolAuthorization.Clean(ex.Message); } }));
        var measure = new Button { Content = L("Mesurer le repos (30 s)", "Measure idle (30 s)") };
        measure.Click += async (_, _) =>
        {
            if (measuring) return;
            measuring = true; measure.IsEnabled = false; timer.Stop(); filterDelay.Stop();
            idleMeasurement.Text = L("Mesure en cours : laissez l’application au repos pendant 30 secondes. Les relevés du débug sont suspendus.", "Measuring: leave the app idle for 30 seconds. Debug sampling is paused.");
            try
            {
                while (refreshing && !closed) await Task.Delay(50, lifetime.Token);
                idleMeasurement.Text = await measureIdle(lifetime.Token);
            }
            catch (OperationCanceledException) when (closed) { }
            catch (Exception ex) { idleMeasurement.Text = ToolAuthorization.Clean(ex.Message); }
            finally { measuring = false; measure.IsEnabled = true; if (!closed && active) timer.Start(); }
        };
        actions.Children.Add(measure);
        actions.Children.Add(Action(L("Actualiser", "Refresh"), async () => await RefreshAsync(true), true));
        actions.Children.Add(Action(L("Fermer", "Close"), Close));
        Grid.SetRow(actions, 3); Panel.Children.Add(actions);
        Content = Panel; FluentDesign.WindowChrome(this); AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1060, Height = 820 });
        search.TextChanged += (_, _) => { filterDelay.Stop(); filterDelay.Start(); };
        filterDelay.Tick += (_, _) => { filterDelay.Stop(); RenderLogs(); };
        severity.SelectionChanged += (_, _) => RenderLogs();
        source.SelectionChanged += async (_, _) => { RenderLogs(); await RefreshAsync(true); };
        logRows.SelectionChanged += (_, _) => { if (logRows.SelectedItem is LogRow entry) logDetail.Text = Pretty(entry.Json); };
        timer.Tick += async (_, _) => { if (active && !measuring) await RefreshAsync(++ticks % 5 == 0); };
        Panel.Loaded += async (_, _) => { await RefreshAsync(true); if (!closed && active && !measuring) timer.Start(); };
        Activated += async (_, args) =>
        {
            active = args.WindowActivationState != Windows.UI.Core.CoreWindowActivationState.Deactivated;
            if (!active) { timer.Stop(); filterDelay.Stop(); }
            else if (!closed && !measuring) { await RefreshAsync(); if (!closed && active && !measuring) timer.Start(); }
        };
        Closed += (_, _) => { closed = true; timer.Stop(); filterDelay.Stop(); lifetime.Cancel(); if (!refreshing) sampler.Dispose(); };
    }
    static Border Card(string label, TextBlock value) => FluentDesign.Surface(Stack(Text(label, 12), value), 12);
    static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    static Button Action(string label, Action clicked, bool accent = false)
    {
        var button = new Button { Content = label };
        if (accent) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        button.Click += (_, _) => clicked(); return button;
    }
    static string Pretty(string json)
    { try { using var doc = JsonDocument.Parse(json); return ToolAuthorization.Clean(JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true }), 16000); } catch (JsonException) { return ToolAuthorization.Clean(json, 16000); } }
    public async Task RefreshAsync(bool readHistory = false)
    {
        if (refreshing || closed || measuring) return; refreshing = true;
        try
        {
            var ui = uiState();
            var sample = await Task.Run(sampler.Sample, lifetime.Token);
            if (closed) return;
            cpu.Text = sample.CpuPercent?.ToString("0.0") + (sample.CpuPercent.HasValue ? " %" : L("Calcul…", "Sampling…"));
            static string Mb(long value) => (value / 1048576d).ToString("0.0") + " MB";
            ram.Text = Mb(sample.WorkingSet); privateRam.Text = Mb(sample.PrivateMemory); managedRam.Text = Mb(sample.ManagedMemory);
            var version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "";
            stateText.Text = $"Monolith {version} · PID {sample.ProcessId}\n" +
                L("Durée d’activité", "Uptime") + $" : {sample.Uptime.ToString(@"dd\.hh\:mm\:ss")}\n" + ui + "\n\n" +
                $"{sample.OperatingSystem} · {sample.Architecture}\n{sample.Runtime} · {sample.LogicalProcessors} " + L("processeurs logiques", "logical processors") + "\n" +
                $"Threads : {sample.Threads} · Handles : {sample.Handles?.ToString() ?? "—"}\nGC 0 / 1 / 2 : {string.Join(" / ", sample.Collections)}\n" +
                L("Journaux", "Logs") + $" : {(sample.LogsEnabled ? L("activés", "enabled") : L("désactivés", "disabled"))} · " +
                L("en attente", "pending") + $" : {sample.PendingLogs} · " + L("non enregistrés (file pleine)", "dropped (queue full)") + $" : {sample.DroppedLogs}\n{AppLog.DirectoryPath}";
            if (readHistory)
            {
                persisted = await ApplicationDiagnostics.ReadRecentLogsAsync(lifetime.Token);
                var failures = await ApplicationDiagnostics.ReadGenerationErrorsAsync(database, lifetime.Token);
                if (closed) return;
                var key = string.Join("\n", failures);
                if (key != lastErrors || errors.Children.Count == 0)
                {
                    lastErrors = key; errors.Children.Clear();
                    errors.Children.Add(Text(L("Causes de fin enregistrées sans contenu complet des messages. Les anciens messages peuvent ne pas contenir de diagnostic.",
                        "Recorded ending causes without full message content. Older messages may have no diagnostic."), 12));
                    if (failures.Length == 0) errors.Children.Add(Text(L("Aucune erreur de génération enregistrée.", "No recorded generation errors.")));
                    foreach (var failure in failures) errors.Children.Add(FluentDesign.Surface(Text(Pretty(failure)), 12));
                }
            }
            RenderLogs(); notice.Text = L("Dernière actualisation : ", "Last refreshed: ") + DateTime.Now.ToString("T");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closed) notice.Text = ToolAuthorization.Clean(ex.Message); }
        finally { refreshing = false; if (closed) sampler.Dispose(); }
    }
    void RenderLogs()
    {
        if (closed) return;
        var raw = source.SelectedIndex == 1 ? persisted.Concat(AppLog.Recent()).Distinct().ToArray() : AppLog.Recent();
        var query = search.Text.Trim(); var selectedLevel = severity.SelectedIndex <= 0 ? "" : severity.SelectedItem?.ToString() ?? "";
        var fingerprint = source.SelectedIndex + "|" + selectedLevel + "|" + query + "|" + string.Join("\n", raw);
        if (fingerprint == lastLogs) return; lastLogs = fingerprint;
        var rows = new List<LogRow>();
        foreach (var value in raw)
        {
            try
            {
                using var doc = JsonDocument.Parse(value); var item = doc.RootElement;
                string Get(string key) => item.TryGetProperty(key, out var field) ? field.ToString() : "";
                var level = Get("level"); if (selectedLevel.Length > 0 && level != selectedLevel) continue;
                if (query.Length > 0 && !value.Contains(query, StringComparison.CurrentCultureIgnoreCase)) continue;
                DateTimeOffset.TryParse(Get("time"), out var time);
                var caption = $"{time.ToLocalTime():HH:mm:ss} · {level} · {Get("operation")}";
                if (Get("errorType").Length > 0) caption += " · " + Get("errorType");
                if (Get("chatId").Length > 0) caption += " · chat " + Get("chatId");
                rows.Add(new(value, caption, level, time));
            }
            catch (JsonException) { }
        }
        var selected = (logRows.SelectedItem as LogRow)?.Json;
        var ordered = rows.OrderByDescending(x => x.Time).Take(500).ToArray(); logRows.ItemsSource = ordered;
        logRows.SelectedItem = ordered.FirstOrDefault(x => x.Json == selected) ?? ordered.FirstOrDefault();
        if (ordered.Length == 0) logDetail.Text = L("Aucun journal pour ces filtres. La journalisation et son niveau se règlent dans About.", "No logs match these filters. Logging and its level are configured in About.");
    }
}
