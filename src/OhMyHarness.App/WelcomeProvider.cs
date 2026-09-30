using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;
using static OhMyHarness.App.UiText;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    sealed class WelcomeProviderForm
    {
        public required ContentControl Panel { get; init; }
        public required Func<ProviderDraft> Read { get; init; }
        public required Func<string?> Validate { get; init; }
        public Action<bool> BusyChanged { get; set; } = _ => { };
        public CancellationTokenSource? Request { get; set; }
        public bool Busy => Request != null;
        public void Cancel() => Request?.Cancel();
    }

    WelcomeProviderForm BuildWelcomeProvider()
    {
        var drafts = db.Providers.Local.Where(x => db.Entry(x).State != EntityState.Deleted && !x.IsComposite)
            .OrderBy(x => x.Id).Select(x => new ProviderDraft { Id = x.Id, Name = x.Name, BaseUrl = x.BaseUrl, Model = x.Model,
                ProtectedKey = [.. x.ProtectedKey], Kind = x.Kind, ContextLimit = x.ContextLimit, SupportsImages = x.SupportsImages,
                DetectedModelsJson = x.DetectedModelsJson, SelectedModelsJson = x.SelectedModelsJson, Username = x.Username,
                ExecutablePath = x.ExecutablePath, AutoStart = x.AutoStart, OpenCodeTools = x.OpenCodeTools,
                BypassFreeLimitation = x.BypassFreeLimitation }).ToList();
        drafts.Add(new ProviderDraft { Name = WorkflowText("Autre API compatible", "Other compatible API"), Kind = "openai", SupportsImages = false });
        drafts.Add(new ProviderDraft { Name = "OpenCode", Kind = "opencode", BaseUrl = "http://127.0.0.1:4096", Username = "opencode", OpenCodeTools = true });
        var selected = drafts.FirstOrDefault(x => x.Id == state.ProviderId) ?? drafts[0];
        var panel = new StackPanel { Spacing = 12 };
        var host = new ContentControl { Content = panel, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var choice = new ComboBox { Header = WorkflowText("Choisir un fournisseur", "Choose a provider"), ItemsSource = drafts, HorizontalAlignment = HorizontalAlignment.Stretch };
        var name = new TextBox { Header = T("Nom du fournisseur"), MaxLength = 100 };
        var endpoint = new TextBox { Header = T("URL de base de l’API"), MaxLength = 2000, PlaceholderText = "https://…/v1" };
        var key = new PasswordBox { Header = T("Clé API (vide : conserver la clé enregistrée)") };
        var model = new ComboBox { Header = T("Identifiant du modèle"), IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary, FontSize = 12 };
        var load = new Button { Content = WorkflowText("Charger les modèles", "Load models") };
        var progress = new ProgressRing { Width = 20, Height = 20, IsActive = false, Visibility = Visibility.Collapsed };
        var username = new TextBox { Header = T("Utilisateur OpenCode"), MaxLength = 100 };
        var executable = new TextBox { Header = T("Chemin vers opencode.exe (facultatif)") };
        var autoStart = new CheckBox { Content = WorkflowText("Démarrer OpenCode automatiquement", "Start OpenCode automatically") };
        var limit = new NumberBox { Header = T("Fenêtre de contexte du modèle (tokens)"), Minimum = 1024, Maximum = 10_000_000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var vision = new CheckBox { Content = T("Ce modèle accepte les images") };
        var advanced = new StackPanel { Spacing = 10 };
        foreach (var control in new UIElement[] { limit, vision, username, executable, autoStart }) advanced.Children.Add(control);
        bool refreshing = false;
        ProviderDraft Read()
        {
            selected.Name = name.Text; selected.BaseUrl = endpoint.Text; selected.Model = model.Text;
            selected.PendingKey = key.Password; selected.Username = username.Text; selected.ExecutablePath = executable.Text;
            selected.AutoStart = autoStart.IsChecked == true; selected.SupportsImages = vision.IsChecked == true;
            if (double.IsFinite(limit.Value)) selected.ContextLimit = (int)limit.Value;
            return selected;
        }
        string? Validate()
        {
            var draft = Read();
            if (string.IsNullOrWhiteSpace(draft.Name)) return T("Le nom du fournisseur est requis.");
            try { ChatEngine.Endpoint(draft.BaseUrl.Trim(), draft.Kind == "opencode" ? "global/health" : "models"); }
            catch { return T("URL HTTP(S) invalide."); }
            if (string.IsNullOrWhiteSpace(draft.Model) || !double.IsFinite(limit.Value) || limit.Value is < 1024 or > 10_000_000)
                return T("Modèle et limite de contexte requis.");
            return null;
        }
        void Select(ProviderDraft draft)
        {
            refreshing = true; selected = draft;
            name.Text = draft.Name; endpoint.Text = draft.BaseUrl; model.ItemsSource = ProviderModels.Parse(draft.DetectedModelsJson); model.Text = draft.Model;
            key.Password = draft.PendingKey; key.PlaceholderText = draft.ProtectedKey.Length > 0 ? T("Clé déjà enregistrée") : T("Votre clé API");
            key.Header = T(draft.Kind == "opencode" ? "Mot de passe du serveur (vide : aucun)" : "Clé API (vide : conserver la clé enregistrée)");
            limit.Value = draft.ContextLimit; vision.IsChecked = draft.SupportsImages;
            username.Text = draft.Username; executable.Text = draft.ExecutablePath; autoStart.IsChecked = draft.AutoStart;
            foreach (var control in new UIElement[] { username, executable, autoStart }) control.Visibility = draft.Kind == "opencode" ? Visibility.Visible : Visibility.Collapsed;
            info.Text = WorkflowText("Saisissez vos informations, puis chargez les modèles ou indiquez directement leur identifiant. Une API locale peut fonctionner sans clé.",
                "Enter your details, then load models or type a model ID directly. A local API may work without a key.");
            refreshing = false;
        }
        choice.SelectionChanged += (_, _) => { if (refreshing || choice.SelectedItem is not ProviderDraft draft) return; Read(); Select(draft); };
        model.SelectionChanged += (_, _) =>
        {
            if (refreshing || model.SelectedItem is not string id) return;
            model.Text = id;
            if (ModelCatalog.GetDefaultContextLimit(id) is int context) limit.Value = context;
        };
        foreach (var control in new UIElement[] { choice, name, endpoint, key, model, Row(load, progress), info,
            new Expander { Header = WorkflowText("Options du modèle et du serveur", "Model and server options"), Content = advanced,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch } }) panel.Children.Add(control);
        var form = new WelcomeProviderForm { Panel = host, Read = Read, Validate = Validate };
        load.Click += async (_, _) =>
        {
            if (form.Busy) return;
            var draft = Read();
            try { ChatEngine.Endpoint(draft.BaseUrl, draft.Kind == "opencode" ? "global/health" : "models"); }
            catch { info.Text = T("URL HTTP(S) invalide."); return; }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            form.Request = timeout; host.IsEnabled = false; progress.IsActive = true; progress.Visibility = Visibility.Visible; form.BusyChanged(true);
            info.Text = WorkflowText("Recherche des modèles…", "Finding models…");
            try
            {
                var secret = draft.PendingKey.Length > 0 ? draft.PendingKey : KeyVault.Decrypt(draft.ProtectedKey);
                var target = new Provider { Name = draft.Name, BaseUrl = draft.BaseUrl, Model = draft.Model, Kind = draft.Kind,
                    Username = draft.Username, ExecutablePath = draft.ExecutablePath, AutoStart = draft.AutoStart,
                    BypassFreeLimitation = draft.BypassFreeLimitation, DetectedModelsJson = draft.DetectedModelsJson, SelectedModelsJson = draft.SelectedModelsJson };
                List<string> models;
                if (target.IsOpenCode)
                {
                    await EnsureOpenCodeServerAsync(target, secret, timeout.Token);
                    models = (await openCodeEngine.ModelsAsync(target, secret, project?.GetSourceFolders().FirstOrDefault(), timeout.Token)).Select(x => x.Reference).ToList();
                }
                else models = await engine.ModelsAsync(target, secret, timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
                models = ProviderModels.Normalize(models);
                if (models.Count == 0) { info.Text = WorkflowText("Connexion établie, aucun modèle trouvé. Vous pouvez saisir un identifiant manuellement.", "Connected, no models found. You can enter a model ID manually."); return; }
                ProviderModels.Refresh(target, models);
                if (!models.Contains(draft.Model, StringComparer.Ordinal)) draft.Model = models[0];
                ProviderModels.Select(target, ProviderModels.Visible(target).Append(draft.Model));
                draft.DetectedModelsJson = target.DetectedModelsJson; draft.SelectedModelsJson = target.SelectedModelsJson;
                refreshing = true; model.ItemsSource = models; model.Text = draft.Model; refreshing = false;
                info.Text = WorkflowText($"Connexion réussie · {models.Count} modèles disponibles. Choisissez celui à utiliser.", $"Connected · {models.Count} models available. Choose a model to use.");
            }
            catch (OperationCanceledException) { info.Text = WorkflowText("Recherche interrompue ou délai dépassé. Vous pouvez réessayer.", "Search cancelled or timed out. You can retry."); }
            catch (Exception ex) { info.Text = ex.Message; }
            finally { form.Request = null; host.IsEnabled = true; progress.IsActive = false; progress.Visibility = Visibility.Collapsed; form.BusyChanged(false); }
        };
        refreshing = true; choice.SelectedItem = selected; Select(selected);
        return form;
    }
}
