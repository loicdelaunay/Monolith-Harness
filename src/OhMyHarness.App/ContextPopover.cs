using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;
using System.Text.Json.Nodes;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    void AttachContextPopover(StackPanel anchor)
    {
        anchor.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        static TextBlock Text(double size = 13, bool secondary = false) => new() { FontSize = size,
            Foreground = secondary ? FluentDesign.Secondary : FluentDesign.Primary, TextWrapping = TextWrapping.Wrap };
        static Grid Columns()
        {
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            return grid;
        }
        var body = new Grid { RowSpacing = 14, Width = 400 };
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = Text(18); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        body.Children.Add(heading);
        var details = new StackPanel { Spacing = 16 };
        var scroll = new ScrollViewer { Content = details, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); body.Children.Add(scroll);
        var intro = Text(12, true); details.Children.Add(intro);

        var overview = new Grid { ColumnSpacing = 14, RowSpacing = 10 };
        overview.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        overview.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        overview.RowDefinitions.Add(new() { Height = GridLength.Auto });
        overview.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var chart = new ContextUsageChart { VerticalAlignment = VerticalAlignment.Center };
        overview.Children.Add(chart);
        var totals = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var source = Text(11);
        totals.Children.Add(new Border { Child = source, Padding = new(7, 3, 7, 3), CornerRadius = new(5),
            Background = FluentDesign.Resource("ControlSelectedBrush"), HorizontalAlignment = HorizontalAlignment.Left });
        var usedTitle = Text(12, true); var usedValue = Text(20);
        usedValue.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        totals.Children.Add(usedTitle); totals.Children.Add(usedValue);
        var remaining = Text(13); var capacity = Text(12, true);
        totals.Children.Add(remaining); totals.Children.Add(capacity);
        Grid.SetColumn(totals, 1); overview.Children.Add(totals); details.Children.Add(overview);

        var exchange = new Grid { ColumnSpacing = 16 };
        exchange.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        exchange.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var inputTitle = Text(12, true); var outputTitle = Text(12, true);
        var inputValue = Text(16); var outputValue = Text(16);
        var inputPanel = new StackPanel { Spacing = 4 }; inputPanel.Children.Add(inputTitle); inputPanel.Children.Add(inputValue);
        var outputPanel = new StackPanel { Spacing = 4 }; outputPanel.Children.Add(outputTitle); outputPanel.Children.Add(outputValue);
        exchange.Children.Add(inputPanel); Grid.SetColumn(outputPanel, 1); exchange.Children.Add(outputPanel);
        var lastCall = new StackPanel { Spacing = 10 }; var lastCallTitle = Text(13);
        lastCallTitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        lastCall.Children.Add(lastCallTitle); lastCall.Children.Add(exchange);
        details.Children.Add(FluentDesign.Surface(lastCall, 12));

        var history = new StackPanel { Spacing = 8 };
        var historyHeading = Text(13); historyHeading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        history.Children.Add(historyHeading);
        var rows = Enumerable.Range(0, 5).Select(_ => (Title: Text(), Value: Text())).ToArray();
        foreach (var row in rows)
        {
            var line = Columns(); line.Children.Add(row.Title);
            row.Value.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(row.Value, 1); line.Children.Add(row.Value); history.Children.Add(line);
        }
        var historyNote = Text(12, true); history.Children.Add(historyNote); details.Children.Add(history);
        var compactionNote = Text(12, true); var busyNote = Text(12, true);
        details.Children.Add(compactionNote); details.Children.Add(busyNote);
        var compact = new Button { Content = WorkflowText("Compacter maintenant", "Compact now"),
            HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        Grid.SetRow(compact, 2); body.Children.Add(compact);
        var flyout = new Flyout { Content = body };
        TextZoom.Observe(body);
        void Update()
        {
            var scale = TextZoom.ForWindow(root) / 100d;
            body.Width = Math.Max(180, Math.Min(400 * scale, root.ActualWidth - 64));
            scroll.MaxHeight = Math.Max(100, Math.Min(520 * scale, root.ActualHeight - 150 * scale));
            var narrow = body.Width < 300 * scale;
            chart.HorizontalAlignment = narrow ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            Grid.SetColumnSpan(chart, narrow ? 2 : 1);
            Grid.SetRow(totals, narrow ? 1 : 0); Grid.SetColumn(totals, narrow ? 0 : 1);
            Grid.SetColumnSpan(totals, narrow ? 2 : 1);
            var limit = ActiveRun?.Provider.ContextLimit ?? provider?.ContextLimit ?? 128000;
            var value = ContextDetails.From(VisibleHistory(), limit);
            var update = ActiveRun?.Update;
            var input = ActiveRun == null ? value.Input : update?.InputTokens;
            var output = ActiveRun == null ? value.Output : update?.OutputTokens;
            var used = ActiveRun?.Context?.Tokens ?? (update != null ? (update.InputTokens ?? ActiveRun?.InputEstimate ?? value.Used) + (update.OutputTokens ?? ContextWindow.EstimateText(update.Text + update.Reasoning)) : value.Used);
            var estimated = ActiveRun?.Context?.Estimated ?? (update == null ? value.Estimated : update.InputTokens == null || update.OutputTokens == null);
            var approximation = estimated ? "≈ " : "";
            var fraction = used / (double)Math.Max(1, limit);
            var percent = $"{fraction * 100:F1} %";
            heading.Text = WorkflowText("Utilisation du contexte", "Context usage");
            intro.Text = WorkflowText("Le contexte regroupe ce que le modèle reçoit pour préparer sa réponse.",
                "Context is the information the model receives to prepare its response.");
            source.Text = estimated ? WorkflowText("Estimation", "Estimate") : WorkflowText("Déclaré par le fournisseur", "Reported by the provider");
            usedTitle.Text = WorkflowText("Contexte utilisé", "Context used");
            usedValue.Text = $"{approximation}{used:N0} tokens";
            remaining.Text = WorkflowText("Disponible : ", "Available: ") + $"{approximation}{Math.Max(0, limit - used):N0} tokens";
            capacity.Text = WorkflowText("Capacité : ", "Capacity: ") + $"{limit:N0} tokens";
            chart.Update(fraction, approximation + percent, WorkflowText("utilisé", "used"),
                usedTitle.Text + " · " + usedValue.Text + " · " + percent + " · " + remaining.Text, scale);
            lastCallTitle.Text = WorkflowText("Dernier appel · tokens déclarés", "Last call · reported tokens");
            inputTitle.Text = WorkflowText("Entrée du modèle", "Model input");
            outputTitle.Text = WorkflowText("Sortie du modèle", "Model output");
            inputValue.Text = input?.ToString("N0") ?? "—"; outputValue.Text = output?.ToString("N0") ?? "—";
            var missing = WorkflowText("Le fournisseur n’a pas encore communiqué cette valeur.", "The provider has not reported this value yet.");
            ToolTipService.SetToolTip(inputValue, input == null ? missing : null);
            ToolTipService.SetToolTip(outputValue, output == null ? missing : null);
            historyHeading.Text = WorkflowText("Historique actif · estimations en tokens", "Active history · token estimates");
            rows[0].Title.Text = WorkflowText("Vos messages", "Your messages"); rows[0].Value.Text = $"{value.User:N0}";
            rows[1].Title.Text = WorkflowText("Réponses du modèle", "Model replies"); rows[1].Value.Text = $"{value.Assistant:N0}";
            rows[2].Title.Text = WorkflowText("Résultats d’outils", "Tool results"); rows[2].Value.Text = $"{value.Tools:N0}";
            rows[3].Title.Text = WorkflowText("Résumé de compactage", "Compaction summary"); rows[3].Value.Text = $"{value.Summary:N0}";
            rows[4].Title.Text = WorkflowText("Images (approx.)", "Images (approx.)"); rows[4].Value.Text = $"{value.Images:N0}";
            historyNote.Text = WorkflowText("Ces estimations excluent les consignes système et les définitions d’outils. Elles peuvent différer du total.",
                "These estimates exclude system instructions and tool definitions. They may differ from the total.");
            compactionNote.Text = WorkflowText("Compactage automatique à 95 % : le modèle résume l’historique pour libérer de la place. Cette opération peut consommer des tokens.",
                "Automatic compaction at 95%: the model summarizes history to free up space. This may consume tokens.");
            busyNote.Text = WorkflowText("Compactage disponible après la réponse.", "Compaction available after the response.");
            busyNote.Visibility = ActiveRun == null ? Visibility.Collapsed : Visibility.Visible;
            compact.Content = WorkflowText("Compacter maintenant", "Compact now");
            compact.IsEnabled = conversationReady && !conversationLoading && chat != null && provider != null && ActiveRun == null && VisibleHistory().Any(x => x.State == "complete" && x.Role != "compaction");
            TextZoom.SetWindow(body, TextZoom.ForWindow(root));
        }
        AttachHoverPopover(anchor, body, flyout, Update);
        compact.Click += async (_, _) => { flyout.Hide(); await Guard(CompactCurrentChat); };
    }

    async Task CompactCurrentChat()
    {
        if (!conversationReady || conversationLoading || chat == null || project == null || provider == null || ActiveRun != null) return;
        using var run = new ConversationRun(chat, project, provider, state, "", [],db.Providers.Local) { Messages = messages };
        using var consumption = TokenConsumption.Begin(run.Db.FilePath, "compaction", project, chat);
        conversationRuns.Add(run.Chat.Id, run); RefreshGenerationControls();
        var ct = run.Cancellation.Token;
        SetRunStatus(run, WorkflowText("Compactage manuel du contexte…", "Compacting context…"));
        try
        {
            var secret = KeyVault.Decrypt(run.Provider.ProtectedKey);
            var changed = await ContextDetails.CompactAsync(run, async (text, token) => {
                Completion result;
                if (run.Provider.IsOpenCode)
                {
                    await EnsureOpenCodeServerAsync(run.Provider, secret, token, run.Project);
                    var directory = OpenCodeDirectory(run.Project);
                    var isolated = new Provider { Id = run.Provider.Id, Name = run.Provider.Name, Kind = run.Provider.Kind, BaseUrl = run.Provider.BaseUrl, Username = run.Provider.Username, Model = run.Provider.Model, OpenCodeTools = false };
                    var session = await openCodeEngine.CreateSessionAsync(isolated, secret, directory, "Compactage manuel", token);
                    result = await openCodeEngine.PromptAsync(isolated, secret, directory, session, text, ContextDetails.SummaryInstruction, [], _ => { }, token, retrySettings: FeatureSettings.Read(run.Options.FeaturesJson));
                }
                else result = await engine.StreamAsync(run.Provider, secret, new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = ContextDetails.SummaryInstruction },
                    new JsonObject { ["role"] = "user", ["content"] = text }), [], _ => { }, token, retrySettings: FeatureSettings.Read(run.Options.FeaturesJson));
                return result.Message["content"]?.GetValue<string>() ?? "";
            }, ct);
            if (changed)
            {
                var summary = run.Db.Messages.Local.Last(x => x.Role == "compaction" && x.State == "complete");
                AddMessage("compaction", summary.Content, [], run.Messages);
            }
            SetRunStatus(run, changed ? WorkflowText("Contexte compacté.", "Context compacted.") : WorkflowText("Aucune réduction utile : historique conservé.", "No useful reduction: history preserved."), StatusKind.Notice);
        }
        catch (Exception ex) { run.Failed=true;SetRunStatus(run, ex is OperationCanceledException ? WorkflowText("Compactage arrêté.", "Compaction stopped.") : ex.Message,
            ex is OperationCanceledException ? StatusKind.Notice : StatusKind.Error); }
        finally
        {
            try { conversationHistory[run.Chat.Id] = await run.Db.Messages.AsNoTracking().Include(x => x.Attachments).Where(x => x.ChatId == run.Chat.Id).OrderBy(x => x.Id).ToListAsync(); }
            finally
            {
                conversationRuns.Remove(run.Chat.Id); RefreshGenerationControls();
                if (IsVisible(run)) RefreshContextInfo();
                await RefreshInboxAsync();
            }
        }
        if(!run.Failed&&!run.Cancellation.IsCancellationRequested)await RunNextQueuedAsync(run.Chat.Id,run.Messages);
    }
}
