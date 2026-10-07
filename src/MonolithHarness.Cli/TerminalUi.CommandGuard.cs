using MonolithHarness.Core;

namespace MonolithHarness.Cli;

public sealed partial class TerminalUi
{
    async Task CommandGuardPreferences(WorkspaceSnapshot snapshot)
    {
        var settings = FeatureSettings.Read(snapshot.State.FeaturesJson);
        var status = settings.CommandGuardMode == "model" ? settings.CommandGuardModel : "LANCET Nano " + LocalCommandGuard.Version;
        var action = await Prompt(L("Validation des commandes", "Command validation"),
            L("Validateur : ", "Validator: ") + status + "\n" + L("Une analyse incertaine, invalide ou indisponible demande votre accord. Les analyses peuvent se tromper.", "Uncertain, invalid or unavailable analysis requires your approval. Analysis can be wrong."),
            [new("lancet", "Utiliser LANCET Nano / Use LANCET Nano"), new("model", "Choisir un modèle configuré / Choose a configured model"),
             new("install", "Télécharger et installer LANCET / Download and install LANCET"), new("import", "Installation manuelle de LANCET / Manual LANCET installation")]);
        if (action == "lancet")
        {
            await SaveFeatures(s => s.CommandGuardMode = "lancet");
            Post(() => notice = LocalCommandGuard.Installed ? L("LANCET est prêt.", "LANCET is ready.") : L("Installez LANCET dans Réglages / Autorisations / Validation des commandes. Les commandes demanderont votre accord en attendant.", "Install LANCET in Settings / Permissions / Command validation. Commands will require your approval until then."));
        }
        if (action == "model")
        {
            var providers = snapshot.Providers.Where(ProviderModels.CanChat).ToList();
            if (providers.Count == 0) { Post(() => notice = L("Configurez d’abord un fournisseur et son modèle.", "Configure a provider and model first.")); return; }
            var id = await Prompt("Fournisseur / Provider", choices: providers.Select(p => new Choice(p.Id.ToString(), p.Name)).ToList());
            if (id == null) return;
            var provider = providers.Single(p => p.Id == int.Parse(id));
            var model = await Prompt("Modèle / Model", provider.IsLocal
                ? L("Le modèle local doit être préparé dans Fournisseurs / Local.", "The local model must be prepared in Providers / Local.")
                : L("Ce fournisseur recevra les commandes, le shell et le dossier à analyser. Ces appels peuvent être facturés ; aucun outil d’exécution n’est fourni.", "This provider receives commands, shell and working directory for analysis. Requests may be billed; no execution tools are provided."),
                ModelCatalog.GetModelsForProvider(provider).Select(m => new Choice(m, m)).Prepend(new("custom", "Saisir un modèle / Custom model")).ToList());
            if (model == "custom") model = await Prompt("Modèle / Model", initial: provider.Model);
            if (string.IsNullOrWhiteSpace(model) || model.Trim().Length > 300) return;
            await SaveFeatures(s => { s.CommandGuardMode = "model"; s.CommandGuardProviderId = provider.Id; s.CommandGuardModel = model.Trim(); });
        }
        if (action is "install" or "import")
        {
            string? directory = null;
            if (action == "import")
            {
                directory = await Prompt(L("Dossier du bundle LANCET", "LANCET bundle directory"),
                    L("Bundle complet 0.4.3 : classify.py, model/ et licences. Les fichiers sont copiés et vérifiés. Les composants CPU sont téléchargés depuis PyPI, sauf si les roues compatibles et leurs dépendances sont présentes dans wheels/ (NumPy 2.2.6, Tokenizers 0.22.2, ONNX Runtime 1.23.2).", "Complete 0.4.3 bundle: classify.py, model/ and licenses. Files are copied and verified. CPU components download from PyPI unless compatible wheels and dependencies exist in wheels/ (NumPy 2.2.6, Tokenizers 0.22.2, ONNX Runtime 1.23.2).") + "\nhttps://huggingface.co/fingerthief/lancet-nano/tree/2450cfbea514baef810f4087d6d31854783f3d2e/bundle");
                if (string.IsNullOrWhiteSpace(directory)) return;
            }
            else if (await Prompt(L("Installer LANCET", "Install LANCET"),
                L("Environ 116 Mo de modèle, puis ses composants CPU. Les fichiers sont vérifiés. Les commandes sont analysées localement.", "About 116 MB of model files, followed by CPU components. Files are verified. Commands are analyzed locally."),
                [new("install", "Télécharger et installer / Download and install"), new("cancel", "Annuler / Cancel")]) != "install") return;
            var progress = new Progress<CommandGuardProgress>(value => Post(() => notice = L("Installation LANCET : ", "LANCET installation: ") +
                (value.Phase is "download" or "import" ? $"{value.Downloaded / 1_000_000d:0.0} / {value.Total / 1_000_000d:0.0} Mo" : value.Phase)));
            if (directory == null) await LocalCommandGuard.InstallAsync(progress, lifetime.Token);
            else await LocalCommandGuard.ImportAsync(directory.Trim(), progress, lifetime.Token);
            Post(() => notice = L("Validation LANCET prête.", "LANCET validation ready."));
        }
    }
}
