using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace MonolithHarness.App;

internal sealed partial class AiDetectorWindow : Window
{
    sealed record Choice(string Id, string Name) { public override string ToString() => Name; }
    internal Grid Panel { get; } = new() { Padding = new(24), RowSpacing = 14, Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush") };
    readonly TextBox endpoint = new(), text = new(), url = new();
    readonly ComboBox source = new(), filter = new(), detectionService = new(), detectionModel = new();
    readonly StackPanel slopSettings;
    readonly Button reloadModels;
    Button saveModel = null!;
    List<Provider> providers;
    readonly Func<Task<List<Provider>>> loadModels;
    readonly Func<Provider, CancellationToken, Task> prepare;
    readonly Func<int, string, Task> saveDetection;
    readonly Func<FeatureSettings>? readSettings;
    readonly string consumptionDatabase;
    readonly HttpClient modelHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    internal Func<Provider, ModelToolRequest, CancellationToken, Task<ModelToolResult>>? SmokeRequest;
    Provider? SelectedProvider => providers.FirstOrDefault(p => p.Id.ToString() == Selected(detectionService));
    readonly CheckBox heatmap = new();
    readonly TextBlock status = Text("", 13), destination = Text("", 12, true), count = Text("", 12, true), filename = Text("", 13, true);
    readonly TextBlock score = Text("—", 32), verdict = Text("", 17), summary = Text("", 12, true), caution = Text("", 12, true);
    readonly StackPanel engines = new() { Spacing = 8 }, paragraphs = new() { Spacing = 8 }, site = new() { Spacing = 8 };
    readonly ProgressBar gauge = new() { Minimum = 0, Maximum = 100, Height = 8 }, progress = new() { IsIndeterminate = true, Height = 3, Visibility = Visibility.Collapsed };
    readonly Expander connection, paragraphsSection, siteSection;
    readonly Button analyze, cancel, copy, check, startLocal, stopLocal, choose;
    readonly Func<string, Task> saveEndpoint;
    readonly Action<object, Window> initializePicker;
    readonly Dictionary<string, JsonObject> liveEngines = new(StringComparer.Ordinal);
    JsonArray catalog = [];
    JsonObject? report;
    CancellationTokenSource? request;
    string? document;
    bool closed, analyzing;
    static string L(string fr, string en) => UiText.Resolve(fr, en);
    static TextBlock Text(string value, double size = 14, bool muted = false) => new() { Text = value, FontSize = size,
        Foreground = muted ? FluentDesign.Secondary : FluentDesign.Primary, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    static StackPanel Stack(params UIElement[] children) { var stack = new StackPanel { Spacing = 10 }; foreach (var child in children) stack.Children.Add(child); return stack; }
    static Button Action(string label, Func<Task> click, bool accent = false)
    {
        var button = new Button { Content = label }; if (accent) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        button.Click += async (_, _) => await click(); return button;
    }
    static string S(JsonObject obj, string key, string fallback = "") => SlopTotalClient.String(obj, key, fallback);
    static double N(JsonObject obj, string key) => SlopTotalClient.Number(obj, key);
    static string Selected(ComboBox box) => (box.SelectedItem as Choice)?.Id ?? "";

    internal AiDetectorWindow(string address, ElementTheme theme, Func<string, Task> saveEndpoint, Action<object, Window> initializePicker,
        List<Provider>? providers = null, int providerId = 0, string? selectedModel = null, Func<Task<List<Provider>>>? loadModels = null,
        Func<Provider, CancellationToken, Task>? prepare = null, Func<int, string, Task>? saveDetection = null,
        Func<FeatureSettings>? readSettings = null, string? consumptionDatabase = null)
    {
        this.saveEndpoint = saveEndpoint; this.initializePicker = initializePicker; Panel.RequestedTheme = theme;
        this.providers = providers ?? []; this.loadModels = loadModels ?? (() => Task.FromResult(this.providers));
        this.prepare = prepare ?? ((_, _) => Task.CompletedTask); this.saveDetection = saveDetection ?? ((_, _) => Task.CompletedTask);
        this.readSettings = readSettings; this.consumptionDatabase = consumptionDatabase ?? HarnessDb.DatabasePath;
        detectionService.Header = L("Service de détection", "Detection service"); detectionService.HorizontalAlignment = HorizontalAlignment.Stretch;
        detectionModel.Header = L("Modèle du fournisseur", "Provider model"); detectionModel.HorizontalAlignment = HorizontalAlignment.Stretch;
        reloadModels = Action(L("Actualiser les fournisseurs", "Refresh providers"), async () => await RunAsync(async _ =>
        {
            var current = SelectedProvider?.Id ?? 0; var model = detectionModel.SelectedItem as string;
            this.providers = await this.loadModels(); PopulateDetection(current, model);
        }));
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) Panel.RowDefinitions.Add(new() { Height = height });
        Panel.Children.Add(Stack(Text("AI Generated detector", 27), Text(L("Examinez le texte avec des détecteurs spécialisés ou l’un de vos modèles IA.",
            "Examine text with specialized detectors or one of your AI models."), 14, true)));

        endpoint.Text = string.IsNullOrWhiteSpace(address) ? SlopTotalClient.DefaultEndpoint : address;
        endpoint.MaxLength = 2048;
        endpoint.Header = L("Adresse du service SlopTotal", "SlopTotal service address"); endpoint.HorizontalAlignment = HorizontalAlignment.Stretch;
        endpoint.TextChanged += (_, _) => UpdateDestination();
        check = Action(L("Connecter et enregistrer", "Connect and save"), ConnectAsync);
        startLocal = Action(L("Installer / démarrer en local", "Install / start locally"), StartLocalAsync);
        stopLocal = Action(L("Arrêter le service local", "Stop local service"), StopLocalAsync);
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        links.Children.Add(new HyperlinkButton { Content = "SlopTotal · GitHub", NavigateUri = new("https://github.com/pablocaeg/sloptotal") });
        links.Children.Add(new HyperlinkButton { Content = L("Installer Docker", "Install Docker"), NavigateUri = new("https://docs.docker.com/get-started/get-docker/") });
        var localActions = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        var serviceButtons = new[] { check, startLocal, stopLocal }; foreach (var button in serviceButtons) localActions.Children.Add(button);
        int serviceColumns = 0;
        localActions.SizeChanged += (_, e) =>
        {
            int columns = e.NewSize.Width > 950 * TextZoom.ForWindow(Panel) / 100d ? 3 : 1; if (columns == serviceColumns) return; serviceColumns = columns;
            localActions.ColumnDefinitions.Clear(); localActions.RowDefinitions.Clear();
            for (int i = 0; i < columns; i++) localActions.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (int i = 0; i < (3 + columns - 1) / columns; i++) localActions.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (int i = 0; i < 3; i++) { Grid.SetColumn(serviceButtons[i], i % columns); Grid.SetRow(serviceButtons[i], i / columns); }
        };
        slopSettings = Stack(endpoint, localActions, Text(L("Local : Docker doit être installé et démarré. Le premier lancement télécharge les dépendances et plusieurs Go de modèles. Le service reste lancé après fermeture de cette fenêtre ; utilisez Arrêter pour libérer ses ressources. Ses modèles et rapports sont conservés dans les volumes Docker.",
                "Local: Docker must be installed and running. First start downloads dependencies and several GB of models. The service keeps running after this window closes; use Stop to free its resources. Models and reports are retained in Docker volumes."), 12, true), links);
        connection = new Expander { Header = L("Service de détection · configuration", "Detection service · setup"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = Stack(detectionService, detectionModel, reloadModels, slopSettings), IsExpanded = false };
        // Saving an AI selection is explicit and makes no paid test request.
        saveModel = Action(L("Enregistrer la sélection", "Save selection"), async () => await RunAsync(async _ =>
        {
            if (SelectedProvider is not { } provider || detectionModel.SelectedItem is not string model) throw new ArgumentException(L("Choisissez un modèle.", "Select a model."));
            await this.saveDetection(provider.Id, model); status.Text = L("Sélection enregistrée.", "Selection saved.");
        }));
        var modelActions = Stack(detectionModel, saveModel); ((StackPanel)connection.Content).Children.Remove(detectionModel);
        ((StackPanel)connection.Content).Children.Insert(1, modelActions);
        detectionService.SelectionChanged += (_, _) => { UpdateDetectionMode(); modelActions.Visibility = SelectedProvider == null ? Visibility.Collapsed : Visibility.Visible; };
        detectionModel.SelectionChanged += (_, _) => { UpdateDestination(); if (analyze != null) analyze.IsEnabled = !analyzing && (SelectedProvider == null || detectionModel.SelectedItem != null); };
        Grid.SetRow(connection, 1); Panel.Children.Add(connection);

        source.Header = L("Source à analyser", "Source to analyze"); source.HorizontalAlignment = HorizontalAlignment.Stretch;
        source.ItemsSource = new[] { new Choice("text", L("Texte", "Text")), new Choice("url", L("Texte d’une page web", "Web page text")),
            new Choice("document", L("Document", "Document")), new Choice("site", L("Site créé avec un outil IA", "AI-built website")) }; source.SelectedIndex = 0;
        text.AcceptsReturn = true; text.TextWrapping = TextWrapping.Wrap; text.MinHeight = 230; text.MaxHeight = 400; text.MaxLength = 100_000;
        text.PlaceholderText = L("Collez le texte à examiner…", "Paste text to examine…"); text.TextChanged += (_, _) => UpdateWordCount();
        ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
        url.Header = L("URL de la page", "Page URL"); url.PlaceholderText = "https://…";
        choose = Action(L("Choisir un document…", "Choose document…"), PickDocumentAsync);
        var documents = Stack(choose, filename, Text(L("PDF, DOCX, TXT ou MD · 10 Mo maximum. Les PDF scannés nécessitent un texte déjà extrait par OCR.",
            "PDF, DOCX, TXT or MD · maximum 10 MB. Scanned PDFs require text already extracted with OCR."), 12, true));
        heatmap.Content = L("Afficher les scores par paragraphe", "Show paragraph scores"); heatmap.IsChecked = true;
        var left = Stack(source, url, documents, text, count, heatmap, destination,
            Text(L("Le score est un indice, pas une preuve d’auteur. Les textes de moins de 80 mots sont peu fiables ; privilégiez au moins 200 mots. Ces moteurs évaluent le texte, pas les images ni le code source. La fiabilité varie selon la langue et les réécritures.",
                "The score is an indicator, not proof of authorship. Texts under 80 words are unreliable; aim for at least 200 words. These engines evaluate prose, not images or source code. Reliability varies by language and rewriting."), 12, true));
        source.SelectionChanged += (_, _) =>
        {
            var mode = Selected(source); text.Visibility = mode == "text" ? Visibility.Visible : Visibility.Collapsed;
            url.Visibility = mode is "url" or "site" ? Visibility.Visible : Visibility.Collapsed; documents.Visibility = mode == "document" ? Visibility.Visible : Visibility.Collapsed;
            count.Visibility = mode == "text" ? Visibility.Visible : Visibility.Collapsed; heatmap.Visibility = mode == "site" ? Visibility.Collapsed : Visibility.Visible;
            UpdateDestination();
        };
        url.Visibility = documents.Visibility = Visibility.Collapsed;
        verdict.Text = L("Votre rapport apparaîtra ici", "Your report will appear here");
        var scoreCard = FluentDesign.Surface(Stack(Text(L("Indice de génération IA", "AI generation index"), 12, true), score, gauge, verdict, summary, caution), 18);
        filter.Header = L("Résultats des détecteurs", "Detector results"); filter.HorizontalAlignment = HorizontalAlignment.Stretch;
        filter.ItemsSource = new[] { new Choice("all", L("Tous les moteurs", "All engines")), new Choice("flagged", L("Indices IA détectés", "AI signals detected")),
            new Choice("clean", L("Peu d’indices IA", "Few AI signals")), new Choice("error", L("Moteurs indisponibles", "Unavailable engines")) }; filter.SelectedIndex = 0;
        filter.SelectionChanged += (_, _) => RenderEngines();
        paragraphsSection = new Expander { Header = L("Analyse par paragraphe", "Paragraph analysis"), Content = paragraphs, HorizontalAlignment = HorizontalAlignment.Stretch, IsExpanded = true, Visibility = Visibility.Collapsed };
        siteSection = new Expander { Header = L("Traces du constructeur du site", "Website builder fingerprints"), Content = site, HorizontalAlignment = HorizontalAlignment.Stretch, IsExpanded = true, Visibility = Visibility.Collapsed };
        var right = Stack(scoreCard, siteSection, filter, engines, paragraphsSection);
        var columns = new Grid { ColumnSpacing = 20, RowSpacing = 20 };
        columns.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new() { Width = new(3, GridUnitType.Star) });
        columns.RowDefinitions.Add(new() { Height = GridLength.Auto }); columns.RowDefinitions.Add(new() { Height = GridLength.Auto });
        columns.Children.Add(left); Grid.SetColumn(right, 1); columns.Children.Add(right);
        columns.SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width < 940 * TextZoom.ForWindow(Panel) / 100d;
            Grid.SetColumnSpan(left, narrow ? 2 : 1); Grid.SetColumnSpan(right, narrow ? 2 : 1); Grid.SetColumn(right, narrow ? 0 : 1); Grid.SetRow(right, narrow ? 1 : 0);
        };
        var scroll = new ScrollViewer { Content = columns, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 2); Panel.Children.Add(scroll);
        var notice = Stack(progress, status); Grid.SetRow(notice, 3); Panel.Children.Add(notice);
        analyze = Action(L("Analyser", "Analyze"), AnalyzeAsync, true);
        cancel = Action(L("Arrêter", "Stop"), () => { request?.Cancel(); return Task.CompletedTask; }); cancel.Visibility = Visibility.Collapsed;
        copy = Action(L("Copier le rapport", "Copy report"), CopyAsync); copy.IsEnabled = false;
        var primaryActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        primaryActions.Children.Add(cancel); primaryActions.Children.Add(analyze);
        var actions = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        actions.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        actions.RowDefinitions.Add(new() { Height = GridLength.Auto }); actions.RowDefinitions.Add(new() { Height = GridLength.Auto });
        copy.HorizontalAlignment = HorizontalAlignment.Left; actions.Children.Add(copy); Grid.SetColumn(primaryActions, 1); actions.Children.Add(primaryActions);
        actions.SizeChanged += (_, e) => { bool narrow = e.NewSize.Width < 450 * TextZoom.ForWindow(Panel) / 100d; Grid.SetColumnSpan(copy, narrow ? 2 : 1); Grid.SetRow(primaryActions, narrow ? 1 : 0); };
        Grid.SetRow(actions, 4); Panel.Children.Add(actions);
        PopulateDetection(providerId, selectedModel);
        UpdateWordCount(); UpdateDestination(); RenderEngines();
        Content = Panel; FluentDesign.WindowChrome(this); AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1260, Height = 960 });
        Closed += (_, _) => { closed = true; request?.Cancel(); modelHttp.Dispose(); };
    }

    void PopulateDetection(int providerId, string? selectedModel)
    {
        providers = providers.Where(p => !p.IsComposite).ToList();
        detectionService.ItemsSource = new[] { new Choice("sloptotal", "SlopTotal · " + L("Détecteurs spécialisés", "Specialized detectors")) }
            .Concat(providers.Select(p => new Choice(p.Id.ToString(), p.Name + " · #" + p.Id))).ToArray();
        detectionService.SelectedIndex = Math.Max(0, providers.FindIndex(p => p.Id == providerId) + 1);
        if (SelectedProvider != null && detectionModel.ItemsSource is IEnumerable<string> names && names.Contains(selectedModel)) detectionModel.SelectedItem = selectedModel;
    }
    void UpdateDetectionMode()
    {
        var provider = SelectedProvider;
        slopSettings.Visibility = provider == null ? Visibility.Visible : Visibility.Collapsed;
        detectionModel.ItemsSource = provider == null ? Array.Empty<string>() : ProviderModels.Visible(provider).ToArray();
        detectionModel.SelectedIndex = provider == null ? -1 : 0;
        var previous = Selected(source);
        source.ItemsSource = new[] { new Choice("text", L("Texte", "Text")), new Choice("document", L("Document", "Document")) }
            .Concat(provider == null ? new[] { new Choice("url", L("Texte d’une page web", "Web page text")), new Choice("site", L("Site créé avec un outil IA", "AI-built website")) } : Array.Empty<Choice>()).ToArray();
        source.SelectedItem = ((Choice[])source.ItemsSource).FirstOrDefault(c => c.Id == previous) ?? ((Choice[])source.ItemsSource)[0];
        UpdateDestination();
    }
    void UpdateDestination()
    {
        if (SelectedProvider is { } provider)
        {
            destination.Text = L("Le texte analysé sera envoyé à votre fournisseur : ", "Analyzed text will be sent to your provider: ") + provider.Name + " · " + detectionModel.SelectedItem + ". " +
                L("Analyse du texte collé et des documents. Les pages web et traces de sites sont disponibles avec SlopTotal. Le score du modèle est une appréciation non étalonnée.", "Pasted text and document analysis. Web pages and site fingerprints are available with SlopTotal. Model scores are uncalibrated assessments.");
            return;
        }
        try
        {
            var server = SlopTotalClient.NormalizeEndpoint(endpoint.Text); var local = server.IsLoopback;
            destination.Text = local ? L("Analyse sur votre service local. Aucun envoi au fournisseur de chat.", "Analyzed by your local service. Nothing sent to the chat provider.")
                : L("Le contenu sera envoyé au service : ", "Content will be sent to: ") + server.GetLeftPart(UriPartial.Authority);
            destination.Text += " " + L("SlopTotal conserve les rapports selon la configuration du serveur (30 jours par défaut).", "SlopTotal retains reports according to server settings (30 days by default).");
        }
        catch (ArgumentException) { destination.Text = L("Renseignez une adresse de service valide.", "Enter a valid service address."); }
    }
    void UpdateWordCount()
    {
        int words = Regex.Matches(text.Text, @"\S+", RegexOptions.None, TimeSpan.FromSeconds(1)).Count;
        count.Text = text.Text.Length.ToString("N0") + " " + L("caractères", "characters") + " · " + words.ToString("N0") + " " + L("mots", "words");
    }
    void SetBusy(bool value)
    {
        analyzing = value; detectionService.IsEnabled = detectionModel.IsEnabled = reloadModels.IsEnabled = saveModel.IsEnabled = !value;
        endpoint.IsEnabled = source.IsEnabled = text.IsEnabled = url.IsEnabled = heatmap.IsEnabled = choose.IsEnabled = check.IsEnabled = startLocal.IsEnabled = stopLocal.IsEnabled = analyze.IsEnabled = !value;
        analyze.IsEnabled = !value && (SelectedProvider == null || detectionModel.SelectedItem != null);
        cancel.Visibility = progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        copy.IsEnabled = !value && report != null;
    }
    async Task RunAsync(Func<CancellationToken, Task> action, TimeSpan? timeout = null)
    {
        if (request != null || closed) return;
        using var operation = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(15)); request = operation; SetBusy(true);
        status.Foreground = FluentDesign.Secondary;
        try { await action(operation.Token); }
        catch (OperationCanceledException) { if (!closed) status.Text = L("Analyse arrêtée ou délai dépassé.", "Analysis stopped or timed out."); }
        catch (Exception ex)
        {
            if (!closed)
            {
                status.Foreground = FluentDesign.Resource("ToolMessageErrorStrokeBrush");
                var message = ex.Message.Length > 900 ? ex.Message[..900] + "…" : ex.Message;
                status.Text = ex is HttpRequestException ? L("La connexion a refusé la demande ou n’est pas joignable. Vérifiez sa configuration. ", "The connection rejected the request or is unreachable. Check its configuration. ") + message : message;
                if (ex is HttpRequestException) connection.IsExpanded = true;
            }
        }
        finally { request = null; if (!closed) SetBusy(false); }
    }
    async Task ConnectAsync() => await RunAsync(async ct =>
    {
        status.Text = L("Connexion au service…", "Connecting to service…");
        using var client = new SlopTotalClient(endpoint.Text); var health = await client.HealthAsync(ct); catalog = await client.EnginesAsync(ct);
        await saveEndpoint(client.Endpoint.ToString().TrimEnd('/')); await saveDetection(0, "");
        status.Text = L("Service joignable", "Service reachable") + " · " + catalog.Count + " " + L("moteurs annoncés", "listed engines")
            + (S(health, "status") == "healthy" ? "" : " · " + L("Service dégradé", "Degraded service"));
        RenderEngines();
    });
    async Task StartLocalAsync() => await RunAsync(async ct =>
    {
        connection.IsExpanded = true; endpoint.Text = SlopTotalClient.DefaultEndpoint;
        status.Text = L("Installation / démarrage du service local…", "Installing / starting local service…");
        bool installing = true;
        var updates = new Progress<string>(line =>
        {
            if (closed || !installing || request?.Token != ct || ct.IsCancellationRequested) return;
            status.Text = line.Contains("Downloading", StringComparison.OrdinalIgnoreCase) ? L("Téléchargement des dépendances…", "Downloading dependencies…")
                : line.Contains("install", StringComparison.OrdinalIgnoreCase) ? L("Installation des dépendances…", "Installing dependencies…")
                : L("Préparation du service local… Le premier démarrage peut prendre plusieurs minutes.", "Preparing local service… First startup may take several minutes.");
        });
        await SlopTotalLocalService.StartAsync(updates, ct);
        installing = false;
        using var client = new SlopTotalClient(endpoint.Text);
        status.Text = L("Attente du démarrage… Les modèles peuvent encore être en téléchargement.", "Waiting for startup… Models may still be downloading.");
        var until = DateTime.UtcNow.AddMinutes(3); bool ready = false;
        while (DateTime.UtcNow < until)
        {
            ct.ThrowIfCancellationRequested();
            try { await client.HealthAsync(ct); ready = true; break; }
            catch (Exception ex) when (ex is HttpRequestException or IOException || ex is OperationCanceledException && !ct.IsCancellationRequested) { await Task.Delay(1500, ct); }
        }
        if (!ready) throw new IOException(L("Le conteneur a démarré mais le service ne répond pas encore. Consultez ses logs Docker puis utilisez Connecter.", "The container started but the service is not responding yet. Check its Docker logs, then use Connect."));
        catalog = await client.EnginesAsync(ct); await saveEndpoint(SlopTotalClient.DefaultEndpoint); RenderEngines();
        status.Text = L("Service local prêt. Le premier examen peut attendre le chargement des modèles.", "Local service ready. The first scan may wait for models to load.");
    }, TimeSpan.FromHours(1));
    async Task StopLocalAsync() => await RunAsync(async ct => { await SlopTotalLocalService.StopAsync(ct); status.Text = L("Service local arrêté. Les modèles téléchargés sont conservés.", "Local service stopped. Downloaded models are retained."); }, TimeSpan.FromSeconds(45));
    async Task PickDocumentAsync()
    {
        if (analyzing) return;
        try
        {
            var picker = new FileOpenPicker(); foreach (var extension in new[] { ".pdf", ".docx", ".txt", ".md" }) picker.FileTypeFilter.Add(extension);
            initializePicker(picker, this); var file = await picker.PickSingleFileAsync(); if (file == null || closed) return;
            if (new FileInfo(file.Path).Length > SlopTotalClient.MaxDocumentBytes) throw new IOException(L("Document : 10 Mo maximum.", "Document: maximum 10 MB."));
            document = file.Path; filename.Text = Path.GetFileName(document); status.Text = "";
        }
        catch (Exception ex) { if (!closed) status.Text = ex.Message; }
    }
    async Task AnalyzeAsync() => await RunAsync(async ct =>
    {
        if (SelectedProvider != null) { await AnalyzeProviderAsync(ct); return; }
        using var client = new SlopTotalClient(endpoint.Text); var mode = Selected(source); var input = mode == "text" ? text.Text.Trim() : ""; var address = mode is "url" or "site" ? url.Text.Trim() : "";
        if (mode == "text" && input.Length < 50) throw new ArgumentException(L("Collez au moins 50 caractères pour lancer l’analyse.", "Paste at least 50 characters to start analysis."));
        if (mode == "document" && document == null) throw new ArgumentException(L("Choisissez un document à analyser.", "Choose a document to analyze."));
        if (mode is "url" or "site" && address.Length == 0) throw new ArgumentException(L("Renseignez l’URL de la page.", "Enter the page URL."));
        status.Text = L("Connexion aux détecteurs…", "Connecting to detectors…");
        await client.HealthAsync(ct); catalog = await client.EnginesAsync(ct); await saveEndpoint(client.Endpoint.ToString().TrimEnd('/')); await saveDetection(0, "");
        report = null; liveEngines.Clear(); score.Text = "—"; gauge.Value = 0; summary.Text = caution.Text = "";
        verdict.Text = L("Analyse en cours…", "Analyzing…"); site.Children.Clear(); paragraphs.Children.Clear(); siteSection.Visibility = paragraphsSection.Visibility = Visibility.Collapsed; RenderEngines();
        var timer = Stopwatch.StartNew(); bool fullReceived = false;
        if (mode == "document") { status.Text = L("Extraction du document…", "Extracting document…"); input = await client.ExtractAsync(document!, ct); }
        if (mode == "site")
        {
            status.Text = L("Recherche de traces du constructeur et analyse du texte…", "Looking for builder fingerprints and analyzing text…");
            report = await client.SiteAsync(address, ct); RenderSite(report);
        }
        else
        {
            var updates = new Progress<SlopTotalClient.Update>(update =>
            {
                if (closed || fullReceived || request?.Token != ct || ct.IsCancellationRequested) return;
                if (update.QueuePosition is { } position) { status.Text = L("En file d’attente · position ", "Queued · position ") + position; return; }
                if (update.Engine is not { } engine) return;
                liveEngines[S(engine, "engine_name")] = engine;
                score.Text = N(engine, "overall_score").ToString("0.0") + " / 100"; gauge.Value = N(engine, "overall_score");
                verdict.Text = L("Score provisoire", "Provisional score"); summary.Text = N(engine, "engines_done") + " / " + N(engine, "engines_total") + " " + L("moteurs terminés", "engines completed");
                status.Text = L("Analyse en cours…", "Analyzing…") + " · " + timer.Elapsed.TotalSeconds.ToString("0") + " s"; RenderEngines();
            });
            report = await client.AnalyzeAsync(input, address, updates, ct); fullReceived = true; RenderFull(report);
            if (heatmap.IsChecked == true)
            {
                status.Text = L("Analyse des paragraphes…", "Analyzing paragraphs…");
                try
                {
                    var detail = await client.ParagraphsAsync(input, address, ct); report["paragraph_analysis"] = detail; RenderParagraphs(detail);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    paragraphsSection.Visibility = Visibility.Visible; paragraphs.Children.Add(Text(L("Le rapport global est disponible. Analyse des paragraphes indisponible : ", "The overall report is available. Paragraph analysis unavailable: ") + ex.Message, 12, true));
                    report["paragraph_error"] = ex.Message;
                }
            }
        }
        report["service_url"] = client.Endpoint.ToString(); report["client_elapsed_seconds"] = timer.Elapsed.TotalSeconds;
        status.Text = L("Analyse terminée en ", "Analysis completed in ") + timer.Elapsed.TotalSeconds.ToString("0.0") + " s";
    });

    async Task AnalyzeProviderAsync(CancellationToken ct)
    {
        var configured = SelectedProvider ?? throw new ArgumentException(L("Choisissez un fournisseur.", "Select a provider."));
        var selectedModel = detectionModel.SelectedItem as string ?? throw new ArgumentException(L("Choisissez un modèle.", "Select a model."));
        var selected = CompositeModel.Resolve(new AgentModel { ProviderId = configured.Id, Model = selectedModel }, [configured]);
        var mode = Selected(source);
        if (mode is not ("text" or "document")) throw new ArgumentException(L("Choisissez du texte ou un document.", "Select text or a document."));
        if (mode == "document" && document == null) throw new ArgumentException(L("Choisissez un document.", "Select a document."));
        status.Text = L("Préparation du texte…", "Preparing text…");
        var input = mode == "document" ? await AiTextDetection.ReadDocumentAsync(document!, ct) : text.Text.Trim();
        var byParagraph = heatmap.IsChecked == true;
        var prompt = AiTextDetection.Request(input, byParagraph, UiText.Language);
        await saveDetection(configured.Id, selectedModel);
        report = null; catalog = []; liveEngines.Clear(); score.Text = "—"; gauge.Value = 0; summary.Text = caution.Text = "";
        verdict.Text = L("Analyse du modèle en cours…", "Model analysis in progress…");
        site.Children.Clear(); paragraphs.Children.Clear(); siteSection.Visibility = paragraphsSection.Visibility = Visibility.Collapsed; RenderEngines();
        var watch = Stopwatch.StartNew();
        using var consumption = SmokeRequest == null ? TokenConsumption.Begin(consumptionDatabase, "ai_detector") : null;
        if (SmokeRequest == null) await prepare(selected, ct);
        var last = DateTime.MinValue;
        void Update(GenerationUpdate update)
        {
            if ((DateTime.UtcNow - last).TotalMilliseconds < 200) return;
            last = DateTime.UtcNow;
            DispatcherQueue.TryEnqueue(() => { if (!closed && request?.Token == ct && !ct.IsCancellationRequested) status.Text = update.Retry is { } retry ? retry.Describe(UiText.Language) : L("Analyse du modèle…", "Model analysis…"); });
        }
        var result = SmokeRequest != null ? await SmokeRequest(selected, prompt, ct)
            : await new ModelToolClient(modelHttp).RunAsync(selected, KeyVault.Decrypt(selected.ProtectedKey), prompt, Update, ct, readSettings?.Invoke());
        var parsed = await Task.Run(() => AiTextDetection.Report(input, result.Text, byParagraph, configured.Name + " · " + selectedModel), ct);
        ct.ThrowIfCancellationRequested(); if (closed) return;
        parsed["provider_id"] = configured.Id; parsed["provider_name"] = configured.Name; parsed["model"] = selectedModel;
        parsed["input_tokens"] = result.InputTokens; parsed["output_tokens"] = result.OutputTokens; parsed["tokens_estimated"] = result.Estimated;
        parsed["client_elapsed_seconds"] = watch.Elapsed.TotalSeconds;
        report = parsed; RenderFull(report);
        if (byParagraph && report["paragraph_analysis"] is JsonObject detail) RenderParagraphs(detail);
        status.Text = L("Analyse terminée en ", "Analysis completed in ") + watch.Elapsed.TotalSeconds.ToString("0.0") + " s";
    }

    void RenderFull(JsonObject result)
    {
        liveEngines.Clear(); if (result["engine_results"] is JsonArray items) foreach (var engine in items.OfType<JsonObject>()) liveEngines[S(engine, "engine_name")] = engine;
        var failed = liveEngines.Values.Count(SlopTotalClient.EngineFailed); var expected = (int)N(result, "engines_total"); var incomplete = liveEngines.Count < expected || failed > 0;
        bool anyValid = liveEngines.Values.Any(x => !SlopTotalClient.EngineFailed(x));
        score.Text = anyValid ? N(result, "overall_score").ToString("0.0") + " / 100" : "—"; gauge.Value = anyValid ? N(result, "overall_score") : 0;
        verdict.Text = !anyValid ? L("Résultat indéterminé", "Indeterminate result") : incomplete ? L("Rapport incomplet · interprétation limitée", "Incomplete report · limited interpretation") : Verdict(S(result, "overall_verdict"));
        int flagged = liveEngines.Values.Count(x => !SlopTotalClient.EngineFailed(x) && S(x, "verdict") is "suspicious" or "slop");
        summary.Text = N(result, "word_count").ToString("N0") + " " + L("mots", "words") + " · " + liveEngines.Count + " / " + expected + " " + L("moteurs terminés", "engines completed")
            + " · " + flagged + " " + L("signalent des indices IA", "flag AI signals");
        caution.Text = incomplete ? L("Certains détecteurs sont absents ou en erreur. Le score du service peut être biaisé ; ne concluez pas sur son verdict global.", "Some detectors are missing or failed. The service score may be biased; do not rely on its overall verdict.")
            : N(result, "word_count") < 200 ? L("Texte court : résultat à interpréter avec prudence.", "Short text: interpret the result with care.") : L("Score calibré fourni par SlopTotal. Comparez les moteurs avant de tirer une conclusion.", "Calibrated score supplied by SlopTotal. Compare engines before drawing a conclusion.");
        if (S(result, "detection_method") == "language_model")
            caution.Text = L("Appréciation d’un modèle, non étalonnée : ce score n’est ni une probabilité mesurée ni une preuve d’auteur. Il peut varier avec le modèle et sa réponse.", "Uncalibrated model assessment: this score is neither a measured probability nor proof of authorship. It can vary by model and response.");
        RenderEngines();
    }
    static string Verdict(string value) => value switch
    {
        "Clean — likely human-written" or "clean" => L("Peu d’indices de génération IA", "Few signs of AI generation"),
        "Low risk" => L("Indices faibles", "Weak signals"), "Suspicious" or "suspicious" or "mixed" => L("Indices mixtes ou suspects", "Mixed or suspicious signals"),
        "Likely AI-generated" or "ai" => L("Génération IA probable selon les détecteurs", "Likely AI-generated according to detectors"),
        "Slop detected" or "slop" => L("Indices élevés de génération IA", "Strong signs of AI generation"), _ => value
    };
    static Brush ScoreBrush(double value) => value > 65 ? FluentDesign.Resource("ToolMessageErrorStrokeBrush") : value > 35 ? FluentDesign.Adapt(228, 162, 39) : FluentDesign.Resource("AccentFillColorDefaultBrush");
    void RenderEngines()
    {
        engines.Children.Clear(); var selection = Selected(filter);
        var results = liveEngines.Values.Where(x => selection switch { "error" => SlopTotalClient.EngineFailed(x), "flagged" => !SlopTotalClient.EngineFailed(x) && S(x, "verdict") is "suspicious" or "slop", "clean" => !SlopTotalClient.EngineFailed(x) && S(x, "verdict") == "clean", _ => true })
            .OrderBy(x => SlopTotalClient.EngineFailed(x)).ThenByDescending(x => N(x, "score")).ToArray();
        foreach (var engine in results)
        {
            bool failed = SlopTotalClient.EngineFailed(engine); var name = S(engine, "engine_name");
            var metadata = catalog.OfType<JsonObject>().FirstOrDefault(x => S(x, "name") == name || S(x, "key") == name);
            var title = name + " · " + (failed ? L("Indisponible", "Unavailable") : (N(engine, "score") * 100).ToString("0.0") + " / 100");
            var details = Stack(Text(failed ? L("Ce moteur n’a pas fourni de mesure exploitable.", "This engine did not provide a usable measurement.") : Verdict(S(engine, "verdict")), 13),
                Text(S(engine, "description", metadata == null ? "" : S(metadata, "description")), 12, true), Text(S(engine, "details"), 12, true));
            var expander = new Expander { Header = title, Content = details, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (!failed) details.Children.Insert(0, new ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Clamp(N(engine, "score") * 100, 0, 100), Height = 5, Foreground = ScoreBrush(N(engine, "score") * 100) });
            if (metadata != null && Uri.TryCreate(S(metadata, "url"), UriKind.Absolute, out var link) && link.Scheme is "https" or "http")
                details.Children.Add(new HyperlinkButton { Content = L("Documentation du moteur", "Engine documentation"), NavigateUri = link });
            engines.Children.Add(expander);
        }
        if (results.Length == 0) engines.Children.Add(Text(liveEngines.Count > 0 ? L("Aucun moteur ne correspond à ce filtre.", "No engines match this filter.") : L("Lancez une analyse pour comparer les détecteurs.", "Start an analysis to compare detectors."), 13, true));
    }
    void RenderParagraphs(JsonObject detail)
    {
        paragraphs.Children.Clear(); paragraphsSection.Visibility = Visibility.Visible;
        paragraphs.Children.Add(Text(S(report ?? new(), "detection_method") == "language_model" ? L("Appréciation du même modèle par paragraphe ; scores non étalonnés.", "Per-paragraph assessment by the same model; uncalibrated scores.") : L("Ces scores rapides par passage sont distincts du rapport complet. Le service peut regrouper les paragraphes courts et tronquer l’extrait affiché.", "These quick passage scores are separate from the full report. The service may merge short paragraphs and truncate displayed excerpts."), 12, true));
        if (detail["paragraphs"] is not JsonArray items || items.Count == 0) { paragraphs.Children.Add(Text(L("Aucun paragraphe exploitable.", "No usable paragraphs."))); return; }
        foreach (var item in items.OfType<JsonObject>())
        {
            var value = N(item, "score"); var caption = "#" + (N(item, "index") + 1) + " · " + (item["score"] == null ? "—" : value.ToString("0.0")) + " / 100 · " + Verdict(S(item, "verdict"));
            var card = FluentDesign.Surface(Stack(Text(caption, 13), new ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Clamp(value, 0, 100), Height = 5, Foreground = ScoreBrush(value), Visibility = item["score"] == null ? Visibility.Collapsed : Visibility.Visible }, Text(S(item, "text"), 13), Text(S(item, "explanation"), 12, true)), 12);
            card.BorderBrush = ScoreBrush(value); card.BorderThickness = new(3, 0, 0, 0); paragraphs.Children.Add(card);
        }
    }
    void RenderSite(JsonObject result)
    {
        siteSection.Visibility = Visibility.Visible;
        var fingerprints = result["site"] as JsonObject; var builders = fingerprints?["builders"] as JsonArray;
        if (builders is { Count: > 0 }) foreach (var builder in builders.OfType<JsonObject>())
        {
            var confidence = S(builder, "confidence") == "confirmed" ? L("Traces confirmées", "Confirmed fingerprints") : L("Traces possibles", "Possible fingerprints");
            var evidence = builder["evidence"] as JsonArray;
            site.Children.Add(FluentDesign.Surface(Stack(Text(S(builder, "name") + " · " + confidence, 16), Text(evidence == null ? "" : string.Join("\n", evidence.Select(x => "• " + x?.ToString())), 13, true)), 12));
        }
        else site.Children.Add(Text(L("Aucune trace de constructeur IA identifiée. Cela ne prouve pas que le site a été écrit sans IA.", "No AI builder fingerprints identified. This does not prove the site was written without AI."), 13));
        if (fingerprints != null && S(fingerprints, "generator").Length > 0) site.Children.Add(Text("Generator : " + S(fingerprints, "generator"), 12, true));
        if (result["text"] is JsonObject prose)
        {
            score.Text = N(prose, "score").ToString("0.0") + " / 100"; gauge.Value = N(prose, "score"); verdict.Text = Verdict(S(prose, "verdict"));
            summary.Text = L("Analyse rapide du texte du site", "Quick analysis of website text") + " · " + N(prose, "word_count").ToString("N0") + " " + L("mots", "words");
            if (prose["engines"] is JsonArray list) foreach (var engine in list.OfType<JsonObject>())
            {
                // Quick API uses engine keys and a 0–100 scale; full reports use names and 0–1.
                var entry = (JsonObject)engine.DeepClone(); var key = S(entry, "engine");
                var metadata = catalog.OfType<JsonObject>().FirstOrDefault(x => S(x, "key") == key);
                entry["engine_name"] = metadata == null ? key : S(metadata, "name"); entry["score"] = N(engine, "score") / 100d;
                liveEngines[S(entry, "engine_name")] = entry;
            }
            RenderEngines();
            if (liveEngines.Values.Any(SlopTotalClient.EngineFailed))
            {
                verdict.Text = L("Rapport incomplet · interprétation limitée", "Incomplete report · limited interpretation");
                if (liveEngines.Values.All(SlopTotalClient.EngineFailed)) { score.Text = "—"; gauge.Value = 0; }
            }
        }
        else { score.Text = "—"; verdict.Text = L("Texte insuffisant pour un score IA", "Insufficient prose for an AI score"); }
        caution.Text = L("Les traces du constructeur et le score du texte sont deux mesures indépendantes.", "Builder fingerprints and the text score are two independent measurements.");
    }
    async Task CopyAsync()
    {
        if (report == null) return;
        try
        {
            var data = new DataPackage(); data.SetText(report.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true })); Clipboard.SetContent(data);
            status.Text = L("Rapport JSON copié.", "JSON report copied.");
        }
        catch (Exception ex) { status.Text = ex.Message; }
        await Task.CompletedTask;
    }
}
