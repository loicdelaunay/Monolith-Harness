using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    sealed class ProviderDraft
    {
        public string LocalModelsJson { get; set; } = "{}";
        public bool LocalBusy { get; set; }
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string Model { get; set; } = "";
        public byte[] ProtectedKey { get; set; } = [];
        public string PendingKey { get; set; } = "";
        public bool DeleteKey { get; set; }
        public bool EditingKey { get; set; }
        public int ContextLimit { get; set; } = ModelContexts.DefaultContextLimit;
        public string ModelContextsJson { get; set; } = "{}";
        public bool SupportsImages { get; set; } = true;
        public string Kind { get; set; } = "openai";
        public string CompositeJson { get; set; } = "";
        public string DetectedModelsJson { get; set; } = "[]";
        public string SelectedModelsJson { get; set; } = "";
        public string Username { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string AcpArgumentsJson { get; set; } = "[]";
        public bool AutoStart { get; set; }
        public bool OpenCodeTools { get; set; }
        public bool BypassFreeLimitation { get; set; }
        public bool ModelsExpanded { get; set; }
        public string ModelSearch { get; set; } = "";
        public int ModelPage { get; set; }
        public bool ModelsLoading { get; set; }
        public string ModelStatus { get; set; } = "";
        public string ConnectionStatus { get; set; } = "";
        public InfoBarSeverity ConnectionSeverity { get; set; } = InfoBarSeverity.Informational;
        public int ConnectionRevision { get; set; }
        public override string ToString() => string.IsNullOrWhiteSpace(Model) ? Name : $"{Name} · {Model}";
    }

    sealed class ProviderEditorState
    {
        public required Grid Panel { get; init; }
        public required SettingsHeaderPanel Header { get; init; }
        public required StackPanel Cards { get; init; }
        public required List<ProviderDraft> Drafts { get; init; }
        public required TextBlock Error { get; init; }
        public ProviderDraft? Selected { get; set; }
        public event Action<ProviderDraft>? Removed;
        public void NotifyRemoved(ProviderDraft draft) => Removed?.Invoke(draft);
        public Action Commit { get; set; } = () => { };
        public Action ViewChanged { get; set; } = () => { };
        public Action Changed { get; set; } = () => { };
        public Func<bool> HasChanges { get; set; } = () => false;
    }

    ProviderEditorState BuildProviderEditor(int selectedProviderId)
    {
        var drafts = db.Providers.Local.Where(x => db.Entry(x).State != EntityState.Deleted).OrderBy(x => x.Id)
            .Select(x => new ProviderDraft { Id = x.Id, Name = x.Name, BaseUrl = x.BaseUrl, Model = x.Model, ProtectedKey = [.. x.ProtectedKey], ContextLimit = x.ContextLimit, ModelContextsJson = x.ModelContextsJson, SupportsImages = x.SupportsImages,
                DetectedModelsJson=x.DetectedModelsJson, SelectedModelsJson=x.SelectedModelsJson, Kind = x.Kind, CompositeJson=x.CompositeJson, LocalModelsJson=x.LocalModelsJson, Username = x.Username, ExecutablePath = x.ExecutablePath, AcpArgumentsJson = x.AcpArgumentsJson, AutoStart = x.AutoStart, OpenCodeTools = x.OpenCodeTools, BypassFreeLimitation = x.BypassFreeLimitation })
            .ToList();
        var view = new ProviderConnectionLayout();
        var cards = view.Rows;
        var state = new ProviderEditorState { Panel = new Grid(), Header = new SettingsHeaderPanel { Spacing = 8 }, Cards = cards, Drafts = drafts, Error = Label("", 12) };
        state.Error.Tag = null;

        var editor = view.Connection;
        var editorTitle = view.Title;
        var name = new TextBox { Header = WorkflowText("Nom de la connexion", "Connection name"), MaxLength = 100 };
        var url = new TextBox { Header = T("URL de base de l’API") };
        var key = new PasswordBox { Header = T("Clé API") };
        var keyNote = Label("", 12); keyNote.Foreground = FluentDesign.Secondary;
        var replaceKey = new Button { Content = WorkflowText("Remplacer", "Replace"), VerticalAlignment = VerticalAlignment.Bottom };
        var keyRow = new Grid { ColumnSpacing = 8 };
        keyRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        keyRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        keyRow.Children.Add(key); Grid.SetColumn(replaceKey, 1); keyRow.Children.Add(replaceKey);
        var model = new ComboBox { Header = WorkflowText("Modèle par défaut", "Default model"), IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        var vertexFields = new VertexProviderFields { Visibility = Visibility.Collapsed };
        var limit = new ModelContextEditor();
        var vision = new CheckBox { Content = T("Ce modèle accepte les images") };
        var deleteKey = new CheckBox { Content = T("Supprimer la clé enregistrée") };
        var username = new TextBox { Header = T("Utilisateur OpenCode"), PlaceholderText = "opencode" };
        var executable = new TextBox { Header = T("Chemin vers opencode.exe (facultatif)"), PlaceholderText = @"C:\…\OpenCode.exe", HorizontalAlignment = HorizontalAlignment.Stretch };
        var browseExecutable = new Button { Content = T("Parcourir…"), VerticalAlignment = VerticalAlignment.Bottom };
        var execGrid = new Grid();
        execGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        execGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(executable, 0); Grid.SetColumn(browseExecutable, 1);
        browseExecutable.Margin = new Thickness(8, 0, 0, 0);
        execGrid.Children.Add(executable); execGrid.Children.Add(browseExecutable);
        var acpArguments = new TextBox { Header = WorkflowText("Arguments ACP (tableau JSON, facultatif)", "ACP arguments (optional JSON array)"), Text = "[]" };
        var launchPanel = new StackPanel { Spacing = 12 }; launchPanel.Children.Add(execGrid); launchPanel.Children.Add(acpArguments);
        var connectionLaunch = new Expander { Name = "AcpLaunchOptions", Header = WorkflowText("Options de lancement (avancé)", "Launch options (advanced)"), Content = launchPanel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var connectAccount = new Button { Name = "AcpConnectAccount", Content = WorkflowText("Connecter mon compte ChatGPT", "Connect my ChatGPT account"), HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var autoStart = new CheckBox { Content = T("Démarrer automatiquement le serveur OpenCode") };
        var openCodeTools = new CheckBox { Content = T("Activer les outils agent OpenCode avec demande d’autorisation") };
        var bypassFreeLimitation = new CheckBox { Content = T("BETA Bypass free limitation (utiliser les modèles gratuits sur un autre harnais)") };
        ToolTipService.SetToolTip(bypassFreeLimitation, T("Permet d’utiliser les modèles gratuits sur un autre harnais"));
        var info = Label(T("Créez autant de connexions que nécessaire. Chaque instance conserve sa propre clé, son URL, son modèle et sa limite de contexte."), 12); info.Tag = null;
        var testConnection = new Button { Content = T("Tester la connexion") };
        var connectionResult = new InfoBar { Name = "ProviderConnectionResult", IsOpen = false, IsClosable = false,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(connectionResult, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var modelsResult = new InfoBar { Name = "ProviderModelsResult", IsOpen = false, IsClosable = false };
        // The server owns its providers: saving never deletes or rewrites them, so the editor shows them read-only.
        var managed = ServerManagedProviders();
        bool Managed(ProviderDraft? draft) => draft is { Id: > 0 } && managed.Contains(draft.Id);
        bool CanEdit(ProviderDraft? draft) => draft != null && !Managed(draft);
        var managedNotice = new InfoBar { Name = "ServerManagedProvider", IsOpen = false, IsClosable = false, Severity = InfoBarSeverity.Informational,
            Title = WorkflowText("Connexion fournie par votre serveur Monolith", "Connection provided by your Monolith server"),
            Message = WorkflowText("Le serveur la synchronise : elle n’est ni modifiable ni supprimable ici. Retirez-la côté serveur ou déconnectez-vous dans Réglages > Serveur.",
                "The server synchronizes it: it cannot be edited or deleted here. Remove it on the server or sign out in Settings > Server.") };
        var modelsHost = new StackPanel { Spacing = 12 };
        var modelsBusy = new ProgressRing { Width = 22, Height = 22, IsActive = false, Visibility = Visibility.Collapsed };
        var importModels = new Button { Content = T("Importer les modèles OpenCode") };
        var editorBusy = new ProgressRing { Width = 22, Height = 22, IsActive = false, Visibility = Visibility.Collapsed };
        var addOpenAi = new MenuFlyoutItem { Text = T("+ OpenAI compatible") };
        var addDeepSeek = new MenuFlyoutItem { Text = "+ DeepSeek" };
        var addOpenCode = new MenuFlyoutItem { Text = "+ OpenCode" };
        var acpItems = AcpProviders.Presets.Select(preset => (Preset: preset, Item: new MenuFlyoutItem { Text = "+ " + preset.Name })).ToList();
        var addComposite = new MenuFlyoutItem { Text = "+ Modèle composé / Composite" };
        var addLocal = new MenuFlyoutItem { Text = "+ Local · Bêta" };
        var cloudItems = ProviderPresets.Cloud.Select(preset =>
        {
            var item = new MenuFlyoutItem { Text = "+ " + preset.Name };
            ToolTipService.SetToolTip(item, WorkflowText(preset.DescriptionFr, preset.DescriptionEn));
            return (Preset: preset, Item: item);
        }).ToList();
        var providerHelp = new StackPanel { Spacing = 8 };
        var compositePanel=new StackPanel();
        var localPanel = new StackPanel();
        var duplicate = new MenuFlyoutItem { Text = T("Dupliquer"), Icon = new FontIcon { Glyph = "\uE8C8" } };
        var remove = new MenuFlyoutItem { Text = WorkflowText("Supprimer la connexion", "Delete connection"), Icon = new FontIcon { Glyph = "\uE74D" } };
        var actionMenu = new MenuFlyout(); actionMenu.Items.Add(duplicate); actionMenu.Items.Add(new MenuFlyoutSeparator()); actionMenu.Items.Add(remove);
        view.More.Flyout = actionMenu;
        var listRows = new Dictionary<ProviderDraft, (Button Button, TextBlock Name, Border Surface)>();
        string Snapshot() => System.Text.Json.JsonSerializer.Serialize(drafts.Select(d => new { d.Id, d.Name, d.Kind, d.BaseUrl, d.Model, d.ProtectedKey, d.PendingKey, d.DeleteKey,
            d.ContextLimit, d.ModelContextsJson, d.SupportsImages, d.DetectedModelsJson, d.SelectedModelsJson, d.CompositeJson, d.LocalModelsJson,
            d.Username, d.ExecutablePath, d.AcpArgumentsJson, d.AutoStart, d.OpenCodeTools, d.BypassFreeLimitation }));
        var original = Snapshot(); state.HasChanges = () => Snapshot() != original;
        bool refreshing = false, connectionBusy = false;

        void RenderConnectionResult(ProviderDraft? draft)
        {
            connectionResult.Message = draft?.ConnectionStatus ?? "";
            connectionResult.Severity = draft?.ConnectionSeverity ?? InfoBarSeverity.Informational;
            connectionResult.IsOpen = !string.IsNullOrEmpty(draft?.ConnectionStatus);
            modelsResult.Message = connectionResult.Message; modelsResult.Severity = connectionResult.Severity; modelsResult.IsOpen = connectionResult.IsOpen;
            connectionResult.Visibility = modelsResult.Visibility = connectionResult.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        }
        void ShowConnectionResult(ProviderDraft draft, string message, InfoBarSeverity severity)
        {
            // A provider response must never echo the credential typed into this form.
            if (!string.IsNullOrWhiteSpace(draft.PendingKey)) message = message.Replace(draft.PendingKey.Trim(), "[secret]", StringComparison.Ordinal);
            draft.ConnectionStatus = message; draft.ConnectionSeverity = severity;
            if (state.Selected == draft) RenderConnectionResult(draft);
        }
        void InvalidateConnectionResult()
        {
            if (refreshing || state.Selected is not { } draft) return;
            draft.ConnectionRevision++; draft.ConnectionStatus = "";
            RenderConnectionResult(draft);
        }
        string NextName(string baseName)
        {
            var index = 1; var candidate = baseName;
            while (drafts.Any(x => string.Equals(x.Name, candidate, StringComparison.OrdinalIgnoreCase))) candidate = baseName + " " + ++index;
            return candidate;
        }

        Provider AsProvider(ProviderDraft draft) => new() { Name = draft.Name, BaseUrl = draft.BaseUrl, Model = draft.Model, ContextLimit = draft.ContextLimit, SupportsImages = draft.SupportsImages,
            DetectedModelsJson=draft.DetectedModelsJson, SelectedModelsJson=draft.SelectedModelsJson, Kind = draft.Kind, LocalModelsJson=draft.LocalModelsJson, Username = draft.Username, ExecutablePath = draft.ExecutablePath, AcpArgumentsJson = draft.AcpArgumentsJson, AutoStart = draft.AutoStart, OpenCodeTools = draft.OpenCodeTools, BypassFreeLimitation = draft.BypassFreeLimitation, ModelContextsJson = draft.ModelContextsJson };
        void RefreshLimit(ProviderDraft? draft)
        {
            var target = draft == null ? null : AsProvider(draft);
            if (draft != null) { ModelContexts.MergeLocal(target!); draft.ModelContextsJson = target!.ModelContextsJson; draft.ContextLimit = target.ContextLimit; }
            limit.Bind(target, changed => { if (draft != null) { draft.ModelContextsJson = changed.ModelContextsJson; draft.ContextLimit = changed.ContextLimit; if (!refreshing) state.Changed(); } });
        }
        void Select(ProviderDraft? draft)
        {
            refreshing = true; state.Selected = draft;
            RenderConnectionResult(draft);
            editorTitle.Text = draft?.Name ?? "";
            view.Description.Text = ProviderDescription(draft);
            view.Logo.Content = draft == null ? null : ProviderConnectionIcon(draft, 32);
            var enabled = draft != null;
            var editable = CanEdit(draft);
            foreach (var control in new Control[] { name, url, key, model, limit, vision, deleteKey, testConnection, importModels, username, executable, browseExecutable, autoStart, openCodeTools, bypassFreeLimitation, acpArguments, connectAccount, vertexFields }) control.IsEnabled = editable;
            if (connectionBusy) testConnection.IsEnabled = importModels.IsEnabled = connectAccount.IsEnabled = false;
            duplicate.IsEnabled = enabled; remove.IsEnabled = editable;
            managedNotice.IsOpen = Managed(draft); managedNotice.Visibility = managedNotice.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            name.Text = draft?.Name ?? ""; url.Text = draft?.BaseUrl ?? ""; model.Text = draft?.Model ?? "";
            model.ItemsSource = draft == null ? null : ModelCatalog.GetModelsForProvider(AsProvider(draft));
            model.SelectedItem = draft?.Model; model.Text = draft?.Model ?? "";
            RefreshLimit(draft); vision.IsChecked = draft?.SupportsImages ?? true; deleteKey.IsChecked = draft?.DeleteKey ?? false;
            username.Text = draft?.Username ?? ""; executable.Text = draft?.ExecutablePath ?? ""; autoStart.IsChecked = draft?.AutoStart ?? false;
            openCodeTools.IsChecked = draft?.OpenCodeTools ?? false;
            bypassFreeLimitation.IsChecked = draft?.BypassFreeLimitation ?? false;
            acpArguments.Text = draft?.AcpArgumentsJson ?? "[]";
            var vertex = draft?.Kind == "vertex";
            vertexFields.Visibility = vertex ? Visibility.Visible : Visibility.Collapsed;
            if (vertex) vertexFields.Bind(draft!.BaseUrl);
            var acp = AcpProviders.Find(draft?.Kind) != null;
            var openCode = draft?.Kind == "opencode";
            connectionLaunch.Visibility = acp || openCode ? Visibility.Visible : Visibility.Collapsed; connectionLaunch.IsExpanded = openCode;
            info.Text = acp ? WorkflowText("Connexion au compte de l’agent, sans clé API dans l’application.", "Connect to the agent account without an API key in the application.") : T("Créez autant de connexions que nécessaire. Chaque instance conserve sa propre clé, son URL, son modèle et sa limite de contexte.");
            username.Visibility = execGrid.Visibility = autoStart.Visibility = openCodeTools.Visibility = bypassFreeLimitation.Visibility = openCode ? Visibility.Visible : Visibility.Collapsed;
            if (acp) { execGrid.Visibility = openCodeTools.Visibility = Visibility.Visible; }
            acpArguments.Visibility = acp ? Visibility.Visible : Visibility.Collapsed;
            connectAccount.Visibility = draft?.Kind == "chatgpt-acp" ? Visibility.Visible : Visibility.Collapsed;
            executable.Header = acp ? WorkflowText("Exécutable ACP personnalisé (facultatif)", "Custom ACP executable (optional)") : T("Chemin vers opencode.exe (facultatif)");
            executable.PlaceholderText = acp ? WorkflowText("Vide : adaptateur installé automatiquement via Node.js", "Blank: adapter installed automatically through Node.js") : @"C:\…\OpenCode.exe";
            openCodeTools.Content = acp ? WorkflowText("Activer les outils natifs de l’agent ACP", "Enable ACP agent native tools") : T("Activer les outils agent OpenCode avec demande d’autorisation");
            key.Header = WorkflowText(openCode ? "Mot de passe du serveur" : "Clé API", openCode ? "Server password" : "API key");
            importModels.Content = T(openCode ? "Importer les modèles OpenCode" : "Importer les modèles");
            key.Password = draft?.PendingKey ?? "";
            RefreshKeyState();
            var composite=draft?.Kind=="composite";
            ShowProviderHelp(draft);
            foreach(var control in new UIElement[]{url,keyRow,keyNote,model,limit,vision,deleteKey,testConnection,importModels})control.Visibility=composite?Visibility.Collapsed:Visibility.Visible;
            if (acp || vertex) url.Visibility = Visibility.Collapsed;
            if (acp || vertex && !VertexAi.UsesApiKey(draft!.BaseUrl)) keyRow.Visibility = keyNote.Visibility = deleteKey.Visibility = Visibility.Collapsed;
            compositePanel.Visibility = composite ? Visibility.Visible : Visibility.Collapsed;
            localPanel.Visibility = draft?.Kind == "local" ? Visibility.Visible : Visibility.Collapsed;
            compositePanel.Children.Clear();if(composite)compositePanel.Children.Add(BuildCompositeModelEditor(draft!,drafts));
            localPanel.Children.Clear();
            if (draft?.Kind == "local")
            {
                foreach (var control in new UIElement[] { url, model, vision, testConnection, importModels }) control.Visibility = Visibility.Collapsed;
                key.Header = WorkflowText("Jeton Hugging Face de lecture (facultatif)", "Hugging Face read token (optional)");
                localPanel.Children.Add(BuildLocalProviderEditor(draft, () => { model.ItemsSource = ProviderModels.Available(AsProvider(draft)); model.Text = draft.Model; RefreshLimit(draft); BuildSelectedModels(); state.Changed(); }));
            }
            refreshing = false;
            RefreshKeyState(); BuildSelectedModels();
        }
        void RefreshKeyState()
        {
            var draft = state.Selected;
            var stored = draft?.ProtectedKey.Length > 0 && draft.DeleteKey == false;
            key.IsEnabled = CanEdit(draft) && (!stored || draft!.EditingKey || draft.PendingKey.Length > 0);
            replaceKey.Visibility = stored && CanEdit(draft) ? Visibility.Visible : Visibility.Collapsed;
            replaceKey.Content = draft?.EditingKey == true ? WorkflowText("Conserver", "Keep saved") : WorkflowText("Remplacer", "Replace");
            key.PlaceholderText = stored ? "••••••••••••••••" : T("Votre clé API");
            keyNote.Text = draft?.PendingKey.Length > 0 ? WorkflowText("La nouvelle clé sera appliquée à l’enregistrement.", "The new key will be applied when saved.")
                : draft?.DeleteKey == true ? WorkflowText("La clé enregistrée sera supprimée.", "The saved key will be removed.")
                : stored ? WorkflowText("Clé enregistrée sur cet appareil.", "Key saved on this device.") : WorkflowText("Clé conservée dans le profil de cette application.", "The key is stored in this application's profile.");
        }
        void Refresh(ProviderDraft? draft)
        {
            var changed = state.Selected != draft;
            refreshing = true; state.Selected = draft;
            Select(draft);
            if (changed) view.ShowPage(0); RenderCards(); view.ShowSelection(draft != null);
            if (changed) state.ViewChanged();
            state.Changed();
        }
        state.Commit = () =>
        {
            if (refreshing || state.Selected == null || !drafts.Contains(state.Selected)) return;
            if(state.Selected.Kind=="composite"){state.Selected.Name=name.Text;return;}
            if (state.Selected.Kind == "local")
            {
                state.Selected.Name = name.Text; state.Selected.PendingKey = key.Password; state.Selected.DeleteKey = deleteKey.IsChecked == true;
                RefreshLimit(state.Selected);
                return;
            }
            SetDefaultModel(model.Text);
            state.Selected.Name = name.Text; state.Selected.BaseUrl = url.Text;
            RefreshLimit(state.Selected);
            state.Selected.SupportsImages = vision.IsChecked == true; state.Selected.DeleteKey = deleteKey.IsChecked == true; state.Selected.PendingKey = key.Password;
            state.Selected.AcpArgumentsJson = acpArguments.Text;
            state.Selected.Username = username.Text; state.Selected.ExecutablePath = executable.Text; state.Selected.AutoStart = autoStart.IsChecked == true;
            state.Selected.OpenCodeTools = openCodeTools.IsChecked == true;
            state.Selected.BypassFreeLimitation = bypassFreeLimitation.IsChecked == true;
        };

        vertexFields.EndpointChanged += value =>
        {
            if (refreshing || state.Selected?.Kind != "vertex") return;
            url.Text = value; state.Selected.BaseUrl = value;
            keyRow.Visibility = keyNote.Visibility = deleteKey.Visibility = vertexFields.UsesApiKey ? Visibility.Visible : Visibility.Collapsed;
            ShowProviderHelp(state.Selected);
        };
        name.TextChanged += (_, _) =>
        {
            if (refreshing || state.Selected == null) return;
            state.Selected.Name = name.Text; editorTitle.Text = name.Text;
            if (listRows.TryGetValue(state.Selected, out var row)) row.Name.Text = name.Text;
            state.Changed();
        };
        url.TextChanged += (_, _) => { if (!refreshing && state.Selected != null && state.Selected.BaseUrl != url.Text) { state.Selected.BaseUrl = url.Text; InvalidateConnectionResult(); } };
        url.LostFocus += (_, _) =>
        {
            if (refreshing || state.Selected is not { } draft) return;
            refreshing = true; model.ItemsSource = ModelCatalog.GetModelsForProvider(AsProvider(draft)); model.SelectedItem = draft.Model; model.Text = draft.Model; refreshing = false;
            view.Description.Text = ProviderDescription(draft); ShowProviderHelp(draft); BuildSelectedModels();
        };
        key.PasswordChanged += (_, _) => { if (!refreshing && state.Selected != null && state.Selected.PendingKey != key.Password) { state.Selected.PendingKey = key.Password; InvalidateConnectionResult(); RefreshKeyState(); state.Changed(); } };
        replaceKey.Click += (_, _) =>
        {
            if (state.Selected is not { } draft) return;
            draft.EditingKey = !draft.EditingKey;
            if (!draft.EditingKey) { draft.PendingKey = ""; key.Password = ""; }
            RefreshKeyState(); if (draft.EditingKey) key.Focus(FocusState.Programmatic);
        };
        void SetDefaultModel(string value)
        {
            if (refreshing || state.Selected is not { } draft) return;
            var id = draft.Kind == "vertex" ? VertexAi.NormalizeModel(value) : value.Trim();
            if (id == draft.Model) return;
            draft.Model = id;
            if (id.Length > 0) draft.SelectedModelsJson = System.Text.Json.JsonSerializer.Serialize(ProviderModels.Normalize(ProviderModels.Visible(AsProvider(draft)).Append(id)));
            InvalidateConnectionResult(); RefreshLimit(draft); BuildSelectedModels(); state.Changed();
        }
        model.TextSubmitted += (_, args) => SetDefaultModel(args.Text);
        model.SelectionChanged += (_, _) => { if (model.SelectedItem is string selected) SetDefaultModel(selected); };
        model.LostFocus += (_, _) => SetDefaultModel(model.Text);
        vision.Checked += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.SupportsImages = true; };
        vision.Unchecked += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.SupportsImages = false; };
        deleteKey.Checked += (_, _) => { if (!refreshing && state.Selected != null) { state.Selected.DeleteKey = true; InvalidateConnectionResult(); } };
        deleteKey.Unchecked += (_, _) => { if (!refreshing && state.Selected != null) { state.Selected.DeleteKey = false; InvalidateConnectionResult(); } };
        username.TextChanged += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.Username = username.Text; };
        acpArguments.TextChanged += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.AcpArgumentsJson = acpArguments.Text; };
        executable.TextChanged += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.ExecutablePath = executable.Text; };
        browseExecutable.Click += async (_, _) =>
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.FileTypeFilter.Add(".exe");
                picker.FileTypeFilter.Add("*");
                InitializePicker(picker, settingsWindow ?? this);
                var file = await picker.PickSingleFileAsync();
                if (file != null && state.Selected != null)
                {
                    executable.Text = file.Path;
                    state.Selected.ExecutablePath = file.Path;
                }
            }
            catch (Exception ex) { info.Text = ex.Message; }
        };
        autoStart.Checked += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.AutoStart = true; };
        autoStart.Unchecked += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.AutoStart = false; };
        openCodeTools.Checked += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.OpenCodeTools = true; };
        openCodeTools.Unchecked += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.OpenCodeTools = false; };
        bypassFreeLimitation.Checked += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.BypassFreeLimitation = true; };
        bypassFreeLimitation.Unchecked += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.BypassFreeLimitation = false; };
        void AddPreset(string preset)
        {
            state.Commit();
            var deepSeek = preset == "deepseek";
            var draft = new ProviderDraft { Name = NextName(deepSeek ? "DeepSeek" : T("OpenAI compatible")), BaseUrl = deepSeek ? "https://api.deepseek.com" : "https://api.openai.com/v1", Model = deepSeek ? "deepseek-chat" : "gpt-4.1-mini", ContextLimit = ModelContexts.DefaultContextLimit, SupportsImages = true };
            drafts.Add(draft); Refresh(draft); name.Focus(FocusState.Programmatic); name.SelectAll();
        }
        addOpenAi.Click += (_, _) => AddPreset("openai");
        addLocal.Click += (_, _) => { state.Commit(); var draft = new ProviderDraft { Name = NextName("Local"), Kind = "local", BaseUrl = "http://127.0.0.1", Model = "", ContextLimit = 8192, SupportsImages = false }; drafts.Add(draft); Refresh(draft); };
        addComposite.Click+=(_,_)=>{state.Commit();var draft=new ProviderDraft{Name=NextName("Modèle composé"),Kind="composite",CompositeJson=new CompositeModel().Json()};drafts.Add(draft);Refresh(draft);};
        addDeepSeek.Click += (_, _) => AddPreset("deepseek");
        foreach (var cloud in cloudItems)
            cloud.Item.Click += (_, _) =>
            {
                state.Commit();
                var preset = cloud.Preset;
                var draft = new ProviderDraft { Name = NextName(preset.Name), Kind = preset.Kind, BaseUrl = preset.BaseUrl, Model = preset.DefaultModel, ContextLimit = ModelContexts.DefaultContextLimit, SupportsImages = true };
                drafts.Add(draft); Refresh(draft); if (preset.Kind == "vertex") vertexFields.FocusProject(); else key.Focus(FocusState.Programmatic);
            };
        foreach (var (preset, item) in acpItems) item.Click += (_, _) => { state.Commit(); var target = AcpProviders.Create(preset); var draft = new ProviderDraft { Name = NextName(target.Name), Kind = target.Kind, BaseUrl = "", Model = target.Model, SupportsImages = false, OpenCodeTools = true, DetectedModelsJson = target.DetectedModelsJson, SelectedModelsJson = target.SelectedModelsJson }; drafts.Add(draft); Refresh(draft); };
        addOpenCode.Click += (_, _) =>
        {
            state.Commit();
            var defaultDesktop = ResolveOpenCodeExecutable(null);
            var hasDesktop = File.Exists(defaultDesktop);
            var draft = new ProviderDraft
            {
                Name = NextName("OpenCode"),
                Kind = "opencode",
                BaseUrl = "http://127.0.0.1:4096",
                Username = "opencode",
                Model = "opencode/big-pickle",
                ExecutablePath = hasDesktop ? defaultDesktop : "",
                AutoStart = hasDesktop,
                BypassFreeLimitation = true,
                ContextLimit = 200000,
                SupportsImages = true
            };
            drafts.Add(draft); Refresh(draft); name.Focus(FocusState.Programmatic); name.SelectAll();
        };
        duplicate.Click += (_, _) =>
        {
            if (state.Selected == null) return;
            state.Commit();
            var source = state.Selected;
            var draft = new ProviderDraft { Name = NextName(source.Name + " " + T("copie")), BaseUrl = source.BaseUrl, Model = source.Model, ContextLimit = source.ContextLimit, ModelContextsJson = source.ModelContextsJson, SupportsImages = source.SupportsImages,
                DetectedModelsJson=source.DetectedModelsJson, SelectedModelsJson=source.SelectedModelsJson, Kind = source.Kind, CompositeJson=source.CompositeJson, LocalModelsJson=source.LocalModelsJson, Username = source.Username, ExecutablePath = source.ExecutablePath, AcpArgumentsJson = source.AcpArgumentsJson, AutoStart = source.AutoStart, OpenCodeTools = source.OpenCodeTools, BypassFreeLimitation = source.BypassFreeLimitation };
            drafts.Add(draft); Refresh(draft); name.Focus(FocusState.Programmatic); name.SelectAll();
        };
        remove.Click += async (_, _) =>
        {
            if (state.Selected is not { } selected) return;
            if (Managed(selected)) { managedNotice.IsOpen = true; managedNotice.Visibility = Visibility.Visible; return; }
            if (selected.LocalBusy) { ShowConnectionResult(selected, WorkflowText("Annulez l’opération locale avant de retirer ce fournisseur.", "Cancel the local operation before removing this provider."), InfoBarSeverity.Warning); return; }
            if (selected.Kind == "local")
            {
                try { await LocalModelRuntime.UnloadAsync(LocalProviderSettings.Read(selected.LocalModelsJson), CancellationToken.None, onlyIdle: true); }
                catch (Exception ex) { ShowConnectionResult(selected, ex.Message, InfoBarSeverity.Error); return; }
            }
            var index = drafts.IndexOf(selected); drafts.Remove(selected);
            state.Selected = null; state.NotifyRemoved(selected);
            Refresh(drafts.Count == 0 ? null : drafts[Math.Clamp(index, 0, drafts.Count - 1)]);
        };
        testConnection.Click += async (_, _) =>
        {
            if (connectionBusy || state.Selected is not { } draft) return;
            connectionBusy = true;
            testConnection.IsEnabled = importModels.IsEnabled = connectAccount.IsEnabled = false;
            editorBusy.IsActive = modelsBusy.IsActive = true; editorBusy.Visibility = modelsBusy.Visibility = Visibility.Visible;
            ShowConnectionResult(draft, T("Test de connexion en cours…"), InfoBarSeverity.Informational);
            var secret = "";
            try
            {
                state.Commit();
                var revision = draft.ConnectionRevision;
                var target = AsProvider(draft);
                List<string>? list = null;
                if (target.IsVertex)
                {
                    secret = draft.PendingKey.Length > 0 ? draft.PendingKey : draft.DeleteKey ? "" : KeyVault.Decrypt(draft.ProtectedKey);
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    await VertexAi.ProbeAsync(http, target, secret, timeout.Token);
                }
                else list = await Discover(draft, selectAll: true);
                if (!drafts.Contains(draft)) return;
                if (revision != draft.ConnectionRevision || !target.IsVertex && list == null)
                {
                    ShowConnectionResult(draft, WorkflowText("Configuration modifiée pendant le test : recommencez.", "Configuration changed during the check: try again."), InfoBarSeverity.Warning);
                    return;
                }
                if (list is { Count: 0 })
                {
                    ShowConnectionResult(draft, WorkflowText("Connexion établie, mais aucun modèle détecté. Les choix existants sont conservés.", "Connected, but no models were found. Existing selections were kept."), InfoBarSeverity.Warning);
                    return;
                }
                if (string.IsNullOrWhiteSpace(draft.Name)) throw new ArgumentException(T("Le nom du fournisseur est requis."));
                var saved = await SaveTestedProviderAsync(draft);
                if (state.Selected == draft) Select(draft);
                provider = saved; this.state.ProviderId = saved.Id;
                var wasLoading = loading;
                try
                {
                    loading = true;
                    providers.ItemsSource = db.Providers.Local.Where(x => db.Entry(x).State != EntityState.Deleted).ToList();
                    providers.SelectedItem = saved;
                }
                finally { loading = wasLoading; }
                PopulateModelSelector();
                await db.SaveChangesAsync();
                BuildSelectedModels(); state.Changed();
                ShowConnectionResult(draft, target.IsVertex
                    ? WorkflowText($"Connexion Vertex AI réussie · {target.Model}. Le comptage de jetons est accessible ; la génération et les crédits ne sont pas vérifiés par ce contrôle.",
                        $"Vertex AI connected · {target.Model}. Token counting is accessible; generation and credit eligibility are not verified by this check.")
                    : WorkflowText($"Connexion réussie · {list!.Count} modèles sélectionnés et enregistrés.", $"Connected · {list!.Count} models selected and saved."),
                    InfoBarSeverity.Success);
            }
            catch (OperationCanceledException)
            {
                ShowConnectionResult(draft, WorkflowText("Le test de connexion a été annulé ou le délai de réponse est dépassé.", "Connection check cancelled or timed out."), InfoBarSeverity.Warning);
            }
            catch (Exception ex)
            {
                var detail = string.IsNullOrWhiteSpace(secret) ? ex.Message : ex.Message.Replace(secret.Trim(), "[secret]", StringComparison.Ordinal);
                ShowConnectionResult(draft, WorkflowText("Échec de la connexion : ", "Connection failed: ") + detail, InfoBarSeverity.Error);
            }
            finally
            {
                connectionBusy = false;
                testConnection.IsEnabled = importModels.IsEnabled = connectAccount.IsEnabled = CanEdit(state.Selected);
                editorBusy.IsActive = modelsBusy.IsActive = false; editorBusy.Visibility = modelsBusy.Visibility = Visibility.Collapsed;
            }
        };
        connectAccount.Click += async (_, _) =>
        {
            if (connectionBusy || state.Selected is not { } draft) return;
            connectionBusy = true;
            connectAccount.IsEnabled = testConnection.IsEnabled = importModels.IsEnabled = false;
            editorBusy.IsActive = modelsBusy.IsActive = true; editorBusy.Visibility = modelsBusy.Visibility = Visibility.Visible;
            info.Text = WorkflowText("Connexion OpenAI : terminez l’authentification dans le navigateur…", "OpenAI sign-in: complete authentication in your browser…");
            try { await Discover(draft, selectAll: true, authenticate: true); await SaveTestedProviderAsync(draft); info.Text = WorkflowText("Compte connecté · modèles enregistrés.", "Account connected · models saved."); }
            catch (Exception ex) { info.Text = ex.Message; }
            finally { connectionBusy = false; connectAccount.IsEnabled = testConnection.IsEnabled = importModels.IsEnabled = CanEdit(state.Selected); editorBusy.IsActive = modelsBusy.IsActive = false; editorBusy.Visibility = modelsBusy.Visibility = Visibility.Collapsed; }
        };
        async Task<List<string>?> Discover(ProviderDraft draft, bool selectAll = false, bool authenticate = false)
        {
            state.Commit();
            using var timeout = new CancellationTokenSource(authenticate ? TimeSpan.FromMinutes(5) : AcpProviders.Find(draft.Kind) != null ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(25));
            var target = AsProvider(draft);
            var endpoint = target.BaseUrl;
            var secret = draft.PendingKey.Length > 0 ? draft.PendingKey : draft.DeleteKey ? "" : KeyVault.Decrypt(draft.ProtectedKey);
            List<string> list;
            if (target.IsAcp) list = await new AcpEngine().ModelsAsync(target, timeout.Token, authenticate);
            else if (target.IsOpenCode)
            {
                await EnsureOpenCodeServerAsync(target, secret, timeout.Token);
                var directory = project?.GetSourceFolders().FirstOrDefault();
                list = await Task.Run(async () => (await openCodeEngine.ModelsAsync(target, secret, directory, timeout.Token))
                    .Select(x => x.Reference).ToList(), timeout.Token);
            }
            else list = await Task.Run(() => engine.ModelsAsync(target, secret, timeout.Token), timeout.Token);
            if (draft.BaseUrl != endpoint || !drafts.Contains(draft)) return null;
            if (selectAll && list.Count == 0) return list;
            await Task.Run(() =>
            {
                ProviderModels.Refresh(target, list);
                if (selectAll && list.Count > 0) ProviderModels.Select(target, list);
            }, timeout.Token);
            if (list.Count > 0 && (string.IsNullOrWhiteSpace(draft.Model) || selectAll && !list.Contains(draft.Model, StringComparer.Ordinal)))
            {
                draft.Model = list[0];
                ProviderModels.Select(target, selectAll ? list : [draft.Model]);
            }
            draft.SupportsImages = target.SupportsImages;
            if (state.Selected == draft) vision.IsChecked = target.SupportsImages;
            draft.DetectedModelsJson = target.DetectedModelsJson;
            draft.SelectedModelsJson = target.SelectedModelsJson;
            ModelContexts.Select(target, draft.Model);
            draft.LocalModelsJson = target.LocalModelsJson;
            draft.ModelContextsJson = target.ModelContextsJson; draft.ContextLimit = target.ContextLimit;
            if (state.Selected == draft)
            {
                var wasRefreshing = refreshing;
                try { refreshing = true; model.ItemsSource = list; model.SelectedItem = draft.Model; model.Text = draft.Model; RefreshLimit(draft); }
                finally { refreshing = wasRefreshing; }
            }
            ShowConnectionResult(draft, WorkflowText($"{list.Count} modèles détectés.", $"{list.Count} models detected."), InfoBarSeverity.Success);
            if (state.Selected == draft) BuildSelectedModels();
            state.Changed(); return list;
        }
        importModels.Click += async (_, _) =>
        {
            if (connectionBusy || state.Selected is not { } draft) return;
            connectionBusy = true;
            importModels.IsEnabled = testConnection.IsEnabled = connectAccount.IsEnabled = false;
            editorBusy.IsActive = modelsBusy.IsActive = true; editorBusy.Visibility = modelsBusy.Visibility = Visibility.Visible;
            ShowConnectionResult(draft, WorkflowText("Recherche des modèles…", "Finding models…"), InfoBarSeverity.Informational);
            try { await Discover(draft); }
            catch (OperationCanceledException) { ShowConnectionResult(draft, WorkflowText("Recherche annulée ou délai de réponse dépassé.", "Model lookup cancelled or timed out."), InfoBarSeverity.Warning); }
            catch (Exception ex) { ShowConnectionResult(draft, ex.Message, draft.Kind == "vertex" && VertexAi.TryRead(draft.BaseUrl, out var config) && config!.Authentication == VertexAi.Express ? InfoBarSeverity.Warning : InfoBarSeverity.Error); }
            finally { connectionBusy = false; importModels.IsEnabled = testConnection.IsEnabled = connectAccount.IsEnabled = CanEdit(state.Selected); editorBusy.IsActive = modelsBusy.IsActive = false; editorBusy.Visibility = modelsBusy.Visibility = Visibility.Collapsed; }
        };

        void ShowProviderHelp(ProviderDraft? draft)
        {
            providerHelp.Children.Clear();
            var acpPreset = AcpProviders.Find(draft?.Kind);
            if (acpPreset != null) { providerHelp.Visibility = Visibility.Visible; providerHelp.Children.Add(Label(WorkflowText(acpPreset.DescriptionFr, acpPreset.DescriptionEn), 12)); providerHelp.Children.Add(new HyperlinkButton { Content = WorkflowText("Installation et connexion", "Installation and sign-in"), NavigateUri = new Uri(acpPreset.Documentation) }); return; }
            var preset = draft?.Kind == "vertex" ? ProviderPresets.Find("vertex") : draft == null || draft.Kind is "local" or "opencode" or "composite" ? null : ProviderPresets.Resolve(draft.BaseUrl);
            providerHelp.Visibility = preset == null ? Visibility.Collapsed : Visibility.Visible;
            if (preset == null) return;
            providerHelp.Children.Add(Label(WorkflowText(preset.DescriptionFr, preset.DescriptionEn), 12));
            providerHelp.Children.Add(Row(
                new HyperlinkButton { Content = preset.Id == "vertex" ? WorkflowText("Accès Google Cloud", "Google Cloud access") : WorkflowText("Obtenir une clé API", "Get an API key"), NavigateUri = new Uri(preset.Id == "vertex" && VertexAi.UsesApiKey(draft?.BaseUrl) ? VertexAi.ApiKeyHelp : preset.KeysUrl) },
                new HyperlinkButton { Content = WorkflowText("Documentation", "Documentation"), NavigateUri = new Uri(preset.DocumentationUrl) }));
            if (preset.Id == "vertex")
            {
                providerHelp.Children.Add(Label(WorkflowText("Le catalogue Google ne garantit pas l’accès à chaque modèle. L’API doit être activée, les droits IAM accordés et les crédits éligibles sur ce projet. Vous pouvez aussi saisir google/gemini-4-argon manuellement.", "The Google catalogue does not guarantee access to every model. Enable the API, grant IAM access and check eligible credits for this project. You can also enter google/gemini-4-argon manually."), 12));
                return;
            }
            providerHelp.Children.Add(Label(WorkflowText("Renseignez votre clé, puis importez les modèles accessibles à votre compte ou saisissez leur identifiant exact. Ajustez le contexte et la case Images au modèle choisi.",
                "Enter your key, then import models accessible to your account or enter their exact identifier. Adjust context and the Images checkbox for the selected model."), 12));
            if (preset.SuggestedChatModels.Count > 0)
                providerHelp.Children.Add(Label(WorkflowText("Exemples d’identifiants · accès non vérifié : ", "Example identifiers · access not verified: ") + string.Join(", ", preset.SuggestedChatModels), 12));
            if (preset.Id == "zai") providerHelp.Children.Add(Label(WorkflowText("Le catalogue automatique peut être indisponible. Le choix manuel reste possible. GLM utilise un raisonnement activé/désactivé ; l’intensité est gérée par le modèle.",
                "Automatic discovery may be unavailable. You can still select a model manually. GLM uses enabled/disabled thinking; effort is managed by the model."), 12));
        }

        string ProviderDescription(ProviderDraft? draft) => draft == null ? "" : (Managed(draft) ? WorkflowText("Serveur Monolith · ", "Monolith server · ") : "") + draft.Kind switch
        {
            "local" => WorkflowText("Local · cet ordinateur", "Local · this computer"),
            "composite" => WorkflowText("Modèle composé", "Composite model"),
            "vertex" => "Vertex AI · Google Cloud",
            "opencode" => "OpenCode",
            _ => AcpProviders.Find(draft.Kind)?.Name ?? ProviderPresets.Resolve(draft.BaseUrl)?.Name ??
                (Uri.TryCreate(draft.BaseUrl, UriKind.Absolute, out var endpoint) ? endpoint.Host : WorkflowText("API compatible", "Compatible API"))
        };
        void RenderCards()
        {
            var query = view.Search.Text.Trim();
            var visible = drafts.Where(d => query.Length == 0 || (d.Name + " " + ProviderDescription(d)).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            if (visible.Count > 0 && listRows.Keys.SequenceEqual(visible))
            {
                foreach (var (draft, row) in listRows)
                {
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(row.Button, draft == state.Selected ? WorkflowText("Sélectionnée", "Selected") : ""); row.Name.Text = draft.Name;
                    row.Button.Resources["ButtonBackgroundPointerOver"] = FluentDesign.Resource(draft == state.Selected ? "ProviderConnectionSelectionHoverBrush" : "ControlHoverBrush");
                    row.Surface.BorderBrush = FluentDesign.Resource(draft == state.Selected ? "AccentFillColorDefaultBrush" : "TransparentBrush");
                    row.Surface.Background = FluentDesign.Resource(draft == state.Selected ? "ProviderConnectionSelectionBrush" : "TransparentBrush");
                }
                return;
            }
            cards.Children.Clear(); listRows.Clear();
            foreach (var draft in visible)
            {
                var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                var label = Label(draft.Name, 14); label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; text.Children.Add(label);
                var description = Label(ProviderDescription(draft), 12); description.Foreground = FluentDesign.Secondary; text.Children.Add(description);
                var content = new Grid { ColumnSpacing = 10 };
                content.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                content.Children.Add(ProviderConnectionIcon(draft, 26)); Grid.SetColumn(text, 1); content.Children.Add(text);
                var choose = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new(10, 12, 4, 12), Background = FluentDesign.Resource("TransparentBrush"), BorderThickness = new(0), MinWidth = 0 };
                choose.Resources["ButtonBackgroundPointerOver"] = FluentDesign.Resource(draft == state.Selected ? "ProviderConnectionSelectionHoverBrush" : "ControlHoverBrush");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(choose, draft == state.Selected ? WorkflowText("Sélectionnée", "Selected") : "");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choose, draft.Name + " · " + ProviderDescription(draft));
                var more = new Button { MinWidth = 28, Padding = new(4), Background = FluentDesign.Resource("TransparentBrush"), BorderThickness = new(0), VerticalAlignment = VerticalAlignment.Center };
                FluentDesign.IconButton(more, "\uE712", WorkflowText("Actions de ", "Actions for ") + draft.Name, false);
                var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                row.Children.Add(choose); Grid.SetColumn(more, 1); row.Children.Add(more);
                var surface = new Border { Child = row, BorderThickness = new(1), CornerRadius = new(6),
                    BorderBrush = FluentDesign.Resource(draft == state.Selected ? "AccentFillColorDefaultBrush" : "TransparentBrush"),
                    Background = FluentDesign.Resource(draft == state.Selected ? "ProviderConnectionSelectionBrush" : "TransparentBrush") };
                choose.Click += (_, _) => { state.Commit(); Refresh(draft); };
                more.Click += (_, _) => { state.Commit(); Refresh(draft); actionMenu.ShowAt(view.More); };
                listRows[draft] = (choose, label, surface); cards.Children.Add(surface);
            }
            if (cards.Children.Count == 0) cards.Children.Add(Label(drafts.Count == 0
                ? WorkflowText("Ajoutez une connexion pour commencer.", "Add a connection to get started.")
                : WorkflowText("Aucune connexion trouvée.", "No connections found."), 13));
        }
        void BuildSelectedModels()
        {
            if (view.SelectedPage != 1 || state.Selected is not { } draft) return;
            modelsHost.Children.Clear();
            if (draft.Kind != "composite" && !AsProvider(draft).IsExternalAgent)
                modelsHost.Children.Add(BuildProviderReasoning(draft, () => AsProvider(draft), () => state.Changed(), CanEdit(draft)));
            if (draft.Kind == "composite")
                modelsHost.Children.Add(Label(WorkflowText("Les modèles sont définis par les agents du modèle composé, dans Connexion.", "Models are defined by the composite agents in Connection."), 13));
            else if (Managed(draft))
                modelsHost.Children.Add(Label(WorkflowText("Les modèles de cette connexion sont fournis par le serveur.", "This connection's models are provided by the server."), 13));
            else modelsHost.Children.Add(BuildProviderModelChoices(draft, () => AsProvider(draft), () =>
            {
                refreshing = true; model.SelectedItem = draft.Model; model.Text = draft.Model; RefreshLimit(draft); refreshing = false;
                InvalidateConnectionResult(); state.Changed();
            }));
        }
        var presets = new MenuFlyout(); view.AddConnection.Flyout = presets;
        foreach (var item in new[] { addOpenAi, addDeepSeek }.Concat(cloudItems.Select(x => x.Item)).Concat(acpItems.Select(x => x.Item)).Concat([addOpenCode, addLocal, addComposite])) presets.Items.Add(item);
        var connectionAdvanced = new Button { Name = "OpenProviderAdvanced", HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(12) };
        FluentDesign.IconButton(connectionAdvanced, "\uE76C", WorkflowText("Paramètres avancés", "Advanced settings"));
        connectionAdvanced.Click += (_, _) => view.ShowPage(2);
        foreach (var item in new UIElement[] { managedNotice, name, connectAccount, vertexFields, url, username, keyRow, keyNote, Row(testConnection, editorBusy), connectionResult, compositePanel, connectionAdvanced }) editor.Children.Add(item);
        foreach (var item in new UIElement[] { model, Row(importModels, modelsBusy), modelsResult, localPanel, modelsHost }) view.Models.Children.Add(item);
        foreach (var item in new UIElement[] { info, providerHelp, limit, vision, autoStart, openCodeTools, bypassFreeLimitation, connectionLaunch, deleteKey }) view.Advanced.Children.Add(item);
        view.PageChanged += _ => { state.Commit(); BuildSelectedModels(); };
        var searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); RenderCards(); };
        view.Search.TextChanged += (_, _) => { searchDelay.Stop(); searchDelay.Start(); };
        view.Unloaded += (_, _) => searchDelay.Stop();
        void Changed() { if (refreshing) return; state.Changed(); }
        foreach (var box in new[] { name, url, username, executable, acpArguments }) box.TextChanged += (_, _) => Changed();
        model.SelectionChanged += (_, _) => Changed(); model.TextSubmitted += (_, _) => Changed(); model.LostFocus += (_, _) => Changed();
        foreach (var check in new[] { vision, deleteKey, autoStart, openCodeTools, bypassFreeLimitation })
        { check.Checked += (_, _) => { RefreshKeyState(); Changed(); }; check.Unchecked += (_, _) => { RefreshKeyState(); Changed(); }; }
        vertexFields.EndpointChanged += _ => Changed();
        state.Panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); state.Panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        state.Panel.Children.Add(view); Grid.SetRow(state.Error, 1); state.Panel.Children.Add(state.Error);
        Refresh(drafts.FirstOrDefault(d => d.Id == selectedProviderId) ?? drafts.FirstOrDefault());
        return state;
    }

    string? ValidateProviderDrafts(ProviderEditorState editor)
    {
        editor.Commit();
        if (editor.Drafts.Any(x => x.LocalBusy)) return WorkflowText("Une opération locale est en cours. Attendez sa fin ou annulez-la avant d’enregistrer.", "A local operation is running. Wait for it or cancel before saving.");
        if(db.PendingInputs.AsNoTracking().Select(x=>x.ProviderId).ToList().Any(id=>!editor.Drafts.Any(draft=>draft.Id==id)))return "Un fournisseur est utilisé par un message en attente. / Provider used by a queued message.";
        if (editor.Drafts.Any(x => string.IsNullOrWhiteSpace(x.Name))) return T("Le nom du fournisseur est requis.");
        foreach (var draft in editor.Drafts)
        {
            if (draft.Kind == "local")
            {
                var local = LocalProviderSettings.Read(draft.LocalModelsJson);
                if (local.Models.Count == 0) return WorkflowText("Importez au moins un modèle dans ", "Import at least one model in ") + draft.Name;
                if (draft.ContextLimit is < 1024 or > 262144) return WorkflowText("Le contexte local doit être compris entre 1 024 et 262 144 tokens.", "Local context must be between 1,024 and 262,144 tokens.");
                continue;
            }
            if(draft.Kind=="composite")
            {
                try{CompositeModel.Read(draft.CompositeJson).Validate(editor.Drafts.Where(x=>x.Id>0).Select(x=>new Provider{Id=x.Id,Kind=x.Kind}));}
                catch(Exception ex){return draft.Name+" : "+ex.Message;}continue;
            }
            if (AcpProviders.Find(draft.Kind) != null) { try { AcpProviders.Validate(new Provider { Kind = draft.Kind, ExecutablePath = draft.ExecutablePath, AcpArgumentsJson = draft.AcpArgumentsJson }); } catch (Exception ex) { return draft.Name + " : " + ex.Message; } if (string.IsNullOrWhiteSpace(draft.Model)) return T("Modèle requis."); continue; }
            if (draft.Kind == "vertex") { try { VertexAi.Read(draft.BaseUrl); if (VertexAi.UsesApiKey(draft.BaseUrl) && (draft.DeleteKey || draft.ProtectedKey.Length == 0) && string.IsNullOrWhiteSpace(draft.PendingKey)) VertexAi.RequireApiKey(""); } catch (ArgumentException ex) { return draft.Name + " : " + ex.Message; } }
            try { ChatEngine.Endpoint(draft.BaseUrl.Trim(), draft.Kind == "opencode" ? "global/health" : "chat/completions"); }
            catch { return T("URL HTTP(S) invalide.") + " " + draft.Name; }
            if (string.IsNullOrWhiteSpace(draft.Model) || draft.ContextLimit < 1024) return T("Modèle et limite de contexte requis.") + " " + draft.Name;
        }
        return null;
    }

    void ApplyProviderDraft(ProviderDraft draft, Provider entity)
    {
        entity.Name = draft.Name.Trim(); entity.BaseUrl = draft.BaseUrl.Trim().TrimEnd('/'); entity.Model = draft.Kind == "vertex" ? VertexAi.NormalizeModel(draft.Model) : draft.Model.Trim();
        entity.SupportsImages = draft.SupportsImages;
        entity.Kind = draft.Kind; entity.Username = draft.Username.Trim(); entity.ExecutablePath = draft.ExecutablePath.Trim(); entity.AutoStart = draft.AutoStart;
        entity.AcpArgumentsJson = draft.AcpArgumentsJson;
        if (entity.IsAcp) { entity.ProtectedKey = []; entity.BaseUrl = ""; }
        if (entity.IsVertex && !VertexAi.UsesApiKey(entity.BaseUrl)) entity.ProtectedKey = [];
        entity.OpenCodeTools = draft.OpenCodeTools;
        entity.BypassFreeLimitation = draft.BypassFreeLimitation;
        entity.CompositeJson = draft.CompositeJson;
        entity.LocalModelsJson = draft.LocalModelsJson;
        entity.ModelContextsJson = draft.ModelContextsJson; ModelContexts.MergeLocal(entity); ModelContexts.Sync(entity);
        entity.DetectedModelsJson = draft.DetectedModelsJson; entity.SelectedModelsJson = draft.SelectedModelsJson;
        if (entity.IsComposite)
        {
            var config = CompositeModel.Read(entity.CompositeJson); var target = CompositeModel.Resolve(config.Orchestrator, db.Providers.Local);
            entity.Model = target.Model; entity.ContextLimit = target.ContextLimit; entity.SupportsImages = target.SupportsImages; entity.BaseUrl = ""; entity.ProtectedKey = [];
        }
        if (!entity.IsAcp && (!entity.IsVertex || VertexAi.UsesApiKey(entity.BaseUrl)) && !string.IsNullOrWhiteSpace(draft.PendingKey)) entity.ProtectedKey = KeyVault.Encrypt(draft.PendingKey.Trim());
        else if (draft.DeleteKey) entity.ProtectedKey = [];
    }

    async Task<Provider> SaveTestedProviderAsync(ProviderDraft draft)
    {
        var entity = draft.Id == 0 ? null : db.Providers.Local.FirstOrDefault(x => x.Id == draft.Id);
        if (entity == null) { entity = new Provider(); db.Providers.Add(entity); }
        ApplyProviderDraft(draft, entity);
        await db.SaveChangesAsync();
        draft.Id = entity.Id;
        draft.ProtectedKey = [.. entity.ProtectedKey];
        draft.PendingKey = "";
        draft.DeleteKey = false;
        draft.EditingKey = false;
        return entity;
    }

    async Task<Provider?> SaveProviderDraftsAsync(ProviderEditorState editor)
    {
        var entities = new Dictionary<ProviderDraft, Provider>();
        // Providers shared by the server are managed in Settings > Server: the editor neither deletes them nor writes a stale token back.
        var managed = ServerManagedProviders();
        foreach (var existing in db.Providers.Local.ToList())
            if (!managed.Contains(existing.Id) && !editor.Drafts.Any(x => x.Id == existing.Id)) db.Providers.Remove(existing);
        foreach (var draft in editor.Drafts)
        {
            if (draft.Id != 0 && managed.Contains(draft.Id))
            {
                if (db.Providers.Local.FirstOrDefault(x => x.Id == draft.Id) is { } shared) entities[draft] = shared;
                continue;
            }
            var entity = draft.Id == 0 ? null : db.Providers.Local.FirstOrDefault(x => x.Id == draft.Id);
            if (entity == null) { entity = new Provider(); db.Providers.Add(entity); }
            ApplyProviderDraft(draft, entity);
            entities[draft] = entity;
        }
        await db.SaveChangesAsync();
        foreach (var entry in entities) entry.Key.Id = entry.Value.Id;
        if (editor.Selected != null && entities.TryGetValue(editor.Selected, out var selected) && ProviderModels.CanChat(selected)) return selected;
        return entities.Values.FirstOrDefault(x => x.Id == state.ProviderId && ProviderModels.CanChat(x)) ?? entities.Values.FirstOrDefault(ProviderModels.CanChat);
    }
}
