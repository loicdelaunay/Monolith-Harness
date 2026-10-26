using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

internal sealed partial class ModelToolsWindow
{
    readonly CheckBox compareReasoning = new() { Content = L("Comparer les niveaux de réflexion", "Compare thinking levels") };
    readonly NumberBox compareRepetitions = new() { Header = L("Répétitions par épreuve", "Repetitions per challenge"), Minimum = 1, Maximum = 5, Value = 1, SmallChange = 1, Width = 180 };
    readonly TextBlock comparisonInfo = Text("", 12, true);
    readonly StackPanel comparisonLevels = new() { Spacing = 2 };
    readonly List<(string Level, CheckBox Check)> comparisonChoices = [];
    FrameworkElement? benchmarkMetrics;
    ReasoningBenchmarkReport? reasoningComparison;
    string[] ComparedLevels => comparisonChoices.Where(x => x.Check.IsChecked == true).Select(x => x.Level).ToArray();
    int ComparisonRepetitions => double.IsFinite(compareRepetitions.Value) ? (int)Math.Clamp(compareRepetitions.Value, 1, 5) : 1;

    UIElement BuildReasoningComparisonOptions()
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(compareReasoning); panel.Children.Add(comparisonLevels); panel.Children.Add(compareRepetitions); panel.Children.Add(comparisonInfo);
        busyControls.Add(compareReasoning); busyControls.Add(compareRepetitions);
        compareReasoning.Checked += (_, _) => RefreshComparisonInfo(); compareReasoning.Unchecked += (_, _) => RefreshComparisonInfo();
        compareRepetitions.ValueChanged += (_, _) => RefreshComparisonInfo();
        return panel;
    }
    void RefreshComparisonLevels()
    {
        if (kind != ModelToolKind.Benchmark || model.SelectedItem is not ToolModelChoice choice) return;
        var previous = ComparedLevels;
        foreach (var item in comparisonChoices) busyControls.Remove(item.Check);
        comparisonChoices.Clear(); comparisonLevels.Children.Clear();
        foreach (var level in ReasoningCapabilities.For(choice.Provider, choice.Model).Levels)
        {
            var check = new CheckBox { Content = ReasoningCapabilities.Label(level, UiText.Language == "fr"), IsChecked = previous.Length == 0 || previous.Contains(level) };
            check.Checked += (_, _) => RefreshComparisonInfo(); check.Unchecked += (_, _) => RefreshComparisonInfo();
            comparisonChoices.Add((level, check)); comparisonLevels.Children.Add(check); busyControls.Add(check);
        }
        RefreshComparisonInfo();
    }
    void RefreshComparisonInfo()
    {
        if (benchmarkLevel == null) return;
        var active = compareReasoning.IsChecked == true;
        benchmarkScope.IsEnabled = !Busy && !active;
        benchmarkApplication.Visibility = !active && benchmarkScope.SelectedIndex != 0 ? Visibility.Visible : Visibility.Collapsed;
        if (benchmarkMetrics != null) benchmarkMetrics.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        if (active && benchmarkLayout != null) SetBenchmarkPreviewVisible(false);
        comparisonLevels.Visibility = compareRepetitions.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        var count = ModelBenchmarkLevels.Cases(BenchmarkLevel, BenchmarkSeed).Count(x => x.Visual == null);
        if (active && benchmarkDescription != null) benchmarkDescription.Text = L("Mesures par niveau sur les mêmes épreuves de raisonnement et de code.", "Per-level measurements on identical reasoning and coding tasks.");
        comparisonInfo.Text = active ? L($"{ComparedLevels.Length} niveaux × {count} épreuves identiques × {ComparisonRepetitions} répétition(s) = {ComparedLevels.Length * count * ComparisonRepetitions} requêtes. Facturation habituelle du fournisseur. Qualité : réussite aux épreuves vérifiables ; ni note générale ni appréciation esthétique.",
            $"{ComparedLevels.Length} levels × {count} identical tasks × {ComparisonRepetitions} repetition(s) = {ComparedLevels.Length * count * ComparisonRepetitions} requests. Standard provider billing. Quality means verifiable task success, not a general or visual quality score.") : "";
    }
    async Task RunReasoningComparison(Provider selected, CancellationToken ct)
    {
        var levels = ComparedLevels; if (levels.Length < 2) throw new InvalidOperationException(L("Sélectionnez au moins deux niveaux disponibles.", "Select at least two available levels."));
        var cases = ModelBenchmarkLevels.Cases(BenchmarkLevel, BenchmarkSeed).Where(x => x.Visual == null).ToArray();
        var entries = new List<ReasoningBenchmarkEntry>();
        reasoningComparison = new("monolith-reasoning-comparison-v1", GitHubUpdates.CurrentVersion, selected.Name, selected.Model,
            DateTimeOffset.UtcNow, BenchmarkSeed, ModelBenchmarkLevels.Id(BenchmarkLevel), ComparisonRepetitions, BenchmarkTimeout, levels, cases, [], false);
        benchmarkReport = null; benchmarkEntries.Clear(); benchmarkRows.Children.Clear();
        SetBenchmarkPreviewVisible(false);
        benchmarkGrade.Text = benchmarkPercent.Text = benchmarkSpeed.Text = benchmarkTokens.Text = benchmarkLogic.Text = benchmarkBugs.Text = "—";
        var summary = new StackPanel { Spacing = 6 }; benchmarkRows.Children.Add(summary);
        var planned = levels.Length * cases.Length * ComparisonRepetitions;
        try
        {
            for (var repetition = 0; repetition < ComparisonRepetitions; repetition++)
                for (var index = 0; index < cases.Length; index++)
                    for (var offset = 0; offset < levels.Length; offset++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var item = cases[index]; var level = levels[(offset + index + repetition) % levels.Length];
                        benchmarkProgress.Text = $"{entries.Count + 1}/{planned} · {CaseTitle(item)} · {ReasoningCapabilities.Label(level, UiText.Language == "fr")} · {repetition + 1}/{ComparisonRepetitions}";
                        ModelToolResult? result = null; bool? passed = null; string? error = null;
                        var watch = Stopwatch.StartNew();
                        try
                        {
                            result = await Request(selected, item.Request with { ThinkingLevel = level, TimeoutSeconds = BenchmarkTimeout }, null, ct);
                            passed = item.Grade(result.Text);
                        }
                        catch (OperationCanceledException) { error = L("Annulé", "Cancelled"); throw; }
                        catch (Exception ex) { error = ex.Message; }
                        finally
                        {
                            entries.Add(new(item.Id, CaseTitle(item), item.Prompt, repetition + 1, level, result, passed, error, watch.Elapsed.TotalSeconds));
                            reasoningComparison = reasoningComparison with { Entries = entries.ToArray() };
                            RenderComparisonSummary(summary);
                            var content = new StackPanel { Spacing = 6 };
                            content.Children.Add(Text(result?.ReasoningSelection?.Describe(UiText.Language == "fr") ?? error ?? "", 12, true));
                            content.Children.Add(Text(item.Prompt, 12, true));
                            content.Children.Add(Text(result == null ? error ?? "" : Metrics(result), 12, true));
                            var answer = Text(result?.Text ?? "", 13); answer.IsTextSelectionEnabled = true; content.Children.Add(answer);
                            if (result?.Reasoning.Length > 0) content.Children.Add(Text(result.Reasoning, 12, true));
                            if (item.ExpectedJson != null) content.Children.Add(Text(L("Réponse attendue : ", "Expected answer: ") + item.ExpectedJson, 12, true));
                            benchmarkRows.Children.Add(new Expander { Header = CaseTitle(item) + " · " + ReasoningCapabilities.Label(level, UiText.Language == "fr") + " · " +
                                (error != null ? L("Erreur", "Error") : passed == true ? L("Réussi", "Passed") : passed == false ? L("Échec", "Failed") : L("Mesuré", "Measured")),
                                Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
                            copyBenchmark.IsEnabled = true;
                        }
                    }
            reasoningComparison = reasoningComparison with { Completed = true };
            benchmarkProgress.Text = L("Comparatif terminé. Rapport et réponses disponibles.", "Comparison complete. Report and responses available.");
        }
        finally
        {
            if (ct.IsCancellationRequested) benchmarkProgress.Text = L("Comparatif interrompu ; résultats partiels conservés.", "Comparison stopped; partial results retained.");
        }
    }
    void RenderComparisonSummary(StackPanel host)
    {
        host.Children.Clear(); if (reasoningComparison == null) return;
        host.Children.Add(Text(L("Comparaison sur les mêmes épreuves", "Comparison on identical challenges"), 18));
        static string Timing(double? value) => value.HasValue ? value.Value.ToString("F2") + " s" : "—";
        foreach (var row in reasoningComparison.Summaries)
        {
            var text = L("Qualité", "Quality") + $" {row.Passed}/{row.Graded} · " + L("Délai moyen", "Average time") + " " + Timing(row.AverageSeconds) + " · " +
                L("Premier élément", "First element") + " " + Timing(row.AverageFirstElementSeconds) + "\n" +
                $"{row.InputTokens:N0} → {row.OutputTokens:N0} tokens" + (row.Estimated ? " ≈" : "") +
                " · cache : " + (row.CachedInputTokens?.ToString("N0") ?? "?") + $" · {row.Errors} " + L("erreur(s)", "error(s)") +
                $" · {row.Fallbacks} " + L("repli(s)", "fallback(s)") + " · " + L("Envoyé : ", "Sent: ") + string.Join(", ", row.SentLevels.Select(x => ReasoningCapabilities.Label(x, UiText.Language == "fr")));
            var line = new StackPanel { Spacing = 3 }; line.Children.Add(Text(ReasoningCapabilities.Label(row.Level, UiText.Language == "fr"), 15)); line.Children.Add(Text(text, 12, true));
            host.Children.Add(Card(line));
        }
        host.Children.Add(Text(L("Métriques des réponses complètes uniquement ; les erreurs comptent comme échecs pour les tâches notées. L’ordre des niveaux varie entre les épreuves. Cache, charge du fournisseur et variabilité du modèle peuvent influencer les résultats. Les reprises réseau sont désactivées.",
            "Metrics cover completed responses only; errors count as failures on graded tasks. Level order rotates across challenges. Caching, provider load and model variability may affect results. Network retries are disabled."), 12, true));
    }
}
