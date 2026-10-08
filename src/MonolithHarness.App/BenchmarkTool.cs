using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

internal sealed partial class ModelToolsWindow
{
    readonly List<ModelBenchmarkEntry> benchmarkEntries = [];
    readonly StackPanel benchmarkRows = new() { Spacing = 10 };
    readonly List<Button> benchmarkReplayButtons = [];
    ModelBenchmarkReport? benchmarkReport;
    TextBlock benchmarkSpeed = null!, benchmarkTokens = null!, benchmarkLogic = null!, benchmarkBugs = null!;
    TextBlock benchmarkProgress = null!;
    TextBlock benchmarkGrade = null!, benchmarkPercent = null!, benchmarkDescription = null!;
    ComboBox benchmarkLevel = null!;
    ComboBox benchmarkScope = null!, benchmarkLimit = null!, benchmarkApplication = null!;
    NumberBox benchmarkSeed = null!;
    IReadOnlyList<ModelBenchmarkCase> currentBenchmarkCases = [];
    BenchmarkPreview? benchmarkPreview;
    readonly Grid benchmarkPreviewHost = new();
    Grid benchmarkLayout = null!;
    bool benchmarkPreviewVisible;
    TextBlock benchmarkPreviewTitle = null!;
    string? benchmarkPreviewHtml;
    Button reloadBenchmarkPreview = null!, copyBenchmarkHtml = null!;
    bool ToolWebPreviewAllowed => readRetrySettings?.Invoke().AllowWebViewInTools == true;
    int BenchmarkSeed => double.IsFinite(benchmarkSeed.Value) ? (int)Math.Clamp(benchmarkSeed.Value, 0, 999_999_999) : 1729;
    int BenchmarkTimeout => new[] { 180, 600, 1200 }[Math.Max(0, benchmarkLimit.SelectedIndex)];
    string BenchmarkScope => new[] { "reasoning", "interactive", "combined" }[Math.Max(0, benchmarkScope.SelectedIndex)];
    ModelBenchmarkLevel BenchmarkLevel => (ModelBenchmarkLevel)Math.Max(0, benchmarkLevel.SelectedIndex);
    IReadOnlyList<ModelBenchmarkCase> BenchmarkCases => currentBenchmarkCases;
    Button copyBenchmark = null!;

    UIElement BuildBenchmark()
    {
        var body = new Grid();
        var overview = new StackPanel { Spacing = 12 };
        var choices = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        choices.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); choices.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        choices.RowDefinitions.Add(new() { Height = GridLength.Auto }); choices.RowDefinitions.Add(new() { Height = GridLength.Auto });
        choices.RowDefinitions.Add(new() { Height = GridLength.Auto });
        benchmarkLevel = new ComboBox { Header = L("Difficulté", "Difficulty"), ItemsSource = new[] { L("Facile", "Easy"), L("Moyen", "Medium"), L("Difficile", "Hard") }, SelectedIndex = 0, MinWidth = 180 };
        benchmarkScope = new ComboBox { Header = L("Épreuves", "Challenges"), ItemsSource = new[] { L("Raisonnement et code", "Reasoning and code"), L("Applications interactives", "Interactive applications"), L("Suite complète", "Combined suite") }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        benchmarkLimit = new ComboBox { Header = L("Délai par épreuve", "Time per challenge"), ItemsSource = new[] { "3 min", "10 min", "20 min" }, SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        benchmarkApplication = new ComboBox { Header = L("Application à construire", "Application to build"), ItemsSource = new[] { L("Les quatre applications", "All four applications") }.Concat(VisualBenchmarkTask.Cases.Select(CaseTitle)).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        benchmarkSeed = new NumberBox { Header = L("Série reproductible", "Reproducible seed"), Value = 1729, Minimum = 0, Maximum = 999_999_999, SmallChange = 1, MinWidth = 115 };
        var reshuffle = Button(L("Nouvelle", "New"), () => { benchmarkSeed.Value = System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, 999_999_999); return Task.CompletedTask; });
        reshuffle.VerticalAlignment = VerticalAlignment.Bottom;
        At(choices, benchmarkScope, 0); At(choices, benchmarkLevel, 0, 1); At(choices, Row(benchmarkSeed, reshuffle), 1); At(choices, benchmarkLimit, 1, 1);
        At(choices, benchmarkApplication, 2); Grid.SetColumnSpan(benchmarkApplication, 2);
        overview.Children.Add(choices); busyControls.AddRange([benchmarkLevel, benchmarkScope, benchmarkLimit, benchmarkSeed, reshuffle, benchmarkApplication]);
        var cards = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        foreach (var _ in Enumerable.Range(0, 5)) cards.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        foreach (var _ in Enumerable.Range(0, 3)) cards.RowDefinitions.Add(new() { Height = GridLength.Auto });
        TextBlock Metric(int column, string title)
        {
            var value = Text("—", 24);
            var stack = new StackPanel { Spacing = 5 }; stack.Children.Add(Text(title, 12, true)); stack.Children.Add(value);
            if (column == 0)
            {
                benchmarkPercent = Text("—", 12, true); stack.Children.Add(benchmarkPercent);
                ToolTipService.SetToolTip(stack, L("S : 100 % · A : ≥ 80 % · B : ≥ 60 % · C : ≥ 40 % · D : ≥ 20 % · E : > 0 % · F : 0 %", "S: 100% · A: ≥ 80% · B: ≥ 60% · C: ≥ 40% · D: ≥ 20% · E: > 0% · F: 0%"));
            }
            At(cards, Card(stack), 0, column); return value;
        }
        benchmarkGrade = Metric(0, L("Note de réussite", "Success grade"));
        benchmarkSpeed = Metric(1, L("Débit moyen · tok/s", "Average · tok/s"));
        benchmarkTokens = Metric(2, L("Tokens utilisés", "Tokens used"));
        benchmarkLogic = Metric(3, L("Logique", "Logic"));
        benchmarkBugs = Metric(4, L("Code / visuels", "Code / visual"));
        cards.SizeChanged += (_, _) =>
        {
            var columns = cards.ActualWidth < 700 ? 3 : 5;
            for (var i = 0; i < 5; i++)
            {
                cards.ColumnDefinitions[i].Width = i < columns ? new(1, GridUnitType.Star) : new(0);
                Grid.SetColumn((FrameworkElement)cards.Children[i], i % columns); Grid.SetRow((FrameworkElement)cards.Children[i], i / columns);
            }
        };
        overview.Children.Add(cards);
        benchmarkDescription = Text("", 13, true); overview.Children.Add(benchmarkDescription);
        overview.Children.Add(Text(L(
            "Difficile : ARC-AGI-2, optimisation et contraintes générées. Applications : code HTML exécuté dans l’aperçu hors ligne, contrôles fonctionnels indépendants. Le rendu reste à examiner. Débit avec latence ; tarification habituelle du fournisseur.",
            "Hard: ARC-AGI-2, generated optimization and constraints. Applications: HTML code runs in an offline preview with independent functional checks. Inspect rendering separately. Throughput includes latency; usual provider pricing."), 12, true));
        benchmarkProgress = Text(L("Prêt à lancer les 7 épreuves.", "Ready to run all 7 challenges."), 14);
        overview.Children.Add(benchmarkProgress);
        var scrollContent = new StackPanel { Spacing = 16 }; scrollContent.Children.Add(overview); scrollContent.Children.Add(benchmarkRows);
        At(body, new ScrollViewer { Content = scrollContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch }, 0);
        run = Button(L("Lancer le benchmark", "Run benchmark"), RunBenchmark, true);
        copyBenchmark = Copy(() => benchmarkReport?.Json() ?? "", L("Copier le rapport JSON", "Copy JSON report"));
        secondaryActions = copyBenchmark;
        benchmarkLayout = new Grid { ColumnSpacing = 18 };
        benchmarkLayout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        benchmarkLayout.ColumnDefinitions.Add(new() { Width = new(0) });
        At(benchmarkLayout, body, 0);
        // A Grid gives the native WebView a finite height; never put it in an unconstrained StackPanel.
        var previewGrid = new Grid { RowSpacing = 10 };
        previewGrid.RowDefinitions.Add(new() { Height = GridLength.Auto }); previewGrid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); previewGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        benchmarkPreviewTitle = Text(L("Aperçu de l’application", "Application preview"), 18);
        var previewHeading = new StackPanel { Spacing = 4 }; previewHeading.Children.Add(benchmarkPreviewTitle);
        previewHeading.Children.Add(Text(L("Rendu produit par le modèle · glisser, cliquer et zoomer après les vérifications.", "Model-generated result · drag, click and zoom after checks finish."), 12, true));
        At(previewGrid, previewHeading, 0); At(previewGrid, benchmarkPreviewHost, 1);
        reloadBenchmarkPreview = Button(L("Recharger", "Reload"), async () => { if (benchmarkPreviewHtml != null) await ShowBenchmarkPreview(benchmarkPreviewHtml, benchmarkPreviewTitle.Text, CancellationToken.None); });
        copyBenchmarkHtml = Copy(() => benchmarkPreviewHtml ?? "", L("Copier le HTML", "Copy HTML"));
        At(previewGrid, Row(reloadBenchmarkPreview, copyBenchmarkHtml), 2);
        At(benchmarkLayout, previewGrid, 0, 1);
        previewGrid.Visibility = Visibility.Collapsed;
        previewGrid.Tag = "benchmark-preview";
        refreshActions = () =>
        {
            run.IsEnabled = !Busy && model.SelectedItem != null;
            copyBenchmark.IsEnabled = benchmarkReport?.Entries.Count > 0;
            reloadBenchmarkPreview.IsEnabled = !Busy && benchmarkPreviewHtml != null;
            copyBenchmarkHtml.IsEnabled = benchmarkPreviewHtml != null;
        };
        benchmarkLevel.SelectionChanged += (_, _) => ResetBenchmark();
        benchmarkScope.SelectionChanged += (_, _) => ResetBenchmark();
        benchmarkApplication.SelectionChanged += (_, _) => ResetBenchmark();
        benchmarkSeed.ValueChanged += (_, _) => ResetBenchmark();
        ResetBenchmark();
        return benchmarkLayout;
    }

    void ResetBenchmark()
    {
        foreach (var button in benchmarkReplayButtons) busyControls.Remove(button); benchmarkReplayButtons.Clear();
        var reasoning = benchmarkScope.SelectedIndex == 1 ? [] : ModelBenchmarkLevels.Cases(BenchmarkLevel, BenchmarkSeed);
        var applications = benchmarkApplication.SelectedIndex <= 0 ? VisualBenchmarkTask.Cases : [VisualBenchmarkTask.Cases[benchmarkApplication.SelectedIndex - 1]];
        currentBenchmarkCases = benchmarkScope.SelectedIndex == 0 ? reasoning : reasoning.Concat(applications).ToArray();
        benchmarkApplication.Visibility = benchmarkScope.SelectedIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        benchmarkReport = null; benchmarkEntries.Clear(); benchmarkRows.Children.Clear(); usage.Text = "";
        benchmarkPreview?.ClosePreview(); benchmarkPreview = null; benchmarkPreviewHtml = null;
        benchmarkPreviewHost.Children.Clear(); benchmarkPreviewHost.Children.Add(Text(L("L’application générée apparaîtra ici.", "The generated application will appear here."), 16, true));
        SetBenchmarkPreviewVisible(benchmarkScope.SelectedIndex != 0);
        foreach (var item in BenchmarkCases) benchmarkRows.Children.Add(Card(Text(CaseTitle(item) + " · " + L("En attente", "Pending"), 14, true)));
        benchmarkGrade.Text = benchmarkPercent.Text = benchmarkSpeed.Text = benchmarkTokens.Text = benchmarkLogic.Text = benchmarkBugs.Text = "—";
        benchmarkDescription.Text = L($"{BenchmarkCases.Count} requêtes · {BenchmarkCases.Count(c => c.Category == "logic")} logique · {BenchmarkCases.Count(c => c.Category == "bug")} code · {BenchmarkCases.Count(c => c.Category == "visual")} applications · série {BenchmarkSeed}",
            $"{BenchmarkCases.Count} requests · {BenchmarkCases.Count(c => c.Category == "logic")} logic · {BenchmarkCases.Count(c => c.Category == "bug")} code · {BenchmarkCases.Count(c => c.Category == "visual")} applications · seed {BenchmarkSeed}");
        benchmarkProgress.Text = L("Prêt à lancer les épreuves.", "Ready to run the challenges.");
        SetNotice(""); refreshActions();
    }

    void SetBenchmarkPreviewVisible(bool visible)
    {
        var pane = (FrameworkElement)benchmarkLayout.Children[1];
        pane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        benchmarkLayout.ColumnDefinitions[1].Width = visible ? new(1.1, GridUnitType.Star) : new(0);
        var changed = benchmarkPreviewVisible != visible; benchmarkPreviewVisible = visible;
        if (changed && Panel.XamlRoot != null) BenchmarkWindowPlacement.Fit(this, visible);
    }

    async Task ShowBenchmarkPreview(string html, string title, CancellationToken ct)
    {
        SetBenchmarkPreviewVisible(true); benchmarkPreviewTitle.Text = title;
        if (!ToolWebPreviewAllowed)
        {
            benchmarkPreview?.ClosePreview(); benchmarkPreview = null;
            benchmarkPreviewHtml = html; benchmarkPreviewHost.Children.Clear();
            var native = new Grid { RowSpacing = 8 };
            native.RowDefinitions.Add(new() { Height = GridLength.Auto }); native.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
            native.Children.Add(Text(L("Code consultable sans navigateur. L’application HTML n’est pas exécutée.", "Source is available without a browser. The HTML application is not executed."), 13, true));
            var code = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
                Text = html[..Math.Min(40000, html.Length)] + (html.Length > 40000 ? L("\n… Aperçu limité ; Copier le HTML conserve le code complet.", "\n… Preview truncated; Copy HTML keeps the complete source.") : ""),
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas"), FontSize = 12, VerticalAlignment = VerticalAlignment.Stretch };
            At(native, code, 1); benchmarkPreviewHost.Children.Add(native);
            benchmarkPreviewTitle.Text = title + L(" · code source", " · source"); refreshActions(); return;
        }
        // A fresh frame on every load clears timers, prior applications and grading state.
        benchmarkPreview?.ClosePreview(); benchmarkPreview = new(); benchmarkPreviewHost.Children.Clear(); benchmarkPreviewHost.Children.Add(benchmarkPreview);
        benchmarkPreviewHtml = html; refreshActions();
        await benchmarkPreview.LoadAsync(html, ct);
    }

    static string CaseTitle(ModelBenchmarkCase item) => L(item.FrenchTitle, item.EnglishTitle);

    static string BenchmarkDuration(double seconds)
    {
        if (seconds < 60) return $"{seconds:F1} s";
        var elapsed = TimeSpan.FromSeconds(Math.Round(seconds));
        return elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours} h {elapsed.Minutes:00} min {elapsed.Seconds:00} s"
            : $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds:00} s";
    }

    Task RunBenchmark() => StartOperation(async (selected, ct) =>
    {
        if (!ToolWebPreviewAllowed && BenchmarkCases.Any(c => c.Visual != null))
            throw new InvalidOperationException(L("Les épreuves interactives nécessitent WebView. Choisissez Raisonnement et code, ou activez Autoriser les aperçus WebView dans les outils dans Réglages / Général sur un poste compatible.", "Interactive challenges require WebView. Choose Reasoning and code, or enable Allow WebView previews in tools in Settings / General on a compatible device."));
        foreach (var button in benchmarkReplayButtons) busyControls.Remove(button); benchmarkReplayButtons.Clear();
        benchmarkEntries.Clear(); benchmarkRows.Children.Clear();
        var cases = BenchmarkCases;
        benchmarkReport = new(ModelBenchmark.SuiteVersion, GitHubUpdates.CurrentVersion, DateTimeOffset.UtcNow,
            selected.Name + " #" + selected.Id, selected.Model, [], false, ModelBenchmarkLevels.Id(BenchmarkLevel), cases.Count(c => c.IsScored), BenchmarkSeed, BenchmarkScope, BenchmarkTimeout, cases.Select(c => c.Id).ToArray());
        UpdateBenchmarkStats();
        try
        {
            foreach (var item in cases)
            {
                ct.ThrowIfCancellationRequested();
                benchmarkProgress.Text = $"{benchmarkEntries.Count + 1}/{cases.Count} · {CaseTitle(item)}";
                var heading = Text(CaseTitle(item) + " · " + L("En cours…", "Running…"), 15);
                var elapsed = Stopwatch.StartNew();
                var response = Text(L("En attente des premiers éléments du modèle…", "Waiting for the model's first output…"), 13, true);
                response.IsTextSelectionEnabled = true;
                var reasoningTitle = Text(L("Réflexion en cours…", "Reasoning in progress…"), 12, true);
                var reasoning = Text("", 13, true); reasoning.IsTextSelectionEnabled = true;
                var reasoningPanel = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };
                reasoningPanel.Children.Add(reasoningTitle); reasoningPanel.Children.Add(reasoning);
                void RenderResponse(GenerationUpdate update)
                {
                    var hasReasoning = !string.IsNullOrWhiteSpace(update.Reasoning);
                    reasoningPanel.Visibility = hasReasoning ? Visibility.Visible : Visibility.Collapsed;
                    reasoningTitle.Text = update.Text.Length == 0 ? L("Réflexion en cours…", "Reasoning in progress…") : L("Réflexion du modèle", "Model reasoning");
                    // Keep recent reasoning visible during long generations; retain full text in the report.
                    reasoning.Text = update.Reasoning.Length > 14000 ? "…\n" + update.Reasoning[^14000..] : update.Reasoning;
                    response.Visibility = hasReasoning && update.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                    response.Text = update.Text.Length > 14000 ? update.Text[..14000] + L("\n… Réponse complète dans le rapport.", "\n… Full response in report.")
                        : update.Text.Length > 0 ? update.Text : L("En attente de la réponse du modèle…", "Waiting for the model's response…");
                }
                var details = new StackPanel { Spacing = 8 };
                if (item.SourceUrl != null)
                {
                    details.Children.Add(new HyperlinkButton { Content = item.Source + " · " + item.SourceItem, NavigateUri = new Uri(item.SourceUrl) });
                    details.Children.Add(Text(item.Adaptation ?? "", 12, true));
                }
                details.Children.Add(Text(L("Consigne", "Prompt"), 12, true)); details.Children.Add(Text(item.Prompt, 13));
                details.Children.Add(Text(L("Réponse du modèle", "Model response"), 12, true));
                details.Children.Add(reasoningPanel); details.Children.Add(response);
                var expand = new Expander { Header = heading, Content = details, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                benchmarkRows.Children.Add(expand);
                ModelToolResult? result = null; bool? passed = null; string? error = null;
                IReadOnlyList<BenchmarkCheck>? checks = null; string? previewHtml = null;
                try
                {
                    if (item.Visual != null)
                    {
                        SetBenchmarkPreviewVisible(true); benchmarkPreviewTitle.Text = CaseTitle(item) + L(" · génération…", " · generating…");
                        benchmarkPreview?.ClosePreview(); benchmarkPreview = null; benchmarkPreviewHtml = null;
                        benchmarkPreviewHost.Children.Clear(); benchmarkPreviewHost.Children.Add(Text(L("Le modèle construit l’application…", "The model is building the application…"), 16, true)); refreshActions();
                    }
                    result = await Request(selected, item.Request with { TimeoutSeconds = BenchmarkTimeout,
                        MaxResponseCharacters = BenchmarkLevel == ModelBenchmarkLevel.Hard ? 600_000 : item.Request.MaxResponseCharacters },
                        null, ct, RenderResponse);
                    reasoningTitle.Text = L("Réflexion du modèle", "Model reasoning");
                    if (item.Visual != null)
                    {
                        previewHtml = VisualBenchmarkTask.ExtractHtml(result.Text);
                        await ShowBenchmarkPreview(previewHtml, CaseTitle(item) + L(" · vérifications…", " · checking…"), ct);
                        benchmarkPreviewHost.IsHitTestVisible = false;
                        try
                        {
                            if (!ToolWebPreviewAllowed || benchmarkPreview == null) throw new InvalidOperationException(L("Vérifications interactives indisponibles : WebView est désactivé.", "Interactive checks unavailable: WebView is disabled."));
                            var judgment = await VisualBenchmarkRunner.RunAsync(item.Visual, BenchmarkLevel, BenchmarkSeed, benchmarkPreview!.InvokeAsync, ct);
                            checks = judgment.Checks; passed = judgment.Passed;
                        }
                        finally { benchmarkPreviewHost.IsHitTestVisible = true; }
                        benchmarkPreviewTitle.Text = CaseTitle(item) + L(" · rendu à examiner", " · inspect the result");
                    }
                    else if (item.Validator != null)
                    {
                        var judgment = item.Validator(ModelUtilityPrompts.ParseObject(result.Text)); checks = judgment.Checks; passed = judgment.Passed;
                    }
                    else passed = item.Grade(result.Text);
                    heading.Text = CaseTitle(item) + " · " + (passed == null ? L("Mesuré", "Measured") : passed == true ? L("Réussi", "Passed") : L("Échec", "Failed"));
                    details.Children.Add(Text(Metrics(result), 12, true));
                }
                catch (OperationCanceledException)
                {
                    error = L("Annulé : réponse partielle, métriques indisponibles.", "Cancelled: partial response, metrics unavailable.");
                    heading.Text = CaseTitle(item) + " · " + L("Annulé", "Cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    error = ex.Message; heading.Text = CaseTitle(item) + " · " + L("Erreur", "Error");
                    var errorText = Text(error, 13); errorText.Foreground = FluentDesign.Resource("ToolMessageErrorStrokeBrush");
                    details.Children.Add(errorText);
                }
                finally
                {
                    elapsed.Stop();
                    heading.Text += " · " + BenchmarkDuration(elapsed.Elapsed.TotalSeconds);
                    reasoningTitle.Text = L("Réflexion du modèle", "Model reasoning");
                    ToolTipService.SetToolTip(heading, L("Durée totale de l’épreuve, génération et vérifications comprises.", "Total challenge duration, including generation and checks."));
                    if (checks != null)
                        foreach (var check in checks) details.Children.Add(Text((check.Passed ? "✓ " : "✗ ") + check.Name + (check.Detail.Length > 0 ? " · " + check.Detail : ""), 13));
                    if (previewHtml != null)
                    {
                        var savedHtml = previewHtml;
                        var replay = Button(L("Voir cet aperçu", "Show this preview"), () => ShowBenchmarkPreview(savedHtml, CaseTitle(item), CancellationToken.None));
                        replay.IsEnabled = !Busy; busyControls.Add(replay); benchmarkReplayButtons.Add(replay); details.Children.Add(replay);
                        details.Children.Add(Text(L("La note porte sur les contrôles fonctionnels. Inspectez séparément le rendu, les gestes et l’ergonomie.", "The grade covers functional checks. Inspect rendering, gestures and usability separately."), 12, true));
                    }
                    var expected = item.ExpectedJson ?? "";
                    if (item.AcceptedPatches != null) expected += "\n" + string.Join("\n", item.AcceptedPatches);
                    if (expected.Length > 0)
                    {
                        details.Children.Add(Text(L("Correction attendue", "Expected answer"), 12, true));
                        details.Children.Add(Text(expected + "\n" + item.Explanation, 13));
                    }
                    benchmarkEntries.Add(new(item.Id, item.Category, CaseTitle(item), item.Prompt, result, passed, error, expected, item.Explanation,
                        item.Source, item.SourceUrl, item.SourceItem, item.Adaptation, checks, previewHtml, elapsed.Elapsed.TotalSeconds));
                    UpdateBenchmarkStats();
                }
            }
            benchmarkReport = benchmarkReport! with { Completed = true };
            benchmarkProgress.Text = L("Benchmark terminé", "Benchmark complete") + $" · {selected.Name} · {selected.Model}";
            var failures = benchmarkEntries.Count(e => e.Error != null);
            SetNotice(failures == 0 ? L("Rapport prêt. Développez chaque épreuve pour consulter sa réponse et son corrigé.", "Report ready. Expand each challenge to inspect its answer and expected solution.")
                : L($"Terminé avec {failures} erreur(s) pendant le benchmark. Les métriques concernent les réponses complètes uniquement.", $"Finished with {failures} benchmark error(s). Metrics cover completed responses only."), failures > 0);
        }
        finally
        {
            if (ct.IsCancellationRequested) benchmarkProgress.Text = L("Benchmark interrompu", "Benchmark interrupted");
            UpdateBenchmarkStats();
        }
    });

    void UpdateBenchmarkStats()
    {
        if (benchmarkReport == null) return;
        benchmarkReport = benchmarkReport with { Entries = benchmarkEntries.ToArray() };
        benchmarkGrade.Text = benchmarkReport.Grade;
        benchmarkPercent.Text = benchmarkReport.SuccessPercent is { } percent ? $"{percent:F0} %" + (benchmarkReport.Provisional ? L(" · provisoire", " · provisional") : "") : "—";
        var prefix = benchmarkReport.Estimated ? "≈ " : "";
        benchmarkSpeed.Text = prefix + benchmarkReport.TokensPerSecond.ToString("F1");
        benchmarkTokens.Text = prefix + (benchmarkReport.InputTokens + benchmarkReport.OutputTokens).ToString("N0");
        string Score(string category) => BenchmarkCases.Any(c => c.Category == category) ? $"{benchmarkEntries.Count(e => e.Category == category && e.Passed == true)} / {BenchmarkCases.Count(c => c.Category == category)}" : "—";
        benchmarkLogic.Text = Score("logic");
        benchmarkBugs.Text = $"{benchmarkEntries.Count(e => (e.Category is "bug" or "visual") && e.Passed == true)} / {BenchmarkCases.Count(c => c.Category is "bug" or "visual")}";
        usage.Text = L("Entrée", "Input") + $" {benchmarkReport.InputTokens:N0} · " + L("Sortie", "Output") + $" {benchmarkReport.OutputTokens:N0} tokens · {benchmarkReport.Seconds:F1} s" +
            (benchmarkReport.Estimated ? L(" · ≈ compteurs estimés", " · ≈ estimated counts") : "");
        refreshActions();
    }
}
