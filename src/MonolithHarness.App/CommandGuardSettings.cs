using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    (FrameworkElement Panel, Action<FeatureSettings> Save, Func<bool> Validate) BuildCommandGuardSettings(Window owner, IReadOnlyList<Provider> sources)
    {
        var current = FeatureSettings.Read(state.FeaturesJson);
        var body = new StackPanel { Spacing = 12 };
        var title = Label(WorkflowText("Validation des commandes", "Command validation"), 16);
        title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; body.Children.Add(title);
        body.Children.Add(Label(WorkflowText("Le mode Automatique utilise le validateur choisi ici. Une analyse incertaine, invalide ou indisponible demande votre accord. Les analyses peuvent se tromper ; les règles du projet restent appliquées.",
            "Automatic mode uses the validator selected here. Uncertain, invalid or unavailable analysis requires your approval. Analysis can be wrong; project rules still apply."), 12));
        var mode = new ComboBox { Header = WorkflowText("Validateur", "Validator"), ItemsSource = new[] { "LANCET Nano · local", WorkflowText("Modèle existant", "Existing model") },
            SelectedIndex = current.CommandGuardMode == "model" ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(mode);
        var lancet = new StackPanel { Spacing = 10 };
        var installed = Label("", 13);
        void RefreshInstalled() => installed.Text = LocalCommandGuard.Installed
            ? WorkflowText("Installé · LANCET Nano ", "Installed · LANCET Nano ") + LocalCommandGuard.Version
            : WorkflowText("LANCET n’est pas préparé. Les commandes demanderont votre accord.", "LANCET is not prepared. Commands will require your approval.");
        RefreshInstalled(); lancet.Children.Add(installed);
        lancet.Children.Add(Label(WorkflowText("Téléchargement : environ 116 Mo de modèle, puis les composants CPU. Les fichiers sont vérifiés avant préparation. Aucune commande n’est transmise à un fournisseur pour l’analyse LANCET.",
            "Download: about 116 MB of model files, followed by CPU components. Files are verified before preparation. LANCET analysis does not send commands to a provider."), 12));
        var directory = new TextBox { Header = WorkflowText("Installation manuelle : dossier du bundle", "Manual installation: bundle directory"),
            PlaceholderText = WorkflowText("Dossier contenant classify.py et model/", "Directory containing classify.py and model/"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var browse = new Button { Content = WorkflowText("Parcourir…", "Browse…") };
        lancet.Children.Add(directory); lancet.Children.Add(browse);
        lancet.Children.Add(new HyperlinkButton { Content = WorkflowText("Fichiers LANCET pour l’installation manuelle", "LANCET files for manual installation"),
            NavigateUri = new Uri("https://huggingface.co/fingerthief/lancet-nano/tree/2450cfbea514baef810f4087d6d31854783f3d2e/bundle"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new(0) });
        lancet.Children.Add(Label(WorkflowText("Importez le bundle complet LANCET Nano 0.4.3, avec model/, classify.py et les licences. L’import copie uniquement les fichiers attendus et vérifie leurs empreintes. Pour une installation hors ligne, placez dans wheels/ les roues Python compatibles et leurs dépendances (NumPy 2.2.6, Tokenizers 0.22.2, ONNX Runtime 1.23.2) ; sinon les composants sont téléchargés depuis PyPI.",
            "Import the complete LANCET Nano 0.4.3 bundle, including model/, classify.py and licenses. Import copies only expected files and verifies their checksums. For offline installation, place compatible Python wheels and dependencies in wheels/ (NumPy 2.2.6, Tokenizers 0.22.2, ONNX Runtime 1.23.2); otherwise components are downloaded from PyPI."), 12));
        var progress = new ProgressBar { Height = 4, Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
        var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary };
        lancet.Children.Add(progress); lancet.Children.Add(status);
        var download = new Button { Content = WorkflowText("Télécharger et installer", "Download and install"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var import = new Button { Content = WorkflowText("Installer depuis ce dossier", "Install from this directory") };
        var cancel = new Button { Content = T("Annuler"), Visibility = Visibility.Collapsed };
        var actions = Row(import, download); actions.HorizontalAlignment = HorizontalAlignment.Right;
        lancet.Children.Add(cancel); lancet.Children.Add(actions);
        CancellationTokenSource? operation = null;
        owner.Closed += (_, _) => operation?.Cancel();
        async Task Run(string? source)
        {
            if (operation != null || installingCommandGuard) return;
            operation = new(); installingCommandGuard = true;
            foreach (var control in new Control[] { mode, directory, browse, download, import }) control.IsEnabled = false;
            cancel.Visibility = progress.Visibility = Visibility.Visible; progress.IsIndeterminate = true;
            try
            {
                var reporter = new Progress<CommandGuardProgress>(value =>
                {
                    progress.IsIndeterminate = value.Phase is not ("download" or "import");
                    if (value.Total > 0) progress.Value = value.Downloaded * 100d / value.Total;
                    status.Text = value.Phase switch {
                        "download" => WorkflowText("Téléchargement : ", "Downloading: ") + $"{value.Downloaded / 1_000_000d:0.0} / {value.Total / 1_000_000d:0.0} Mo",
                        "import" => WorkflowText("Import : ", "Importing: ") + $"{value.Downloaded / 1_000_000d:0.0} / {value.Total / 1_000_000d:0.0} Mo",
                        "verify" => WorkflowText("Vérification des fichiers…", "Verifying files…"),
                        "dependencies" => WorkflowText("Installation des composants CPU…", "Installing CPU components…"),
                        "prepare" => WorkflowText("Préparation du modèle…", "Preparing model…"),
                        "ready" => WorkflowText("Validation LANCET prête.", "LANCET validation ready."),
                        _ => WorkflowText("Préparation de l’installation…", "Preparing installation…") };
                });
                if (source == null) await LocalCommandGuard.InstallAsync(reporter, operation.Token);
                else await LocalCommandGuard.ImportAsync(source, reporter, operation.Token);
                status.Text = WorkflowText("Validation LANCET prête.", "LANCET validation ready.");
            }
            catch (OperationCanceledException) { status.Text = WorkflowText("Installation annulée.", "Installation cancelled."); }
            catch (Exception ex) { status.Text = WorkflowText("Installation impossible : ", "Installation failed: ") + ex.Message; }
            finally
            {
                operation.Dispose(); operation = null; installingCommandGuard = false;
                foreach (var control in new Control[] { mode, directory, browse, download, import }) control.IsEnabled = true;
                cancel.Visibility = progress.Visibility = Visibility.Collapsed; RefreshInstalled();
            }
        }
        browse.Click += async (_, _) => { try { var folder = await PickLocalModelFolderAsync(owner); if (folder != null) directory.Text = folder; } catch (Exception ex) { status.Text = ex.Message; } };
        download.Click += async (_, _) => await Run(null);
        import.Click += async (_, _) => { if (string.IsNullOrWhiteSpace(directory.Text)) status.Text = WorkflowText("Choisissez le dossier du bundle.", "Choose the bundle directory."); else await Run(directory.Text.Trim()); };
        cancel.Click += (_, _) => operation?.Cancel();

        var existing = new StackPanel { Spacing = 10 };
        var available = sources.Where(ProviderModels.CanChat).ToList();
        var chosen = available.FirstOrDefault(p => p.Id == current.CommandGuardProviderId) ?? available.FirstOrDefault();
        var provider = new ComboBox { Header = WorkflowText("Fournisseur du validateur", "Validator provider"), ItemsSource = available, SelectedItem = chosen, HorizontalAlignment = HorizontalAlignment.Stretch };
        var model = new ComboBox { Header = WorkflowText("Modèle de validation", "Validation model"), IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        SettingsModelPicker.Connect(provider, model, current.CommandGuardProviderId == chosen?.Id ? current.CommandGuardModel : chosen?.Model ?? "");
        var notice = Label("", 12);
        void RefreshNotice() => notice.Text = provider.SelectedItem is Provider selected && selected.IsLocal
            ? WorkflowText("Ce modèle local analysera les commandes sur votre ordinateur. Préparez-le au préalable dans Fournisseurs / Local.", "This local model analyzes commands on your computer. Prepare it first in Providers / Local.")
            : WorkflowText("Un modèle API reçoit la commande, le shell et le dossier de travail à analyser ; cet appel peut être facturé. Aucun outil d’exécution n’est fourni au validateur.", "An API model receives the command, shell and working directory for analysis; requests may be billed. The validator receives no execution tools.");
        provider.SelectionChanged += (_, _) => RefreshNotice(); RefreshNotice();
        existing.Children.Add(provider); existing.Children.Add(model); existing.Children.Add(notice);
        var error = Label("", 12);
        body.Children.Add(lancet); body.Children.Add(existing); body.Children.Add(error);
        void RefreshModeVisibility() { lancet.Visibility = mode.SelectedIndex == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed; existing.Visibility = mode.SelectedIndex == 1 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed; }
        mode.SelectionChanged += (_, _) => RefreshModeVisibility(); RefreshModeVisibility();
        bool Validate()
        {
            error.Text = "";
            if (installingCommandGuard) return false;
            if (mode.SelectedIndex == 1 && (provider.SelectedItem is not Provider || string.IsNullOrWhiteSpace(SettingsModelPicker.Read(model)) || SettingsModelPicker.Read(model).Length > 300))
            { error.Text = WorkflowText("Choisissez un fournisseur et un modèle de validation.", "Choose a validation provider and model."); return false; }
            return true;
        }
        return (FluentDesign.Surface(body, 16), settings => {
            settings.CommandGuardMode = mode.SelectedIndex == 1 ? "model" : "lancet";
            settings.CommandGuardProviderId = (provider.SelectedItem as Provider)?.Id ?? 0;
            settings.CommandGuardModel = SettingsModelPicker.Read(model);
        }, Validate);
    }
    IReadOnlyList<ComboBoxItem> PermissionModeItems() => new[] { PermissionModes.Deny, PermissionModes.Ask, PermissionModes.Allow, PermissionModes.Full }.Select(value => {
        var text = Label(value == PermissionModes.Ask ? WorkflowText("Demander (par défaut)", "Ask (default)") : PermissionCaption(value), 14);
        object content = text;
        if (value == PermissionModes.Full)
        {
            text.Foreground = FluentDesign.Adapt(230, 157, 57);
            var icon = FluentDesign.Icon("\uE7BA", 15); icon.Foreground = text.Foreground; icon.VerticalAlignment = VerticalAlignment.Center;
            content = Row(icon, text);
        }
        return new ComboBoxItem { Content = content, Tag = value };
    }).ToList();
}
