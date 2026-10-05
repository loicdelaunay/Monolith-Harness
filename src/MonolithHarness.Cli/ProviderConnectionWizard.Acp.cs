using MonolithHarness.Core;

namespace MonolithHarness.Cli;

public sealed partial class ProviderConnectionWizard
{
    async Task<int?> AcpAsync(AcpPreset preset, CancellationToken ct)
    {
        var draft = AcpProviders.Create(preset);
        var executable = await prompt(new("2/4 · Connexion ACP / ACP connection", preset.DescriptionFr + "\n" + preset.DescriptionEn + "\n" + preset.Documentation +
            "\nExécutable personnalisé facultatif (.exe ou .js). Vide : adaptateur standard. / Optional custom executable (.exe or .js). Blank: standard adapter."));
        if (executable == null) return null;
        draft.ExecutablePath = executable.Trim().Trim('"');
        if (draft.ExecutablePath.Length > 0)
        {
            var args = await prompt(new("2/4 · Arguments ACP / ACP arguments", "Tableau JSON de chaînes / JSON string array", Initial: "[]"));
            if (args == null) return null;
            draft.AcpArgumentsJson = args;
        }
        AcpProviders.Validate(draft);
        var action = await prompt(new("3/4 · Compte et modèles / Account and models", Choices:
            [new("connect", preset.Kind == "chatgpt-acp" ? "Connecter mon compte ChatGPT / Sign in to ChatGPT" : "Tester Antigravity / Test Antigravity"),
             new("later", "Configurer maintenant, connecter plus tard / Configure now, connect later")]));
        if (action == null) return null;
        var models = new List<string> { AcpProviders.DefaultModel };
        if (action == "connect")
        {
            progress?.Invoke("Connexion ACP… Terminez la connexion dans le navigateur si demandé. / Connecting… Complete browser sign-in if requested.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(5));
            models = await new AcpEngine().ModelsAsync(draft, timeout.Token, authenticate: preset.Kind == "chatgpt-acp");
        }
        var selected = models.Count == 1 ? models[0] : await prompt(new("3/4 · Modèle par défaut / Default model",
            "Catalogue de l’agent ; l’accès est vérifié lors d’une réponse. / Agent catalog; access is verified when completing a request.", models.Select(x => new Choice(x, x)).ToList()));
        if (selected == null) return null;
        ModelContexts.Select(draft, selected); ProviderModels.Refresh(draft, models); ProviderModels.Select(draft, models);
        var name = await prompt(new("4/4 · Nom de cette connexion / Connection name", Initial: draft.Name));
        if (name == null) return null;
        if (name.Trim().Length > 0) draft.Name = name.Trim();
        var decision = await prompt(new("4/4 · Valider la connexion / Confirm connection", draft.Name + "\nACP · " + draft.Model + "\n" +
            "Authentification gérée par l’agent, sans clé API enregistrée dans l’application. / Agent-managed sign-in, no API key stored in the application.",
            [new("save", "Valider et utiliser / Save and use"), new("cancel", "Annuler / Cancel")]));
        return decision == "save" ? await save(draft, "", ct) : null;
    }
}
