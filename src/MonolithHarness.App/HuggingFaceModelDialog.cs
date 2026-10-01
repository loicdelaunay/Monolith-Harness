using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    async Task<LocalModel?> SearchLocalModelsAsync(LocalProviderSettings config, string token, int context, XamlRoot owner, CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var catalog = new HuggingFaceModels(); var machine = await MachineCapabilities.ReadAsync(lifetime.Token);
        var query = new TextBox { PlaceholderText = WorkflowText("Nom, famille ou auteur…", "Name, family or author…"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var type = new ComboBox { ItemsSource = new[] { WorkflowText("Conversation · GGUF", "Chat · GGUF"), WorkflowText("Génération d’image", "Image generation") }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var search = new Button { Content = WorkflowText("Rechercher", "Search") };
        var models = new ListView { Height = 170, SelectionMode = ListViewSelectionMode.Single };
        var files = new ComboBox { Header = WorkflowText("Fichier / quantification", "File / quantization"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var machineText = Label(machine.Summary, 12); machineText.Tag = null;
        var detail = Label(WorkflowText("Recherchez puis sélectionnez un modèle pour voir ses fichiers et l’estimation de support.", "Search and select a model to see its files and support estimate."), 13); detail.Tag = null;
        var status = Label("", 12); status.Tag = null;
        var progress = new ProgressBar { Visibility = Visibility.Collapsed, Minimum = 0, Maximum = 100 };
        var body = new StackPanel { Spacing = 10, Width = 660, MaxWidth = Math.Max(280, owner.Size.Width - 100) };
        var repositoryLink = new HyperlinkButton { Content = WorkflowText("Voir la licence et la fiche sur Hugging Face", "View licence and model card on Hugging Face"), Visibility = Visibility.Collapsed };
        foreach (var item in new UIElement[] { type, query, search, new Expander { Header = WorkflowText("Votre machine", "Your machine"), Content = machineText, HorizontalAlignment = HorizontalAlignment.Stretch }, models, files, detail, repositoryLink, status, progress }) body.Children.Add(item);
        body.Children.Add(Label(WorkflowText("Estimation, pas garantie de débit. GGUF autonome pour le chat ; checkpoints complets SD 1.x, SD 2.x et SDXL pour les images. Les composants et les autres familles restent visibles avec leur motif de non-support. Un dépôt protégé exige un jeton et l’acceptation de sa licence sur Hugging Face.",
            "Estimates do not guarantee throughput. Standalone GGUF for chat; full SD 1.x, SD 2.x and SDXL checkpoints for images. Other families and components remain visible with reasons. Gated repositories require a token and accepting their licence on Hugging Face."), 12));
        var dialog = new ContentDialog { XamlRoot = owner, RequestedTheme = root.RequestedTheme, Title = WorkflowText("Modèles locaux · Hugging Face", "Local models · Hugging Face"),
            Content = new ScrollViewer { Content = body, MaxHeight = Math.Max(320, owner.Size.Height - 240) }, PrimaryButtonText = WorkflowText("Télécharger et importer", "Download and import"), CloseButtonText = T("Fermer"), DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = false };
        dialog.Resources["ContentDialogMaxWidth"] = Math.Clamp(owner.Size.Width - 80, 320, 800);
        LocalModel? result = null; HubModel? selectedModel = null; CancellationTokenSource? lookup = null; bool closed = false, downloading = false; int revision = 0;
        TaskCompletionSource<bool>? downloadFinished = null;
        void Describe()
        {
            if (selectedModel == null || files.SelectedItem is not HubModelFile file) { dialog.IsPrimaryButtonEnabled = false; return; }
            var estimate = LocalModelCompatibility.Estimate(file.Name, selectedModel.Purpose, selectedModel.Family, file.Bytes, machine, context, backend: config.Backend);
            detail.Text = estimate.Label + "\n" + estimate.Reason + "\n" + WorkflowText("Licence : ", "Licence: ") + selectedModel.License + (selectedModel.Gated ? WorkflowText(" · accès protégé", " · gated access") : "");
            dialog.IsPrimaryButtonEnabled = estimate.CanLoad && !downloading;
        }
        async Task Search()
        {
            lookup?.Cancel(); lookup = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var request = lookup; var sequence = ++revision;
            status.Text = WorkflowText("Recherche en cours…", "Searching…"); dialog.IsPrimaryButtonEnabled = false; selectedModel = null; files.ItemsSource = null;
            try
            {
                var found = await catalog.SearchAsync(query.Text, type.SelectedIndex == 1 ? "image" : "chat", token, request.Token);
                if (closed || sequence != revision) return;
                models.ItemsSource = found; status.Text = WorkflowText($"{found.Count} résultats, triés par téléchargements.", $"{found.Count} results, ordered by downloads.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed && sequence == revision) status.Text = ex.Message; }
        }
        search.Click += async (_, _) => await Search(); query.KeyDown += async (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; await Search(); } };
        type.SelectionChanged += async (_, _) => { if (!downloading) await Search(); };
        models.SelectionChanged += async (_, _) =>
        {
            if (downloading || models.SelectedItem is not HubModel selected) return;
            lookup?.Cancel(); lookup = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var request = lookup; var sequence = ++revision;
            selectedModel = null; dialog.IsPrimaryButtonEnabled = false; files.ItemsSource = null; status.Text = WorkflowText("Lecture des fichiers du dépôt…", "Reading repository files…");
            try
            {
                var response = await catalog.FilesAsync(selected, token, request.Token); if (closed || sequence != revision) return;
                selectedModel = response.Model; files.ItemsSource = response.Files; files.SelectedItem = response.Files.FirstOrDefault(x => x.Name.Contains("Q4_K_M", StringComparison.OrdinalIgnoreCase)) ?? response.Files.FirstOrDefault();
                repositoryLink.NavigateUri = new Uri("https://huggingface.co/" + selected.Id); repositoryLink.Visibility = Visibility.Visible;
                status.Text = response.Files.Count == 0 ? WorkflowText("Aucun fichier chargeable par cette bêta dans ce dépôt.", "No files loadable by this beta in this repository.") : WorkflowText($"{response.Files.Count} fichiers de modèle.", $"{response.Files.Count} model files."); Describe();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed && sequence == revision) status.Text = ex.Message; }
        };
        files.SelectionChanged += (_, _) => Describe();
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            e.Cancel = true; if (downloading || selectedModel == null || files.SelectedItem is not HubModelFile selected) return;
            downloading = true; dialog.IsPrimaryButtonEnabled = false; models.IsEnabled = files.IsEnabled = search.IsEnabled = type.IsEnabled = query.IsEnabled = false; progress.Visibility = Visibility.Visible;
            downloadFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.CloseButtonText = WorkflowText("Annuler le téléchargement", "Cancel download");
            var transfer = new Progress<LocalTransferProgress>(value => { if (closed) return; progress.IsIndeterminate = !value.Percent.HasValue; progress.Value = value.Percent ?? 0; status.Text = value.Detail + $" · {MachineCapabilities.Size(value.Bytes)} / {MachineCapabilities.Size(value.Total ?? 0)}" + (value.Percent is { } percentage ? $" · {percentage:0}%" : ""); });
            try
            {
                var imported = await catalog.DownloadAsync(selectedModel, selected, config, token, transfer, lifetime.Token);
                try { imported.Family = await LocalModelImport.InspectAsync(imported.FullPath, imported.Purpose, lifetime.Token); }
                catch { File.Delete(imported.FullPath); throw; }
                result = imported; dialog.Hide();
            }
            catch (OperationCanceledException) { if (!closed) status.Text = WorkflowText("Téléchargement annulé.", "Download cancelled."); }
            catch (Exception ex) { if (!closed) status.Text = ex.Message; }
            finally { if (!closed) { downloading = false; models.IsEnabled = files.IsEnabled = search.IsEnabled = type.IsEnabled = query.IsEnabled = true; dialog.CloseButtonText = T("Fermer"); Describe(); } downloadFinished?.TrySetResult(true); }
        };
        dialog.Closed += (_, _) => { closed = true; lifetime.Cancel(); lookup?.Cancel(); };
        using var cancellation = ct.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
        try { await dialog.ShowAsync(); }
        finally { closed = true; lifetime.Cancel(); lookup?.Cancel(); if (downloadFinished != null) await downloadFinished.Task; }
        return result;
    }
}
