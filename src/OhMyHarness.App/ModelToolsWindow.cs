using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;
using Windows.ApplicationModel.DataTransfer;

namespace OhMyHarness.App;

internal enum ModelToolKind { Translator, Proofreader, Benchmark }
internal sealed record ToolModelChoice(Provider Provider, string Model)
{
    public override string ToString() => $"{Provider.Name} · #{Provider.Id} · {Model}";
}

internal sealed partial class ModelToolsWindow : Window
{
    internal readonly Grid Panel = new() { Padding = new(24), RowSpacing = 16, Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush") };
    readonly ComboBox model = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
    readonly Button reload = null!;
    readonly TextBlock notice = Text("", 13, true);
    readonly TextBlock usage = Text("", 12, true);
    readonly ProgressBar progressBar = new() { IsIndeterminate = true, Height = 3, Visibility = Visibility.Collapsed };
    readonly Button stop;
    readonly List<Control> busyControls = [];
    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    readonly ModelToolClient client;
    readonly Func<FeatureSettings>? readRetrySettings;
    readonly Func<Task<List<Provider>>> loadModels;
    readonly Func<Provider, CancellationToken, Task> prepare;
    readonly ModelToolKind kind;
    readonly List<FormattedTextEditor> editors = [];
    UIElement? secondaryActions;
    bool noticeIsError;
    CancellationTokenSource? cancellation;
    internal Task? ActiveOperation { get; private set; }
    internal bool ClosedForTools { get; private set; }
    bool Busy => cancellation != null;
    Button run = null!;
    Action refreshActions = () => { };
    internal Func<Provider, ModelToolRequest, Action<GenerationUpdate>, CancellationToken, Task<ModelToolResult>>? SmokeRequest;

    internal static string L(string fr, string en) => UiText.Language == "en" ? en : fr;
    internal static string TitleFor(ModelToolKind kind) => kind switch
    { ModelToolKind.Translator => L("Traducteur", "Translator"), ModelToolKind.Proofreader => L("Correcteur d’orthographe", "Proofreader"), _ => L("Benchmark de modèle", "Model benchmark") };

    internal ModelToolsWindow(ModelToolKind kind, List<Provider> providers, int? providerId, string? selectedModel,
        ElementTheme theme, Func<Task<List<Provider>>> loadModels, Func<Provider, CancellationToken, Task> prepare, Func<FeatureSettings>? readRetrySettings = null)
    {
        this.kind = kind; this.loadModels = loadModels; this.prepare = prepare; client = new(http);
        this.readRetrySettings = readRetrySettings;
        Panel.RequestedTheme = theme;
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            Panel.RowDefinitions.Add(new() { Height = height });
        var heading = new StackPanel { Spacing = 4 };
        heading.Children.Add(Text(TitleFor(kind), 26));
        heading.Children.Add(Text(kind switch
        {
            ModelToolKind.Translator => L("Traduisez vos textes avec le modèle de votre choix.", "Translate text with the model of your choice."),
            ModelToolKind.Proofreader => L("Relisez, corrigez et reformulez vos textes.", "Review, correct and rephrase your writing."),
            _ => L("Raisonnement, code et applications interactives avec aperçu.", "Reasoning, code and interactive applications with a live preview.")
        }, 13, true));
        Panel.Children.Add(heading);
        var toolbar = new Grid { ColumnSpacing = 10 };
        toolbar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        model.Header = L("Modèle", "Model"); AutomationProperties.SetName(model, L("Modèle pour cet outil", "Model for this tool"));
        toolbar.Children.Add(model);
        reload = Button(L("Actualiser", "Refresh"), async () =>
        {
            try
            {
                var current = model.SelectedItem as ToolModelChoice;
                reload.IsEnabled = false;
                var configured = await loadModels();
                if (!ClosedForTools) PopulateModels(configured, current?.Provider.Id, current?.Model);
            }
            catch (Exception ex) { SetNotice(ex.Message, true); }
            finally { if (!ClosedForTools) reload.IsEnabled = !Busy; }
        });
        reload.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(reload, 1); toolbar.Children.Add(reload);
        Grid.SetRow(toolbar, 1); Panel.Children.Add(toolbar);
        busyControls.AddRange([model, reload]);
        stop = Button(L("Arrêter", "Stop"), () => { cancellation?.Cancel(); return Task.CompletedTask; });
        stop.Visibility = Visibility.Collapsed;
        var footer = new StackPanel { Spacing = 6 };
        footer.Children.Add(progressBar); footer.Children.Add(usage); footer.Children.Add(notice);
        usage.Visibility = kind == ModelToolKind.Benchmark ? Visibility.Visible : Visibility.Collapsed;
        notice.Visibility = Visibility.Collapsed;
        Grid.SetRow(footer, 3); Panel.Children.Add(footer);
        UIElement body = kind switch { ModelToolKind.Translator => BuildTranslator(), ModelToolKind.Proofreader => BuildProofreader(), _ => BuildBenchmark() };
        Grid.SetRow((FrameworkElement)body, 2); Panel.Children.Add(body);
        var actions = new Grid { ColumnSpacing = 12 };
        actions.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        if (secondaryActions != null) actions.Children.Add(secondaryActions);
        run.MinWidth = 160; run.Padding = new(22, 11, 22, 11);
        At(actions, Row(stop, run), 0, 1); At(Panel, actions, 4);
        Content = Panel; FluentDesign.WindowChrome(this);
        AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1160, Height = kind == ModelToolKind.Benchmark ? 940 : 860 });
        PopulateModels(providers, providerId, selectedModel);
        model.SelectionChanged += (_, _) => refreshActions();
        Closed += (_, _) => { ClosedForTools = true; cancellation?.Cancel(); foreach (var editor in editors) editor.CloseEditor(); benchmarkPreview?.ClosePreview(); http.Dispose(); };
    }

    void PopulateModels(List<Provider> providers, int? providerId, string? selectedModel)
    {
        var choices = providers.Where(p => !p.IsComposite).SelectMany(p => ProviderModels.Visible(p).Select(m => new ToolModelChoice(p, m))).ToList();
        model.ItemsSource = choices;
        model.SelectedItem = choices.FirstOrDefault(c => c.Provider.Id == providerId && c.Model == selectedModel)
            ?? choices.FirstOrDefault(c => c.Provider.Id == providerId) ?? choices.FirstOrDefault();
        SetNotice(choices.Count == 0
            ? L("Ajoutez un fournisseur et cochez ses modèles dans Réglages, puis actualisez cette liste.", "Add a provider and select its models in Settings, then refresh this list.")
            : "", choices.Count == 0);
        refreshActions();
    }

    internal Task StartOperation(Func<Provider, CancellationToken, Task> operation)
    {
        if (Busy || model.SelectedItem is not ToolModelChoice choice) return Task.CompletedTask;
        ActiveOperation = Execute();
        return ActiveOperation;
        async Task Execute()
        {
            using var source = new CancellationTokenSource();
            cancellation = source;
            SetBusy(true); usage.Text = "";
            SetNotice(L("Connexion au modèle…", "Connecting to the model…"));
            try
            {
                var selected = CompositeModel.Resolve(new AgentModel { ProviderId = choice.Provider.Id, Model = choice.Model }, [choice.Provider]);
                if (SmokeRequest == null) await prepare(selected, source.Token);
                source.Token.ThrowIfCancellationRequested();
                await operation(selected, source.Token);
            }
            catch (OperationCanceledException) { SetNotice(kind == ModelToolKind.Benchmark
                ? L("Arrêté. Les résultats déjà reçus sont conservés ; la dernière réponse peut être incomplète.", "Stopped. Received results are retained; the last response may be incomplete.")
                : L("Action annulée. Le texte d’origine est conservé.", "Cancelled. The original text is preserved."), true); }
            catch (Exception ex) { SetNotice(ex.Message, true); }
            finally { cancellation = null; if (!ClosedForTools) SetBusy(false); }
        }
    }

    async Task<ModelToolResult> Request(Provider selected, ModelToolRequest request, Action<string>? text, CancellationToken ct,
        Action<GenerationUpdate>? progress = null)
    {
        SetNotice(L("Réponse en cours…", "Generating…"));
        var last = DateTime.MinValue;
        var streaming = true;
        GenerationUpdate? latest = null;
        void Render(GenerationUpdate update) { text?.Invoke(update.Text); progress?.Invoke(update); }
        void Update(GenerationUpdate update)
        {
            if (update.Retry is { } retry)
            {
                DispatcherQueue.TryEnqueue(() => { if (streaming && !ClosedForTools) SetNotice(retry.Describe(UiText.Language)); });
                return;
            }
            latest = update;
            if ((text == null && progress == null) || (DateTime.UtcNow - last).TotalMilliseconds < 60) return;
            last = DateTime.UtcNow;
            DispatcherQueue.TryEnqueue(() => { if (streaming && !ClosedForTools && !ct.IsCancellationRequested) Render(update); });
        }
        ModelToolResult result;
        try
        {
            result = SmokeRequest != null ? await SmokeRequest(selected, request, Update, ct)
                : await client.RunAsync(selected, KeyVault.Decrypt(selected.ProtectedKey), request, Update, ct, kind == ModelToolKind.Benchmark ? null : readRetrySettings?.Invoke());
        }
        finally
        {
            streaming = false;
            // Flush the last throttled update, including partial output on cancellation.
            if (!ClosedForTools && latest != null) Render(latest);
        }
        ct.ThrowIfCancellationRequested();
        if (!ClosedForTools)
        {
            Render(new(result.Text, result.Reasoning, result.InputTokens, result.OutputTokens, result.Seconds));
            if (kind == ModelToolKind.Benchmark) usage.Text = Metrics(result);
        }
        return result;
    }

    void SetBusy(bool value)
    {
        foreach (var control in busyControls) control.IsEnabled = !value;
        run.IsEnabled = !value;
        stop.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        progressBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        if (!value && kind != ModelToolKind.Benchmark && !noticeIsError) SetNotice("");
        refreshActions();
    }
    void SetNotice(string message, bool error = false)
    {
        if (ClosedForTools) return;
        noticeIsError = error;
        notice.Text = kind != ModelToolKind.Benchmark && !error && !Busy ? "" : message;
        notice.Visibility = notice.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        notice.Foreground = error ? FluentDesign.Resource("ToolMessageErrorStrokeBrush") : FluentDesign.Secondary;
    }
    static string Metrics(ModelToolResult result) => L("Entrée", "Input") + $" {result.InputTokens:N0}{(result.InputEstimated ? " ≈" : "")} · " +
        L("Sortie", "Output") + $" {result.OutputTokens:N0}{(result.OutputEstimated ? " ≈" : "")} tokens · {result.TokensPerSecond:F1} tok/s · {result.Seconds:F1} s · " +
        L("latence incluse", "including latency") + (result.Estimated ? L(" · ≈ estimation", " · ≈ estimated") : "");
    static TextBlock Text(string value, double size = 14, bool muted = false) => new()
    { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = muted ? FluentDesign.Secondary : FluentDesign.Primary };
    static StackPanel Row(params UIElement[] controls)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var control in controls) panel.Children.Add(control);
        return panel;
    }
    Button Button(string label, Func<Task> action, bool accent = false)
    {
        var button = new Button { Content = label };
        if (accent) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { SetNotice(ex.Message, true); } };
        return button;
    }
    Button Copy(Func<string> source, string? label = null) => Button(label ?? L("Copier", "Copy"), () =>
    {
        try { var data = new DataPackage(); data.SetText(source()); Clipboard.SetContent(data); SetNotice(L("Copié dans le presse-papiers.", "Copied to clipboard.")); }
        catch (Exception ex) { SetNotice(ex.Message, true); }
        return Task.CompletedTask;
    });
    FormattedTextEditor RichEditor(string placeholder, bool readOnly = false)
    {
        var editor = new FormattedTextEditor(placeholder, readOnly);
        editor.Error += error => SetNotice(error, true); editors.Add(editor); return editor;
    }
    Button CopyFormatted(Func<FormattedTextEditor> editor) => Button(L("Copier", "Copy"), async () =>
    {
        try { await editor().CopyAsync(); }
        catch (Exception ex) { SetNotice(ex.Message, true); }
    });
    static Border Card(UIElement child) => new()
    { Child = child, Padding = new(14), Background = FluentDesign.Card, BorderBrush = FluentDesign.Stroke, BorderThickness = new(1), CornerRadius = new(10) };
    static Grid StretchGrid() { var grid = new Grid { RowSpacing = 10 }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); return grid; }
    static void At(Grid grid, UIElement child, int row, int column = 0) { Grid.SetRow((FrameworkElement)child, row); Grid.SetColumn((FrameworkElement)child, column); grid.Children.Add(child); }
    static readonly string[] Languages = ["Français", "English", "Deutsch", "Español", "Italiano", "Português", "日本語", "한국어", "中文", "العربية", "Русский", "Українська", "Nederlands", "Polski", "Türkçe"];
    static ComboBox LanguagePicker(bool automatic = false, int selected = 0) => new()
    { ItemsSource = automatic ? new[] { L("Détecter la langue", "Detect language") }.Concat(Languages).ToArray() : Languages, SelectedIndex = selected, MinWidth = 170, HorizontalAlignment = HorizontalAlignment.Stretch };
    // Collapse two panels vertically at narrower window sizes. Both editors keep a useful height.
    static ScrollViewer ResponsivePair(FrameworkElement left, FrameworkElement right, double rightWeight = 1)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new(rightWeight, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(left); At(grid, right, 0, 1);
        var viewer = new ScrollViewer { Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        bool? wasNarrow = null;
        viewer.SizeChanged += (_, _) =>
        {
            var narrow = viewer.ActualWidth < 740;
            var changedMode = wasNarrow != narrow; wasNarrow = narrow;
            viewer.VerticalScrollBarVisibility = narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            viewer.VerticalScrollMode = narrow ? ScrollMode.Enabled : ScrollMode.Disabled;
            grid.MinHeight = 0;
            grid.Height = narrow ? double.NaN : Math.Max(0, viewer.ActualHeight - 4);
            grid.ColumnDefinitions[1].Width = narrow ? new(0) : new(rightWeight, GridUnitType.Star);
            grid.RowDefinitions[0].Height = narrow ? GridLength.Auto : new(1, GridUnitType.Star);
            grid.RowDefinitions[1].Height = GridLength.Auto;
            Grid.SetRow(right, narrow ? 1 : 0); Grid.SetColumn(right, narrow ? 0 : 1);
            left.MinHeight = right.MinHeight = narrow ? 280 : 0;
            if (changedMode) viewer.DispatcherQueue.TryEnqueue(() => viewer.ChangeView(null, 0, null, true));
        };
        return viewer;
    }
}
