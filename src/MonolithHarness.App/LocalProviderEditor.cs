using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    StackPanel BuildLocalProviderEditor(ProviderDraft draft, Action changed)
    {
        var config = LocalProviderSettings.Read(draft.LocalModelsJson);
        var panel = new StackPanel { Spacing = 12 };
        var folder = new TextBox { Header = WorkflowText("Dossier des modèles", "Model directory"), Text = config.ModelDirectory };
        folder.Name = "LocalModelDirectory";
        var browseFolder = new Button { Name = "BrowseLocalModelDirectory", Content = T("Parcourir…"), VerticalAlignment = VerticalAlignment.Bottom };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(browseFolder, WorkflowText("Choisir le dossier des modèles", "Choose the model directory"));
        var folderRow = new Grid { ColumnSpacing = 8 };
        folderRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        folderRow.Children.Add(folder); Grid.SetColumn(browseFolder, 1); folderRow.Children.Add(browseFolder);
        var backend = new ComboBox { Header = WorkflowText("Calcul local", "Local computing"), ItemsSource = new[] { "auto", "cpu", "vulkan" }, SelectedItem = config.Backend, HorizontalAlignment = HorizontalAlignment.Stretch };
        var purpose = new ComboBox { Header = WorkflowText("Type de modèle à importer", "Model type to import"), ItemsSource = new[] { WorkflowText("Conversation · GGUF", "Chat · GGUF"), WorkflowText("Image · checkpoint complet", "Image · complete checkpoint") }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var copy = new ToggleSwitch { Header = WorkflowText("Copier les imports dans le dossier des modèles", "Copy imports into the model directory"), IsOn = true };
        var list = new ComboBox { Header = WorkflowText("Modèles importés · conversation et image", "Imported models · chat and image"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var machineInfo = Label(WorkflowText("Lecture des caractéristiques de la machine…", "Reading machine specifications…"), 12); machineInfo.Tag = null;
        var compatibility = Label("", 12); compatibility.Tag = null;
        var status = Label("", 12); status.Tag = null;
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
        var import = new Button { Content = WorkflowText("Importer un fichier…", "Import file…") };
        var search = new Button { Content = WorkflowText("Hugging Face…", "Hugging Face…") };
        var load = new Button { Content = WorkflowText("Préparer / charger", "Prepare / load"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var unload = new Button { Content = WorkflowText("Décharger", "Unload") };
        var remove = new Button { Content = WorkflowText("Retirer de la liste", "Remove from list") };
        var cancel = new Button { Content = WorkflowText("Annuler l’opération", "Cancel operation"), Visibility = Visibility.Collapsed };
        var customChat = new TextBox { Header = WorkflowText("llama-server installé (facultatif)", "Installed llama-server (optional)"), Text = config.ChatExecutable };
        var customImage = new TextBox { Header = WorkflowText("sd-cli installé (facultatif)", "Installed sd-cli (optional)"), Text = config.ImageExecutable };
        MachineCapabilities? machine = null; CancellationTokenSource? operation = null;
        void Save()
        {
            var selectedFolder = folder.Text.Trim();
            config.Directory = PlatformSupport.PathComparer.Equals(selectedFolder.TrimEnd(Path.DirectorySeparatorChar), Path.Combine(PortableStorage.Root, "model")) ? "" : selectedFolder;
            config.Backend = backend.SelectedItem as string ?? "auto";
            config.ChatExecutable = customChat.Text.Trim(); config.ImageExecutable = customImage.Text.Trim(); draft.LocalModelsJson = config.Json();
            var chatModels = config.Models.Where(x => x.Purpose == "chat").Select(x => x.Id).ToList();
            draft.DetectedModelsJson = System.Text.Json.JsonSerializer.Serialize(chatModels); draft.SelectedModelsJson = draft.DetectedModelsJson;
            if (!chatModels.Contains(draft.Model)) draft.Model = chatModels.FirstOrDefault() ?? "";
            draft.SupportsImages = false;
        }
        void Refresh(LocalModel? selected = null)
        {
            Save(); list.ItemsSource = config.Models.ToList(); list.SelectedItem = selected ?? config.Models.FirstOrDefault(x => x.Id == draft.Model) ?? config.Models.FirstOrDefault(); changed();
        }
        void Describe()
        {
            if (list.SelectedItem is not LocalModel selected) { compatibility.Text = WorkflowText("Importez ou téléchargez un modèle pour commencer.", "Import or download a model to start."); return; }
            var exists = File.Exists(selected.FullPath);
            var estimate = machine == null ? null : LocalModelCompatibility.Estimate(selected.FullPath, selected.Purpose, selected.Family, exists ? new FileInfo(selected.FullPath).Length : selected.Bytes, machine, draft.ContextLimit, backend: config.Backend);
            compatibility.Text = (selected.Purpose == "image" ? "Image · " + selected.Family.ToUpperInvariant() : "Conversation") + " · " + selected.Id + "\n" + selected.FullPath + "\n" +
                (!exists ? WorkflowText("Fichier introuvable.", "File not found.") : estimate?.Label + " · " + estimate?.Reason);
            load.IsEnabled = exists && (estimate?.CanLoad ?? true) && operation == null;
        }
        async Task Run(Func<CancellationToken, Task> action)
        {
            if (operation != null) return;
            operation = new(); draft.LocalBusy = true; var current = operation;
            foreach (var control in new Control[] { import, search, load, unload, remove, folder, browseFolder, backend, purpose, copy, list, customChat, customImage }) control.IsEnabled = false;
            cancel.Visibility = progress.Visibility = Visibility.Visible; progress.IsIndeterminate = true;
            try { Save(); await action(current.Token); }
            catch (OperationCanceledException) { status.Text = WorkflowText("Opération annulée.", "Operation cancelled."); }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { operation = null; draft.LocalBusy = false; current.Dispose(); foreach (var control in new Control[] { import, search, load, unload, remove, folder, browseFolder, backend, purpose, copy, list, customChat, customImage }) control.IsEnabled = true; cancel.Visibility = progress.Visibility = Visibility.Collapsed; Describe(); }
        }
        var transfer = new Progress<LocalTransferProgress>(value => { progress.IsIndeterminate = !value.Percent.HasValue; progress.Value = value.Percent ?? 0; status.Text = value.Detail + (value.Percent is { } percent ? $" · {percent:0}%" : ""); });
        folder.TextChanged += (_, _) => Save(); backend.SelectionChanged += (_, _) => Save(); customChat.TextChanged += (_, _) => Save(); customImage.TextChanged += (_, _) => Save();
        list.SelectionChanged += (_, _) => Describe(); cancel.Click += (_, _) => operation?.Cancel();
        browseFolder.Click += async (_, _) =>
        {
            browseFolder.IsEnabled = false;
            try
            {
                var selected = await PickLocalModelFolderAsync(settingsWindow ?? this);
                if (!string.IsNullOrWhiteSpace(selected) && operation == null) { folder.Text = selected; Save(); changed(); }
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { browseFolder.IsEnabled = operation == null; }
        };
        import.Click += async (_, _) => await Run(async ct =>
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker(); picker.FileTypeFilter.Add(".gguf"); picker.FileTypeFilter.Add(".safetensors"); InitializePicker(picker, settingsWindow ?? this);
            var file = await picker.PickSingleFileAsync(); if (file == null) return;
            var type = purpose.SelectedIndex == 1 ? "image" : "chat";
            var imported = await LocalModelImport.ImportAsync(file.Path, type, "", config, copy.IsOn, transfer, ct);
            config.Add(imported); Refresh(imported); status.Text = WorkflowText("Modèle importé. Enregistrez les réglages pour le conserver.", "Model imported. Save settings to keep it.");
        });
        search.Click += async (_, _) => await Run(async ct =>
        {
            var secret = draft.PendingKey.Length > 0 ? draft.PendingKey : draft.DeleteKey ? "" : KeyVault.Decrypt(draft.ProtectedKey);
            var imported = await SearchLocalModelsAsync(config, secret, draft.ContextLimit, ct);
            if (imported != null) { config.Add(imported); Refresh(imported); status.Text = WorkflowText("Modèle téléchargé. Enregistrez les réglages pour le conserver.", "Model downloaded. Save settings to keep it."); }
        });
        load.Click += async (_, _) => await Run(async ct =>
        {
            if (list.SelectedItem is not LocalModel selected) return;
            machine ??= await MachineCapabilities.ReadAsync(ct);
            status.Text = WorkflowText("Préparation du moteur officiel…", "Preparing the official engine…");
            await LocalRuntimeInstaller.EnsureAsync(config, selected.Purpose, machine, transfer, ct);
            if (selected.Purpose == "chat")
            {
                status.Text = WorkflowText("Chargement du modèle en mémoire…", "Loading the model into memory…");
                var target = new Provider { Kind = "local", Model = selected.Id, LocalModelsJson = config.Json(), ModelContextsJson = draft.ModelContextsJson };
                ModelContexts.MergeLocal(target);
                using var lease = await LocalModelRuntime.ChatAsync(target, ct);
                draft.Model = selected.Id; status.Text = WorkflowText("Modèle chargé et prêt.", "Model loaded and ready.");
            }
            else status.Text = WorkflowText("Moteur d’image prêt. Le checkpoint sera chargé à chaque génération. Sélectionnez-le dans le skill Génération d’image.", "Image engine ready. The checkpoint loads for each generation. Select it in the Image generation skill.");
            Save(); changed();
        });
        unload.Click += async (_, _) => await Run(async ct => { await LocalModelRuntime.UnloadAsync(config, ct); status.Text = WorkflowText("Modèles de conversation déchargés.", "Chat models unloaded."); });
        remove.Click += async (_, _) => await Run(async ct =>
        {
            if (list.SelectedItem is not LocalModel selected) return;
            await LocalModelRuntime.UnloadAsync(new LocalProviderSettings { Models = [selected] }, ct); config.Models.Remove(selected); Refresh();
            status.Text = WorkflowText("Retiré de la liste ; le fichier est conservé sur le disque.", "Removed from list; the file remains on disk.");
        });
        var actions = Row(unload, load); actions.HorizontalAlignment = HorizontalAlignment.Right;
        foreach (var item in new UIElement[] { folderRow, backend, new Expander { Header = WorkflowText("Caractéristiques de la machine", "Machine specifications"), Content = machineInfo, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch },
            list, compatibility, purpose, copy, Row(import, search, remove), new Expander { Header = WorkflowText("Moteurs déjà installés", "Already installed engines"), Content = new StackPanel { Children = { customChat, customImage } }, HorizontalAlignment = HorizontalAlignment.Stretch },
            status, progress, cancel, actions }) panel.Children.Add(item);
        panel.Children.Add(Label(WorkflowText("Les modèles restent dans model/ à côté de l’exécutable. Préparer télécharge uniquement un moteur officiel avec contrôle SHA-256. L’estimation dépend de la RAM, du contexte, de la VRAM et des pilotes ; la capacité réelle est confirmée au chargement. Les modèles vision nécessitant un projecteur ne sont pas gérés par cette bêta.",
            "Models stay in model/ next to the executable. Prepare downloads an official engine with SHA-256 checks. Capacity estimates depend on RAM, context, VRAM and drivers; loading confirms actual support. Vision models needing a projector are not supported in this beta."), 12));
        panel.Unloaded += (_, _) => operation?.Cancel();
        panel.Loaded += async (_, _) =>
        {
            try { machine = await MachineCapabilities.ReadAsync(CancellationToken.None); machineInfo.Text = machine.Summary; Describe(); }
            catch (Exception ex) { machineInfo.Text = ex.Message; }
        };
        Refresh(); return panel;
    }
    Func<Window, Task<string?>>? smokeLocalModelFolderPicker;
    async Task<string?> PickLocalModelFolderAsync(Window owner)
    {
        if (smokeLocalModelFolderPicker != null) return await smokeLocalModelFolderPicker(owner);
        var picker = new Windows.Storage.Pickers.FolderPicker(); picker.FileTypeFilter.Add("*"); InitializePicker(picker, owner);
        return (await picker.PickSingleFolderAsync())?.Path;
    }
}
