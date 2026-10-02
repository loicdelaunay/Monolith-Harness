using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using Windows.ApplicationModel.DataTransfer;

namespace MonolithHarness.App;

internal sealed partial class DashboardWindow
{
    readonly TextBlock speedAverage = Text("—", 24), speedMinimum = Text("—", 24), speedMaximum = Text("—", 24), speedCalls = Text("0", 24);
    readonly TextBlock speedPeriod = Text("", 12, true), speedEmpty = Text("", 14, true), speedExcluded = Text("", 12, true), speedDetailCount = Text("", 12, true);
    readonly ModelSpeedTimelineChart speedTimeline = new();
    readonly StackPanel speedModelBars = new() { Spacing = 13 }, speedRows = new() { Spacing = 2 };
    Button speedPrevious = null!, speedNext = null!;
    ModelSpeedReport? speedReport;
    int speedPage;

    UIElement BuildSpeedPage()
    {
        var content = new StackPanel { Spacing = 18 };
        content.Children.Add(speedPeriod);
        var average = Stat(L("Débit moyen", "Average throughput"), speedAverage);
        ToolTipService.SetToolTip(average, L("Total des tokens de sortie ÷ total des durées des appels retenus.", "Total output tokens ÷ total duration of included calls."));
        var minimum = Stat(L("Débit minimal", "Minimum throughput"), speedMinimum);
        var maximum = Stat(L("Débit maximal", "Maximum throughput"), speedMaximum);
        ToolTipService.SetToolTip(minimum, L("Débit moyen de l’appel le plus lent.", "Average throughput of the slowest call."));
        ToolTipService.SetToolTip(maximum, L("Débit moyen de l’appel le plus rapide.", "Average throughput of the fastest call."));
        content.Children.Add(Cards([average, minimum, maximum, Stat(L("Appels mesurés", "Measured calls"), speedCalls)]));
        content.Children.Add(speedEmpty);
        var overTime = FluentDesign.Surface(Stack(Text(L("Évolution du débit", "Throughput timeline"), 17),
            Text(L("Débit moyen · tokens de sortie par seconde", "Average throughput · output tokens per second"), 12, true), speedTimeline), 16);
        var modelScroll = new ScrollViewer { Content = speedModelBars, MaxHeight = 300,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var byModel = FluentDesign.Surface(Stack(Text(L("Vitesse par modèle", "Speed by model"), 17),
            Text(L("Débit moyen · tok/s", "Average throughput · tok/s"), 12, true), modelScroll), 16);
        content.Children.Add(Charts(overTime, byModel));
        content.Children.Add(Text(L("Le débit est calculé à partir des tokens de sortie et de la durée totale de chaque appel, attente et réflexion incluses. La moyenne est pondérée par cette durée. ≈ indique un compteur de sortie estimé. Les nouveaux appels apparaissent une fois terminés, avec actualisation toutes les 15 secondes.",
            "Throughput uses output tokens and the total duration of each call, including waiting and reasoning. The average is weighted by duration. ≈ marks an estimated output count. New calls appear when finished, with refresh every 15 seconds."), 12, true));
        content.Children.Add(speedExcluded);
        speedPrevious = Action(L("Précédent", "Previous"), () => { speedPage--; RenderSpeedDetails(); });
        speedNext = Action(L("Suivant", "Next"), () => { speedPage++; RenderSpeedDetails(); });
        var pagination = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        pagination.Children.Add(speedPrevious); pagination.Children.Add(speedNext);
        var table = Stack(TableRow([L("Réponse terminée", "Completed at"), L("Fournisseur / modèle", "Provider / model"), L("Usage", "Activity"),
            L("Projet / conversation", "Project / chat"), L("Sortie", "Output"), L("Durée", "Duration"), L("Débit · tok/s", "Rate · tok/s"), L("Mesure", "Measurement"), L("État", "Status")], true), speedRows);
        content.Children.Add(FluentDesign.Surface(Stack(Text(L("Détail des mesures", "Measurement details"), 17),
            new ScrollViewer { Content = table, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled },
            TwoColumns(speedDetailCount, pagination)), 16));
        return new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    static string RateText(double? rate, bool estimated, bool unit = true) => rate.HasValue
        ? (estimated ? "≈ " : "") + rate.Value.ToString("N1") + (unit ? " tok/s" : "") : "—";

    void RenderSpeed()
    {
        if (speedReport == null || report == null) return;
        var summary = speedReport.Summary;
        speedAverage.Text = RateText(summary.Average, summary.Estimates > 0);
        speedMinimum.Text = RateText(summary.Minimum, summary.Estimates > 0);
        speedMaximum.Text = RateText(summary.Maximum, summary.Estimates > 0);
        speedCalls.Text = $"{summary.Calls:N0}";
        speedPeriod.Text = $"{report.StartLocal:d} – {report.EndLocal.AddDays(-1):d} · " + L("heure locale", "local time") +
            $" · {summary.Estimates:N0} " + L("mesures avec estimation", "measurements with estimates");
        speedEmpty.Text = L("Aucune mesure de vitesse disponible pour ces filtres. Les appels terminés avec un modèle, des tokens de sortie et une durée valides apparaîtront ici.",
            "No speed measurements available for these filters. Completed calls with a model, output tokens and a valid duration will appear here.");
        speedEmpty.Visibility = summary.Calls == 0 ? Visibility.Visible : Visibility.Collapsed;
        speedExcluded.Text = $"{speedReport.ExcludedCalls:N0} " + L("appel(s) exclu(s) : erreur, interruption, modèle inconnu, sortie ou durée non mesurable.",
            "call(s) excluded: error, interruption, unknown model, or unmeasurable output or duration.");
        speedExcluded.Visibility = speedReport.ExcludedCalls > 0 ? Visibility.Visible : Visibility.Collapsed;
        speedTimeline.SetValues(speedReport.Timeline);
        RenderSpeedModels(); RenderSpeedDetails();
    }

    void RenderSpeedModels()
    {
        speedModelBars.Children.Clear(); if (speedReport == null) return;
        var maximum = Math.Max(1, speedReport.Models.Count == 0 ? 0 : speedReport.Models.Max(x => x.Summary.Average ?? 0));
        foreach (var group in speedReport.Models)
        {
            var summary = group.Summary;
            var name = ProviderName(group.Model) + " · " + group.Model.Model;
            var label = Text(name, 12); label.MaxLines = 2; label.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTipService.SetToolTip(label, name);
            var bar = new Grid { Height = 8, Background = FluentDesign.Resource("ControlSelectedBrush") };
            bar.ColumnDefinitions.Add(new() { Width = new(summary.Average ?? 0, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new() { Width = new(Math.Max(0, maximum - (summary.Average ?? 0)), GridUnitType.Star) });
            bar.Children.Add(new Border { Background = ConsumptionTimelineChart.InputBrush });
            var description = name + $"\n{L("Moyenne", "Average")}: {RateText(summary.Average, summary.Estimates > 0)}" +
                $"\n{L("Minimum", "Minimum")}: {RateText(summary.Minimum, summary.Estimates > 0)} · {L("Maximum", "Maximum")}: {RateText(summary.Maximum, summary.Estimates > 0)}" +
                $"\n{summary.Calls:N0} {L("appels", "calls")} · {summary.OutputTokens:N0} {L("tokens de sortie", "output tokens")}";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bar, description); ToolTipService.SetToolTip(bar, description);
            speedModelBars.Children.Add(Stack(TwoColumns(label, Text(RateText(summary.Average, summary.Estimates > 0), 12)), bar));
        }
        if (speedReport.Models.Count == 0) speedModelBars.Children.Add(Text(L("Aucune mesure sur cette période.", "No measurements for this period."), 13, true));
    }

    void RenderSpeedDetails()
    {
        speedRows.Children.Clear(); var entries = speedReport?.Entries ?? [];
        speedPage = Math.Clamp(speedPage, 0, Math.Max(0, (entries.Count - 1) / PageSize));
        foreach (var entry in entries.Skip(speedPage * PageSize).Take(PageSize))
            speedRows.Children.Add(TableRow([Local(entry.CompletedUtc).ToString("dd/MM/yyyy HH:mm:ss"), ProviderName(entry) + "\n" + entry.Model, ActivityName(entry.Activity),
                (entry.ProjectName.Length == 0 ? "—" : entry.ProjectName) + "\n" + (entry.ChatTitle.Length == 0 ? "—" : entry.ChatTitle), Count(entry.OutputTokens, entry.OutputEstimated),
                $"{entry.Seconds:F1} s", RateText(ModelSpeedReports.Rate(entry), entry.OutputEstimated, false),
                entry.OutputEstimated ? L("Estimation", "Estimate") : L("Déclaré", "Reported"), entry.Legacy ? L("Historique", "History") : Status(entry)]));
        speedDetailCount.Text = entries.Count == 0 ? L("Aucune mesure", "No measurements") :
            $"{speedPage * PageSize + 1:N0} – {Math.Min((speedPage + 1) * PageSize, entries.Count):N0} / {entries.Count:N0} " + L("mesures", "measurements");
        speedPrevious.IsEnabled = speedPage > 0; speedNext.IsEnabled = (speedPage + 1) * PageSize < entries.Count;
    }

    void CopySpeedCsv()
    {
        if (speedReport == null) return;
        static string Cell(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
        var header = "CompletedUtc,Provider,Model,Activity,Project,Chat,OutputTokens,OutputEstimated,Seconds,TokensPerSecond,Status,Legacy";
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var lines = speedReport.Entries.Select(x => string.Join(',', new[] { DateTime.SpecifyKind(x.CompletedUtc, DateTimeKind.Utc).ToString("O"),
            ProviderName(x), x.Model, x.Activity, x.ProjectName, x.ChatTitle, x.OutputTokens.ToString(culture), x.OutputEstimated.ToString(),
            x.Seconds.ToString(culture), ModelSpeedReports.Rate(x)!.Value.ToString(culture), x.Status, x.Legacy.ToString() }.Select(Cell)));
        var data = new DataPackage(); data.SetText(header + "\r\n" + string.Join("\r\n", lines)); Clipboard.SetContent(data);
        notice.Text = L("Mesures de vitesse copiées en CSV.", "Speed measurements copied as CSV."); notice.Foreground = FluentDesign.Secondary;
    }
}
