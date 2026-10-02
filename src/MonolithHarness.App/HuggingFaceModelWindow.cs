using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

internal sealed class HuggingFaceModelWindow : Window
{
    static string WorkflowText(string fr, string en) => UiText.Language == "en" ? en : fr;
    readonly LocalProviderSettings config;
    readonly string token;
    readonly int context;
    readonly HuggingFaceModels catalog;
    readonly Func<CancellationToken, Task<MachineCapabilities>> readMachine;
    readonly CancellationTokenSource lifetime = new();
    readonly TaskCompletionSource<LocalModel?> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TextBox query = new() { Name = "ModelSearch", PlaceholderText = WorkflowText("Rechercher un modèle, une famille ou un auteur…", "Search models, families or authors…"), HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox type = new() { Name = "ModelType", ItemsSource = new[] { WorkflowText("Conversation · GGUF", "Chat · GGUF"), WorkflowText("Images · checkpoints", "Images · checkpoints") }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Button search = new() { Name = "SearchModels", Content = WorkflowText("Rechercher", "Search") };
    readonly ListView models = new() { Name = "HubModels", SelectionMode = ListViewSelectionMode.Single, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly ListView files = new() { Name = "HubFiles", SelectionMode = ListViewSelectionMode.Single, HorizontalContentAlignment = HorizontalAlignment.Stretch, MaxHeight = 176 };
    readonly TextBlock count = Text("", 12, true);
    readonly TextBlock listHint = Text(WorkflowText("Chargement du catalogue…", "Loading catalogue…"), 13, true);
    readonly TextBlock title = Text("", 23);
    readonly TextBlock author = Text("", 12, true);
    readonly TextBlock description = Text("", 13, true);
    readonly TextBlock architecture = Text("", 12, true);
    readonly TextBlock downloads = Text("", 14);
    readonly TextBlock likes = Text("", 14);
    readonly TextBlock modified = Text("", 14);
    readonly TextBlock fileCount = Text("", 12, true);
    readonly TextBlock compatibility = Text("", 13);
    readonly TextBlock compatibilityReason = Text("", 12, true);
    readonly TextBlock machineText = Text(WorkflowText("Lecture des caractéristiques de la machine…", "Reading machine specifications…"), 12, true);
    readonly TextBlock status = Text("", 12, true);
    readonly ProgressBar progress = new() { Visibility = Visibility.Collapsed, Minimum = 0, Maximum = 100 };
    readonly HyperlinkButton repositoryLink = new() { Name = "ModelCardLink", Content = WorkflowText("Fiche du modèle", "Model card"), Padding = new(10, 4, 10, 4) };
    readonly Button download = new() { Name = "DownloadModel", Content = WorkflowText("Télécharger et importer", "Download and import"), IsEnabled = false, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
    readonly Button close = new() { Name = "CloseModelBrowser", Content = WorkflowText("Fermer", "Close") };
    readonly StackPanel readme = new() { Name = "ModelReadme", Spacing = 8 };
    readonly StackPanel details = new() { Spacing = 12, Visibility = Visibility.Collapsed };
    readonly StackPanel emptyDetails = new() { Spacing = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 340, Margin = new(24) };
    CancellationTokenSource? lookup, transfer;
    MachineCapabilities? machine;
    HubModel? selectedModel;
    Task? activeDownload;
    LocalModel? result;
    bool closed, started, downloading;
    int revision;
    internal Grid Panel { get; }
    internal Task<LocalModel?> Completion => completed.Task;

    internal HuggingFaceModelWindow(LocalProviderSettings config, string token, int context, ElementTheme theme,
        HuggingFaceModels? catalog = null, Func<CancellationToken, Task<MachineCapabilities>>? readMachine = null)
    {
        this.config = config; this.token = token; this.context = context;
        this.catalog = catalog ?? new(); this.readMachine = readMachine ?? MachineCapabilities.ReadAsync;
        Title = WorkflowText("Modèles locaux · Hugging Face", "Local models · Hugging Face");
        Panel = new Grid { Name = "LocalModelsBrowser", RequestedTheme = theme, Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush") };
        Panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        Panel.RowDefinitions.Add(new() { Height = GridLength.Auto });

        var header = new StackPanel { Spacing = 10, Margin = new(24, 16, 24, 14) };
        var heading = Text(Title, 22); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        header.Children.Add(heading);
        var toolbar = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        toolbar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new() { Width = new(250) });
        toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Add(toolbar, query); Add(toolbar, type, 0, 1); Add(toolbar, search, 0, 2);
        AutomationProperties.SetName(query, WorkflowText("Rechercher sur Hugging Face", "Search Hugging Face"));
        AutomationProperties.SetName(type, WorkflowText("Type de modèle", "Model type"));
        header.Children.Add(toolbar); Add(Panel, header);

        var body = new Grid { Name = "ModelBrowserColumns", ColumnSpacing = 16, RowSpacing = 16, Margin = new(24, 0, 24, 0) };
        body.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new() { Width = new(3, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = new(0) });
        var listPanel = new Grid { RowSpacing = 10 };
        listPanel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        listPanel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var listHeader = new StackPanel { Spacing = 4 };
        var listTitle = Text(WorkflowText("Modèles", "Models"), 15); listTitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        listHeader.Children.Add(listTitle); listHeader.Children.Add(count); Add(listPanel, listHeader);
        var results = new Grid(); results.Children.Add(models);
        listHint.HorizontalAlignment = HorizontalAlignment.Center; listHint.VerticalAlignment = VerticalAlignment.Center; listHint.Margin = new(16); results.Children.Add(listHint);
        Add(listPanel, results, 1);
        var listSurface = FluentDesign.Surface(listPanel, 12); Add(body, listSurface);

        var detailHeader = new Grid { ColumnSpacing = 12 };
        detailHeader.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        detailHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var identity = new StackPanel { Spacing = 4 };
        title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        identity.Children.Add(title); identity.Children.Add(author);
        repositoryLink.VerticalAlignment = VerticalAlignment.Top;
        Add(detailHeader, identity); Add(detailHeader, repositoryLink, 0, 1);
        details.Children.Add(detailHeader); details.Children.Add(description); details.Children.Add(architecture);
        var stats = new Grid { ColumnSpacing = 12 };
        for (var i = 0; i < 3; i++) stats.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Add(stats, Metric(WorkflowText("Téléchargements", "Downloads"), downloads));
        Add(stats, Metric(WorkflowText("J’aime", "Likes"), likes), 0, 1);
        Add(stats, Metric(WorkflowText("Mis à jour", "Updated"), modified), 0, 2);
        details.Children.Add(stats);
        var filesHeading = new StackPanel { Spacing = 4 };
        var filesTitle = Text(WorkflowText("Fichiers / quantifications", "Files / quantizations"), 15); filesTitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        filesHeading.Children.Add(filesTitle); filesHeading.Children.Add(fileCount);
        details.Children.Add(filesHeading); details.Children.Add(files);
        var support = new StackPanel { Spacing = 6 }; support.Children.Add(compatibility); support.Children.Add(compatibilityReason);
        details.Children.Add(FluentDesign.Surface(support, 12));
        var readmeTitle = Text(WorkflowText("Fiche du dépôt", "Repository model card"), 15); readmeTitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        details.Children.Add(readmeTitle); details.Children.Add(readme);
        details.Children.Add(new Expander { Header = WorkflowText("Votre machine", "Your machine"), Content = machineText, HorizontalAlignment = HorizontalAlignment.Stretch });
        details.Children.Add(new Expander { Header = WorkflowText("Formats pris en charge et accès", "Supported formats and access"), HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Text(WorkflowText("Les estimations ne garantissent pas le débit. GGUF autonome pour la conversation ; checkpoints complets SD 1.x, SD 2.x et SDXL pour les images. Les composants et les autres familles restent visibles avec leur motif de non-support. Un dépôt protégé nécessite un jeton de lecture et l’acceptation de sa licence sur Hugging Face.",
                "Estimates do not guarantee throughput. Standalone GGUF for chat; full SD 1.x, SD 2.x and SDXL checkpoints for images. Other components and families remain visible with reasons. Gated repositories require a read token and accepting their licence on Hugging Face."), 12, true) });
        emptyDetails.Children.Add(FluentDesign.Icon("\uE8F1", 36));
        var emptyTitle = Text(WorkflowText("Trouvez votre prochain modèle local", "Find your next local model"), 20); emptyTitle.TextAlignment = TextAlignment.Center;
        emptyDetails.Children.Add(emptyTitle);
        var emptyHint = Text(WorkflowText("Sélectionnez un modèle à gauche pour découvrir ses fichiers, sa fiche et sa compatibilité avec votre machine.", "Select a model on the left to explore its files, model card and compatibility with your machine."), 13, true); emptyHint.TextAlignment = TextAlignment.Center;
        emptyDetails.Children.Add(emptyHint);
        var detailGrid = new Grid(); detailGrid.Children.Add(new ScrollViewer { Content = details, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); detailGrid.Children.Add(emptyDetails);
        var detailSurface = FluentDesign.Surface(detailGrid, 18); Add(body, detailSurface, 0, 1); Add(Panel, body, 1);

        var footer = new Grid { Margin = new(24, 14, 24, 18), RowSpacing = 10, ColumnSpacing = 16 };
        footer.RowDefinitions.Add(new() { Height = GridLength.Auto }); footer.RowDefinitions.Add(new() { Height = GridLength.Auto }); footer.RowDefinitions.Add(new() { Height = GridLength.Auto });
        footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var transferStatus = new StackPanel { Spacing = 6 }; transferStatus.Children.Add(progress); transferStatus.Children.Add(status); Add(footer, transferStatus); Grid.SetColumnSpan(transferStatus, 2);
        var destination = Text(WorkflowText("Dossier : ", "Directory: ") + config.ModelDirectory, 12, true); destination.MaxLines = 2; destination.TextTrimming = TextTrimming.CharacterEllipsis; destination.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(destination, config.ModelDirectory); Add(footer, destination, 1);
        var actions = new StackPanel { Name = "ModelBrowserActions", Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(close); actions.Children.Add(download); Add(footer, actions, 1, 1); Add(Panel, footer, 2);
        var wasNarrow = false;
        Panel.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 820;
            Grid.SetColumnSpan(query, narrow ? 3 : 1);
            Grid.SetRow(type, narrow ? 1 : 0); Grid.SetColumn(type, narrow ? 0 : 1); Grid.SetColumnSpan(type, narrow ? 2 : 1);
            Grid.SetRow(search, narrow ? 1 : 0);
            body.ColumnDefinitions[0].Width = new(narrow ? 1 : 2, GridUnitType.Star);
            body.ColumnDefinitions[1].Width = narrow ? new(0) : new(3, GridUnitType.Star);
            body.RowDefinitions[0].Height = new(1, GridUnitType.Star);
            body.RowDefinitions[1].Height = narrow ? new(2, GridUnitType.Star) : new(0);
            Grid.SetColumn(detailSurface, narrow ? 0 : 1); Grid.SetRow(detailSurface, narrow ? 1 : 0);
            Grid.SetColumnSpan(destination, narrow ? 2 : 1); Grid.SetRow(actions, narrow ? 2 : 1);
            if (narrow != wasNarrow)
                DispatcherQueue.TryEnqueue(() => { if (!closed && models.SelectedItem != null) models.ScrollIntoView(models.SelectedItem); });
            wasNarrow = narrow;
        };
        search.Click += async (_, _) => await SearchAsync();
        query.KeyDown += async (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter && !downloading) { e.Handled = true; await SearchAsync(); } };
        type.SelectionChanged += async (_, _) => { if (started && !downloading) await SearchAsync(); };
        models.SelectionChanged += async (_, _) => { if (!downloading && models.SelectedItem is ListViewItem { Tag: HubModel selected }) await SelectModelAsync(selected); };
        files.SelectionChanged += (_, _) => Describe();
        download.Click += async (_, _) => { if (!downloading) { activeDownload = DownloadAsync(); await activeDownload; } };
        close.Click += (_, _) => { if (downloading) { transfer?.Cancel(); close.IsEnabled = false; } else Close(); };
        Panel.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; if (downloading) transfer?.Cancel(); else Close(); } };
        Panel.Loaded += async (_, _) => { if (started) return; started = true; query.Focus(FocusState.Programmatic); await SearchAsync(); };
        Closed += async (_, _) =>
        {
            closed = true; lifetime.Cancel(); lookup?.Cancel(); transfer?.Cancel();
            try { if (activeDownload != null) await activeDownload; }
            finally { lifetime.Dispose(); completed.TrySetResult(result); }
        };
        Content = Panel; FluentDesign.WindowChrome(this);
        AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1160, Height = 880 });
    }

    static TextBlock Text(string value, double size = 14, bool secondary = false) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = secondary ? FluentDesign.Secondary : FluentDesign.Primary };
    static void Add(Grid grid, FrameworkElement child, int row = 0, int column = 0) { Grid.SetRow(child, row); Grid.SetColumn(child, column); grid.Children.Add(child); }
    static StackPanel Metric(string label, TextBlock value) => new() { Spacing = 4, Children = { Text(label, 11, true), value } };
    static Border Badge(string label) => new() { Background = FluentDesign.Resource("ControlSelectedBrush"), CornerRadius = new(4), Padding = new(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = label, FontSize = 10, Foreground = FluentDesign.Resource("AccentTextFillColorPrimaryBrush") } };
    static string Number(long value) => value.ToString("N0", UiText.Language == "en" ? System.Globalization.CultureInfo.GetCultureInfo("en-US") : System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));

    async Task SearchAsync()
    {
        if (closed || downloading) return;
        lookup?.Cancel(); using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); lookup = request; var sequence = ++revision;
        selectedModel = null; download.IsEnabled = false; models.Items.Clear(); files.Items.Clear(); repositoryLink.NavigateUri = null;
        details.Visibility = Visibility.Collapsed; emptyDetails.Visibility = Visibility.Visible; listHint.Visibility = Visibility.Visible;
        listHint.Text = status.Text = WorkflowText("Recherche en cours…", "Searching…"); count.Text = "";
        try
        {
            machine ??= await readMachine(request.Token);
            if (closed || sequence != revision) return;
            machineText.Text = machine.Summary;
            var found = await catalog.SearchAsync(query.Text, type.SelectedIndex == 1 ? "image" : "chat", token, request.Token);
            if (closed || sequence != revision) return;
            foreach (var model in found)
            {
                var card = new StackPanel { Spacing = 5 };
                var name = Text(model.Id.Split('/').Last(), 13); name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; name.MaxLines = 2; name.TextTrimming = TextTrimming.CharacterEllipsis;
                card.Children.Add(name);
                var subtitle = Text(model.Id.Split('/')[0], 11, true); subtitle.MaxLines = 1; subtitle.TextTrimming = TextTrimming.CharacterEllipsis; card.Children.Add(subtitle);
                var meta = new Grid { ColumnSpacing = 8 }; meta.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); meta.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                Add(meta, Badge(model.Purpose == "chat" ? "GGUF" : string.IsNullOrEmpty(model.Family) ? "IMAGE" : model.Family.ToUpperInvariant()));
                var popularity = Text(Number(model.Downloads) + WorkflowText(" téléchargements", " downloads"), 11, true); popularity.MaxLines = 1; popularity.TextTrimming = TextTrimming.CharacterEllipsis; Add(meta, popularity, 0, 1); card.Children.Add(meta);
                var item = new ListViewItem { Content = card, Tag = model, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(10, 10, 10, 10), Margin = new(0, 0, 0, 4) };
                AutomationProperties.SetName(item, model.Id); ToolTipService.SetToolTip(item, model.Id); models.Items.Add(item);
            }
            count.Text = WorkflowText($"{found.Count} résultats · par téléchargements", $"{found.Count} results · by downloads");
            listHint.Text = WorkflowText("Aucun modèle trouvé. Essayez un autre nom ou type de modèle.", "No models found. Try a different name or model type.");
            listHint.Visibility = found.Count == 0 ? Visibility.Visible : Visibility.Collapsed; status.Text = "";
            if (models.Items.Count > 0) models.SelectedIndex = 0;
        }
        catch (OperationCanceledException) { if (!closed && sequence == revision) listHint.Text = status.Text = WorkflowText("La recherche a expiré. Réessayez.", "Search timed out. Try again."); }
        catch (Exception ex) { if (!closed && sequence == revision) listHint.Text = status.Text = ex.Message; }
        finally { if (ReferenceEquals(lookup, request)) lookup = null; }
    }

    async Task SelectModelAsync(HubModel selected)
    {
        lookup?.Cancel(); using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); lookup = request; var sequence = ++revision;
        selectedModel = null; download.IsEnabled = false; files.Items.Clear(); readme.Children.Clear();
        details.Visibility = Visibility.Visible; emptyDetails.Visibility = Visibility.Collapsed;
        ShowMetadata(selected);
        compatibility.Text = WorkflowText("Sélectionnez un fichier pour voir sa compatibilité.", "Select a file to see its compatibility."); compatibilityReason.Text = "";
        fileCount.Text = status.Text = WorkflowText("Lecture des fichiers du dépôt…", "Reading repository files…");
        readme.Children.Add(Text(WorkflowText("Chargement de la fiche…", "Loading model card…"), 12, true));
        try
        {
            var response = await catalog.FilesAsync(selected, token, request.Token);
            if (closed || sequence != revision) return;
            selectedModel = response.Model; ShowMetadata(response.Model);
            foreach (var file in response.Files.OrderBy(x => Estimate(x)?.CanLoad == true ? 0 : 1).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var estimate = Estimate(file);
                var card = new Grid { ColumnSpacing = 10, RowSpacing = 2 };
                card.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); card.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                card.RowDefinitions.Add(new() { Height = GridLength.Auto }); card.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var filename = Text(file.Name, 12); filename.MaxLines = 2; filename.TextTrimming = TextTrimming.CharacterEllipsis; Add(card, filename);
                Add(card, Text(MachineCapabilities.Size(file.Bytes), 12, true), 0, 1);
                var support = Text(estimate?.Label ?? WorkflowText("À vérifier", "To verify"), 11, true); Add(card, support, 1); Grid.SetColumnSpan(support, 2);
                var item = new ListViewItem { Content = card, Tag = file, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(10, 6, 10, 6), Margin = new(0, 0, 0, 3) };
                AutomationProperties.SetName(item, file.Name); ToolTipService.SetToolTip(item, file.Name + "\n" + estimate?.Reason); files.Items.Add(item);
            }
            var preferred = files.Items.Cast<ListViewItem>().FirstOrDefault(x => x.Tag is HubModelFile f && f.Name.Contains("Q4_K_M", StringComparison.OrdinalIgnoreCase) && Estimate(f)?.CanLoad == true)
                ?? files.Items.Cast<ListViewItem>().FirstOrDefault(x => x.Tag is HubModelFile f && Estimate(f)?.CanLoad == true) ?? files.Items.Cast<ListViewItem>().FirstOrDefault();
            files.SelectedItem = preferred;
            fileCount.Text = response.Files.Count == 0 ? WorkflowText("Aucun fichier GGUF ou checkpoint Safetensors dans ce dépôt.", "No GGUF or Safetensors checkpoint files in this repository.") : WorkflowText($"{response.Files.Count} fichiers · choisissez une variante", $"{response.Files.Count} files · choose a variant");
            status.Text = ""; Describe();
            try
            {
                var markdown = await catalog.ReadmeAsync(response.Model, token, request.Token);
                if (closed || sequence != revision) return;
                readme.Children.Clear();
                if (string.IsNullOrWhiteSpace(markdown)) readme.Children.Add(Text(WorkflowText("Aucune fiche disponible. Consultez le dépôt sur Hugging Face.", "No model card available. Visit the repository on Hugging Face."), 12, true));
                else MarkdownRenderer.RenderTo(readme, markdown.Length > 24000 ? markdown[..24000] + WorkflowText("\n\nConsultez la fiche complète sur Hugging Face.", "\n\nRead the full model card on Hugging Face.") : markdown);
            }
            catch (OperationCanceledException) { }
            catch { if (!closed && sequence == revision) { readme.Children.Clear(); readme.Children.Add(Text(WorkflowText("Fiche indisponible ici. Ouvrez-la sur Hugging Face.", "Model card unavailable here. Open it on Hugging Face."), 12, true)); } }
        }
        catch (OperationCanceledException) { if (!closed && sequence == revision) fileCount.Text = status.Text = WorkflowText("La requête a expiré. Réessayez.", "Request timed out. Try again."); }
        catch (Exception ex) { if (!closed && sequence == revision) { fileCount.Text = status.Text = ex.Message; readme.Children.Clear(); } }
        finally { if (ReferenceEquals(lookup, request)) lookup = null; }
    }

    void ShowMetadata(HubModel model)
    {
        title.Text = model.Id.Split('/').Last(); author.Text = model.Id.Split('/')[0];
        description.Text = !string.IsNullOrWhiteSpace(model.Description) ? model.Description : model.BaseModel.Length > 0 ? WorkflowText("Modèle de base : ", "Base model: ") + model.BaseModel : WorkflowText("Dépôt Hugging Face · ", "Hugging Face repository · ") + (model.Purpose == "chat" ? "GGUF" : WorkflowText("génération d’image", "image generation"));
        architecture.Text = (model.Architecture.Length > 0 ? WorkflowText("Architecture : ", "Architecture: ") + model.Architecture + " · " : "") + WorkflowText("Licence : ", "Licence: ") + (model.License.Length > 0 ? model.License : WorkflowText("à consulter sur le dépôt", "see repository")) + (model.Gated ? WorkflowText(" · accès protégé", " · gated access") : "");
        downloads.Text = Number(model.Downloads); likes.Text = Number(model.Likes); modified.Text = model.LastModified?.ToString("yyyy-MM-dd") ?? "—";
        repositoryLink.NavigateUri = new Uri("https://huggingface.co/" + string.Join('/', model.Id.Split('/').Select(Uri.EscapeDataString)));
    }
    LocalCompatibility? Estimate(HubModelFile file) => selectedModel != null && machine != null ? LocalModelCompatibility.Estimate(file.Name, selectedModel.Purpose, selectedModel.Family, file.Bytes, machine, context, backend: config.Backend) : null;
    void Describe()
    {
        download.IsEnabled = false;
        if (files.SelectedItem is not ListViewItem { Tag: HubModelFile file } || Estimate(file) is not { } estimate) return;
        compatibility.Text = estimate.Label; compatibilityReason.Text = estimate.Reason;
        download.IsEnabled = estimate.CanLoad && !downloading && !closed;
    }
    async Task DownloadAsync()
    {
        if (closed || downloading || selectedModel == null || files.SelectedItem is not ListViewItem { Tag: HubModelFile file } || Estimate(file)?.CanLoad != true) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); transfer = cancellation; downloading = true;
        download.IsEnabled = false;
        models.IsEnabled = files.IsEnabled = search.IsEnabled = type.IsEnabled = query.IsEnabled = false;
        progress.Visibility = Visibility.Visible; progress.IsIndeterminate = true; close.Content = WorkflowText("Annuler le téléchargement", "Cancel download");
        status.Text = WorkflowText("Téléchargement en cours…", "Downloading…");
        var changes = new Progress<LocalTransferProgress>(value =>
        {
            if (closed || !ReferenceEquals(transfer, cancellation)) return;
            progress.IsIndeterminate = !value.Percent.HasValue; progress.Value = value.Percent ?? 0;
            status.Text = value.Detail + $" · {MachineCapabilities.Size(value.Bytes)} / {MachineCapabilities.Size(value.Total ?? 0)}" + (value.Percent is { } percent ? $" · {percent:0}%" : "");
        });
        try
        {
            var imported = await catalog.DownloadAsync(selectedModel, file, config, token, changes, cancellation.Token);
            try { imported.Family = await LocalModelImport.InspectAsync(imported.FullPath, imported.Purpose, cancellation.Token); }
            catch { File.Delete(imported.FullPath); throw; }
            result = imported; if (!closed) Close();
        }
        catch (OperationCanceledException) { if (!closed) status.Text = WorkflowText("Téléchargement annulé.", "Download cancelled."); }
        catch (Exception ex) { if (!closed) status.Text = ex.Message; }
        finally
        {
            downloading = false; transfer = null;
            if (!closed)
            {
                models.IsEnabled = files.IsEnabled = search.IsEnabled = type.IsEnabled = query.IsEnabled = close.IsEnabled = true;
                progress.Visibility = Visibility.Collapsed; close.Content = WorkflowText("Fermer", "Close"); Describe();
            }
        }
    }
}

public sealed partial class MainWindow
{
    HuggingFaceModelWindow? localModelsWindow;
    Func<LocalProviderSettings, string, int, ElementTheme, HuggingFaceModelWindow>? smokeLocalModelsWindowFactory;
    async Task<LocalModel?> SearchLocalModelsAsync(LocalProviderSettings config, string token, int context, CancellationToken ct)
    {
        if (localModelsWindow != null) { localModelsWindow.Activate(); return null; }
        ct.ThrowIfCancellationRequested();
        var window = smokeLocalModelsWindowFactory?.Invoke(config, token, context, root.RequestedTheme) ?? new(config, token, context, root.RequestedTheme);
        localModelsWindow = window;
        ApplyBrandingIcon(window); ObserveTextZoom(window.Panel);
        using var cancellation = ct.Register(() => DispatcherQueue.TryEnqueue(() => window.Close()));
        window.Activate();
        try { return await window.Completion; }
        finally { if (ReferenceEquals(localModelsWindow, window)) localModelsWindow = null; }
    }
}
