using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;
using Windows.ApplicationModel.DataTransfer;

namespace OhMyHarness.App;

internal sealed class ConsumptionWindow : Window
{
    sealed record Choice(string Id, string Name) { public override string ToString() => Name; }
    internal Grid Panel { get; } = new() { Padding = new(24), RowSpacing = 14, Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush") };
    readonly string database;
    readonly ComboBox period = new(), provider = new(), model = new(), activity = new(), project = new();
    readonly TextBlock notice = Text("", 12, true), periodLabel = Text("", 12, true), empty = Text("", 14, true);
    readonly TextBlock total = Text("0", 24), input = Text("0", 24), output = Text("0", 24), calls = Text("0", 24);
    readonly TextBlock detailCount = Text("", 12, true), legacyNote = Text("", 12, true);
    readonly ConsumptionTimelineChart timeline = new();
    readonly StackPanel modelBars = new() { Spacing = 13 }, rows = new() { Spacing = 2 };
    readonly Button refresh, copy, previous, next;
    readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly DispatcherTimer autoRefresh = new() { Interval = TimeSpan.FromSeconds(15) };
    readonly ProgressBar progress = new() { IsIndeterminate = true, Height = 3, Visibility = Visibility.Collapsed };
    ConsumptionReport? report;
    CancellationTokenSource? request;
    bool changingFilters, closed;
    int page;
    const int PageSize = 30;
    static string L(string fr, string en) => UiText.Resolve(fr, en);
    static TextBlock Text(string text, double size = 14, bool muted = false) => new() { Text = text, FontSize = size,
        Foreground = muted ? FluentDesign.Secondary : FluentDesign.Primary, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    static StackPanel Stack(params UIElement[] children)
    {
        var stack = new StackPanel { Spacing = 10 }; foreach (var child in children) stack.Children.Add(child); return stack;
    }
    static Grid TwoColumns(UIElement left, UIElement right)
    {
        var grid = new Grid { ColumnSpacing = 12 }; grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.Children.Add(left); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
    }
    static Button Action(string label, Action click, bool accent = false)
    {
        var button = new Button { Content = label };
        if (accent) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        button.Click += (_, _) => click(); return button;
    }

    internal ConsumptionWindow(string database, ElementTheme theme)
    {
        this.database = database; Panel.RequestedTheme = theme;
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            Panel.RowDefinitions.Add(new() { Height = height });
        Panel.Children.Add(Stack(Text(L("Consommation", "Token consumption"), 27), Text(L("Suivez les tokens utilisés par vos conversations, agents et outils.",
            "Track tokens used by your chats, agents and tools."), 13, true)));
        period.Header = L("Période", "Period"); period.ItemsSource = new[] { L("Aujourd’hui", "Today"), L("3 jours", "3 days"), L("7 jours", "7 days"), L("30 jours", "30 days"), L("1 an", "1 year") }; period.SelectedIndex = 0;
        provider.Header = L("Fournisseur", "Provider"); model.Header = L("Modèle", "Model"); activity.Header = L("Usage", "Activity"); project.Header = L("Projet", "Project");
        changingFilters = true;
        Fill(provider, [], L("Tous les fournisseurs", "All providers")); Fill(model, [], L("Tous les modèles", "All models"));
        Fill(activity, [], L("Tous les usages", "All activities")); Fill(project, [], L("Tous les projets", "All projects")); changingFilters = false;
        var filterGrid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        var selectors = new[] { period, provider, model, activity, project };
        foreach (var selector in selectors)
        {
            selector.HorizontalAlignment = HorizontalAlignment.Stretch; selector.MinWidth = 0; filterGrid.Children.Add(selector);
            selector.SelectionChanged += (_, _) => { if (!changingFilters) { debounce.Stop(); debounce.Start(); } };
        }
        int columnCount = 0;
        filterGrid.SizeChanged += (_, e) =>
        {
            var count = e.NewSize.Width >= 1050 * TextZoom.ForWindow(Panel) / 100d ? 5 : e.NewSize.Width >= 550 * TextZoom.ForWindow(Panel) / 100d ? 3 : 1;
            if (count == columnCount) return; columnCount = count;
            filterGrid.ColumnDefinitions.Clear(); filterGrid.RowDefinitions.Clear();
            for (int i = 0; i < count; i++) filterGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (int i = 0; i < (selectors.Length + count - 1) / count; i++) filterGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (int i = 0; i < selectors.Length; i++) { Grid.SetColumn(selectors[i], i % count); Grid.SetRow(selectors[i], i / count); }
        };
        Grid.SetRow(filterGrid, 1); Panel.Children.Add(filterGrid);
        var messages = Stack(progress, notice); Grid.SetRow(messages, 2); Panel.Children.Add(messages);
        var content = new StackPanel { Spacing = 18 };
        content.Children.Add(periodLabel);
        var stats = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        var cards = new[] { Stat(L("Total des tokens", "Total tokens"), total), Stat(L("Tokens d’entrée", "Input tokens"), input),
            Stat(L("Tokens de sortie", "Output tokens"), output), Stat(L("Appels enregistrés", "Recorded calls"), calls) };
        foreach (var card in cards) stats.Children.Add(card);
        int statColumns = 0;
        stats.SizeChanged += (_, e) =>
        {
            var count = e.NewSize.Width >= 750 * TextZoom.ForWindow(Panel) / 100d ? 4 : e.NewSize.Width >= 400 ? 2 : 1;
            if (count == statColumns) return; statColumns = count; stats.ColumnDefinitions.Clear(); stats.RowDefinitions.Clear();
            for (int i = 0; i < count; i++) stats.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (int i = 0; i < (cards.Length + count - 1) / count; i++) stats.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (int i = 0; i < cards.Length; i++) { Grid.SetColumn(cards[i], i % count); Grid.SetRow(cards[i], i / count); }
        };
        content.Children.Add(stats); content.Children.Add(empty);
        var charts = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        charts.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); charts.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        charts.RowDefinitions.Add(new() { Height = GridLength.Auto }); charts.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var overTime = FluentDesign.Surface(Stack(Text(L("Évolution des tokens", "Token timeline"), 17), Legend(), timeline), 16);
        var byModel = FluentDesign.Surface(Stack(Text(L("Répartition par modèle", "Tokens by model"), 17), Legend(), modelBars), 16);
        charts.Children.Add(overTime); Grid.SetColumn(byModel, 1); charts.Children.Add(byModel);
        charts.SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width < 950 * TextZoom.ForWindow(Panel) / 100d;
            Grid.SetColumnSpan(overTime, narrow ? 2 : 1); Grid.SetColumnSpan(byModel, narrow ? 2 : 1);
            Grid.SetRow(byModel, narrow ? 1 : 0); Grid.SetColumn(byModel, narrow ? 0 : 1);
        };
        content.Children.Add(charts);
        content.Children.Add(Text(L("Les tokens d’entrée incluent l’historique renvoyé à chaque appel. ≈ indique une estimation. Ces chiffres couvrent cette installation, pas le relevé de facturation du fournisseur.",
            "Input tokens include history sent again with each call. ≈ marks estimates. These figures cover this installation, not the provider's billing statement."), 12, true));
        content.Children.Add(legacyNote);
        previous = Action(L("Précédent", "Previous"), () => { page--; RenderDetails(); });
        next = Action(L("Suivant", "Next"), () => { page++; RenderDetails(); });
        var pagination = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; pagination.Children.Add(previous); pagination.Children.Add(next);
        var table = Stack(TableRow([L("Réponse terminée", "Completed at"), L("Fournisseur / modèle", "Provider / model"), L("Usage", "Activity"), L("Projet / conversation", "Project / chat"),
            L("Entrée", "Input"), L("Sortie", "Output"), L("Total", "Total"), L("Durée", "Duration"), L("Mesure / état", "Measurement / status")], true), rows);
        content.Children.Add(FluentDesign.Surface(Stack(Text(L("Détail des appels", "Call details"), 17),
            new ScrollViewer { Content = table, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, TwoColumns(detailCount, pagination)), 16));
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 3); Panel.Children.Add(scroll);
        refresh = Action(L("Actualiser", "Refresh"), () => _ = RefreshAsync(), true);
        copy = Action(L("Copier le détail CSV", "Copy details as CSV"), CopyCsv);
        var reset = Action(L("Effacer les filtres", "Clear filters"), () => { changingFilters = true; foreach (var selector in selectors.Skip(1)) selector.SelectedIndex = 0; changingFilters = false; _ = RefreshAsync(); });
        reset.Content = Text(L("Effacer les filtres", "Clear filters"), 14); copy.Content = Text(L("Copier le détail CSV", "Copy details as CSV"), 14);
        var secondary = new Grid { ColumnSpacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
        secondary.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); secondary.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        secondary.Children.Add(reset); Grid.SetColumn(copy, 1); secondary.Children.Add(copy);
        var actions = TwoColumns(secondary, refresh);
        actions.RowDefinitions.Add(new() { Height = GridLength.Auto }); actions.RowDefinitions.Add(new() { Height = GridLength.Auto });
        actions.RowSpacing = 10; refresh.HorizontalAlignment = HorizontalAlignment.Right;
        actions.SizeChanged += (_, e) => { bool narrow = e.NewSize.Width < 600 * TextZoom.ForWindow(Panel) / 100d;
            secondary.MaxWidth = e.NewSize.Width; Grid.SetColumnSpan(secondary, narrow ? 2 : 1); Grid.SetRow(refresh, narrow ? 1 : 0); };
        Grid.SetRow(actions, 4); Panel.Children.Add(actions);
        debounce.Tick += async (_, _) => { debounce.Stop(); await RefreshAsync(); };
        autoRefresh.Tick += async (_, _) => { if (request == null) await RefreshAsync(false); };
        Panel.Loaded += (_, _) => autoRefresh.Start();
        Content = Panel; FluentDesign.WindowChrome(this); AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1260, Height = 960 });
        Closed += (_, _) => { closed = true; debounce.Stop(); autoRefresh.Stop(); request?.Cancel(); };
    }

    static Border Stat(string label, TextBlock value) => FluentDesign.Surface(Stack(Text(label, 12, true), value), 14);
    static StackPanel Legend()
    {
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        foreach (var (label, brush) in new[] { (L("Entrée", "Input"), ConsumptionTimelineChart.InputBrush), (L("Sortie", "Output"), ConsumptionTimelineChart.OutputBrush) })
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            item.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new(2), Background = brush, VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(Text(label, 12, true)); legend.Children.Add(item);
        }
        return legend;
    }
    static string Selected(ComboBox box) => (box.SelectedItem as Choice)?.Id ?? "";
    static void Fill(ComboBox box, IEnumerable<Choice> choices, string all)
    {
        var selected = box.SelectedItem as Choice;
        var options = choices.GroupBy(x => x.Id).Select(x => x.First()).OrderBy(x => x.Name).ToList();
        if (selected is { Id.Length: > 0 } && options.All(x => x.Id != selected.Id)) options.Add(selected);
        options.Insert(0, new("", all)); box.ItemsSource = options; box.SelectedItem = options.FirstOrDefault(x => x.Id == selected?.Id) ?? options[0];
    }

    internal async Task RefreshAsync(bool resetPage = true)
    {
        if (closed) return; debounce.Stop(); request?.Cancel();
        using var cancellation = new CancellationTokenSource(); request = cancellation;
        refresh.IsEnabled = false; progress.Visibility = Visibility.Visible; notice.Text = "";
        var filter = new ConsumptionFilter((ConsumptionPeriod)Math.Max(0, period.SelectedIndex), Selected(provider), Selected(model), Selected(activity),
            int.TryParse(Selected(project), out var id) ? id : null);
        try
        {
            var snapshot = await Task.Run(() => ConsumptionReports.ReadAsync(database, filter, cancellation.Token), cancellation.Token);
            if (closed || cancellation.IsCancellationRequested) return;
            report = snapshot; if (resetPage) page = 0;
            changingFilters = true;
            try
            {
                Fill(provider, snapshot.Available.Select(x => new Choice(ConsumptionReports.ProviderKey(x), ProviderName(x))), L("Tous les fournisseurs", "All providers"));
                Fill(model, snapshot.Available.Select(x => new Choice(ConsumptionReports.ModelKey(x), x.Model.Length == 0 ? L("Modèle non enregistré", "Model not recorded") : x.Model)), L("Tous les modèles", "All models"));
                Fill(activity, snapshot.Available.Select(x => new Choice(x.Activity, ActivityName(x.Activity))), L("Tous les usages", "All activities"));
                Fill(project, snapshot.Available.Where(x => x.ProjectId != null).Select(x => new Choice(x.ProjectId!.Value.ToString(), x.ProjectName)), L("Tous les projets", "All projects"));
            }
            finally { changingFilters = false; }
            var approximate = snapshot.Estimates > 0 ? "≈ " : "";
            total.Text = approximate + $"{snapshot.Total:N0}"; input.Text = snapshot.Entries.Any(x => x.InputEstimated) ? $"≈ {snapshot.Input:N0}" : $"{snapshot.Input:N0}";
            output.Text = snapshot.Entries.Any(x => x.OutputEstimated) ? $"≈ {snapshot.Output:N0}" : $"{snapshot.Output:N0}"; calls.Text = $"{snapshot.Entries.Count:N0}";
            periodLabel.Text = $"{snapshot.StartLocal:d} – {snapshot.EndLocal.AddDays(-1):d} · " + L("heure locale", "local time") +
                $" · {snapshot.Estimates:N0} " + L("appels avec estimation", "calls with estimates");
            empty.Text = L("Aucune consommation enregistrée pour ces filtres. Les prochains appels apparaîtront après leur fin.", "No usage recorded for these filters. New calls appear after they finish.");
            empty.Visibility = snapshot.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            legacyNote.Text = (snapshot.Available.Any(x => x.Legacy) ? L("Les anciennes mesures sont reprises avec les informations disponibles ; fournisseur, modèle ou compteurs peuvent manquer.",
                "Old measurements are imported with the available information; provider, model or counters may be missing.") : "") +
                (snapshot.UndatedLegacy > 0 ? " " + L("Anciennes réponses sans date exclues : ", "Old replies without dates excluded: ") + snapshot.UndatedLegacy.ToString("N0") + "." : "");
            legacyNote.Visibility = legacyNote.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            timeline.SetValues(snapshot.Timeline); RenderModels(); RenderDetails(); copy.IsEnabled = snapshot.Entries.Count > 0;
            TextZoom.Apply(Panel);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closed && !cancellation.IsCancellationRequested) { notice.Text = ex.Message; notice.Foreground = FluentDesign.Resource("ToolMessageErrorStrokeBrush"); } }
        finally { if (ReferenceEquals(request, cancellation)) { request = null; if (!closed) { refresh.IsEnabled = true; progress.Visibility = Visibility.Collapsed; } } }
    }

    static string ProviderName(TokenUsage entry) => entry.ProviderName.Length == 0 ? L("Historique · fournisseur inconnu", "History · unknown provider") :
        entry.ProviderId is int id ? $"{entry.ProviderName} · #{id}" : entry.ProviderName;
    static string ActivityName(string value) => value switch { "chat" => L("Conversations", "Chats"), "agent" => L("Agents", "Agents"), "naming" => L("Nommage", "Naming"),
        "vision" => L("Vision", "Vision"), "compaction" => L("Compactage", "Compaction"), "translator" => L("Traduction", "Translation"),
        "proofreader" => L("Correction / reformulation", "Proofreading / rephrasing"), "benchmark" => L("Benchmark", "Benchmark"), _ => value };
    void RenderModels()
    {
        modelBars.Children.Clear(); if (report == null) return;
        var groups = report.Entries.GroupBy(x => ProviderName(x) + " · " + (x.Model.Length == 0 ? L("Modèle non enregistré", "Model not recorded") : x.Model))
            .Select(x => new TokenBucket(x.Key, x.Sum(v => v.InputTokens), x.Sum(v => v.OutputTokens), x.Count())).OrderByDescending(x => x.Total).ToArray();
        var shown = groups.Take(7).ToList();
        if (groups.Length > 7) shown.Add(new(L("Autres modèles", "Other models"), groups.Skip(7).Sum(x => x.Input), groups.Skip(7).Sum(x => x.Output), groups.Skip(7).Sum(x => x.Calls)));
        var maximum = Math.Max(1, shown.Count == 0 ? 0 : shown.Max(x => x.Total));
        foreach (var value in shown)
        {
            var bar = new Grid { Height = 8, Background = FluentDesign.Resource("ControlSelectedBrush") };
            foreach (var number in new[] { value.Input, value.Output, maximum - value.Total }) bar.ColumnDefinitions.Add(new() { Width = new(number, GridUnitType.Star) });
            bar.Children.Add(new Border { Background = ConsumptionTimelineChart.InputBrush });
            var right = new Border { Background = ConsumptionTimelineChart.OutputBrush }; Grid.SetColumn(right, 1); bar.Children.Add(right);
            var label = Text(value.Label, 12); label.MaxLines = 2; label.TextTrimming = TextTrimming.CharacterEllipsis; ToolTipService.SetToolTip(label, value.Label);
            ToolTipService.SetToolTip(bar, $"{L("Entrée", "Input")}: {value.Input:N0} · {L("Sortie", "Output")}: {value.Output:N0}");
            modelBars.Children.Add(Stack(TwoColumns(label, Text($"{value.Total:N0}", 12)), bar));
        }
        if (shown.Count == 0) modelBars.Children.Add(Text(L("Aucune donnée sur cette période.", "No data for this period."), 13, true));
    }

    static Grid TableRow(string[] values, bool header = false)
    {
        var grid = new Grid { Width = 1440, Padding = new(5, 8, 5, 8), ColumnSpacing = 10,
            Background = FluentDesign.Resource(header ? "ControlSelectedBrush" : "TransparentBrush") };
        foreach (var width in new[] { 145, 260, 145, 270, 100, 100, 110, 100, 120 }) grid.ColumnDefinitions.Add(new() { Width = new(width) });
        for (int i = 0; i < values.Length; i++)
        {
            var label = Text(values[i], 12, header); label.MaxLines = 2; label.TextTrimming = TextTrimming.CharacterEllipsis;
            if (header) label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            ToolTipService.SetToolTip(label, values[i]); Grid.SetColumn(label, i); grid.Children.Add(label);
        }
        return grid;
    }
    static DateTime Local(DateTime date) => DateTime.SpecifyKind(date, DateTimeKind.Utc).ToLocalTime();
    static string Count(long value, bool estimated, bool legacy = false) => legacy && estimated && value == 0 ? "—" : (estimated ? "≈ " : "") + value.ToString("N0");
    static string Quality(TokenUsage value) => value.Legacy ? L("Historique", "History") : value.Estimated ? L("Estimation", "Estimate") : L("Déclaré", "Reported");
    static string Status(TokenUsage value) => value.Status switch { "cancelled" => L("Interrompu", "Interrupted"), "error" => L("Erreur", "Error"), _ => L("Terminé", "Complete") };
    void RenderDetails()
    {
        rows.Children.Clear(); var entries = report?.Entries ?? [];
        page = Math.Clamp(page, 0, Math.Max(0, (entries.Count - 1) / PageSize));
        foreach (var entry in entries.Skip(page * PageSize).Take(PageSize))
            rows.Children.Add(TableRow([Local(entry.CompletedUtc).ToString("dd/MM/yyyy HH:mm:ss"), ProviderName(entry) + "\n" + (entry.Model.Length == 0 ? "—" : entry.Model), ActivityName(entry.Activity),
                (entry.ProjectName.Length == 0 ? "—" : entry.ProjectName) + "\n" + (entry.ChatTitle.Length == 0 ? "—" : entry.ChatTitle), Count(entry.InputTokens, entry.InputEstimated, entry.Legacy),
                Count(entry.OutputTokens, entry.OutputEstimated, entry.Legacy), Count(entry.TotalTokens, entry.Estimated), $"{entry.Seconds:F1} s", Quality(entry) + "\n" + Status(entry)]));
        detailCount.Text = entries.Count == 0 ? L("Aucun appel", "No calls") : $"{page * PageSize + 1:N0} – {Math.Min((page + 1) * PageSize, entries.Count):N0} / {entries.Count:N0} " + L("appels", "calls");
        previous.IsEnabled = page > 0; next.IsEnabled = (page + 1) * PageSize < entries.Count;
    }
    void CopyCsv()
    {
        if (report == null) return;
        static string Cell(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
        var header = "CompletedUtc,Provider,Model,Activity,Project,Chat,InputTokens,OutputTokens,InputEstimated,OutputEstimated,Seconds,Status,Legacy";
        var lines = report.Entries.Select(x => string.Join(',', new[] { DateTime.SpecifyKind(x.CompletedUtc, DateTimeKind.Utc).ToString("O"), ProviderName(x), x.Model, x.Activity, x.ProjectName, x.ChatTitle,
            x.InputTokens.ToString(), x.OutputTokens.ToString(), x.InputEstimated.ToString(), x.OutputEstimated.ToString(), x.Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), x.Status, x.Legacy.ToString() }.Select(Cell)));
        var data = new DataPackage(); data.SetText(header + "\r\n" + string.Join("\r\n", lines)); Clipboard.SetContent(data);
        notice.Text = L("Détail CSV copié.", "CSV details copied."); notice.Foreground = FluentDesign.Secondary;
    }
}
