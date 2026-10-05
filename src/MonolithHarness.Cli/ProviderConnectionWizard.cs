using MonolithHarness.Core;

namespace MonolithHarness.Cli;

public sealed record ConnectionStep(string Title, string Body = "", List<Choice>? Choices = null, bool Secret = false, string Initial = "");
public sealed record ProviderPreset(string Id, string Name, string Url, string Kind, bool OptionalKey);

public sealed partial class ProviderConnectionWizard(HttpClient http,
    Func<ConnectionStep, Task<string?>> prompt, Func<Provider, string, CancellationToken, Task<int>> save,
    Action<string>? progress = null)
{
    public static IReadOnlyList<ProviderPreset> Presets { get; } = new ProviderPreset[] {
        new("openai", "OpenAI", "https://api.openai.com/v1", "openai", false),
        new("deepseek", "DeepSeek", "https://api.deepseek.com", "openai", false),
    }.Concat(ProviderPresets.Cloud.Select(x => new ProviderPreset(x.Id, x.Name, x.BaseUrl, "openai", false))).Concat(new ProviderPreset[] {
        new("compatible", "Autre API compatible OpenAI / Other compatible API", "", "openai", true),
        new("local-model", "Local · GGUF · Beta", "model/", "local", true),
        new("local", "API locale compatible OpenAI / Local compatible API", "", "openai", true),
        new("antigravity-acp", "Antigravity · ACP", "", "antigravity-acp", true),
        new("chatgpt-acp", "ChatGPT Plus / Pro · Codex", "", "chatgpt-acp", true),
        new("opencode", "OpenCode", "http://127.0.0.1:4096", "opencode", true)
    }).ToArray();
    public async Task<int?> RunAsync(CancellationToken ct)
    {
        var type = await prompt(new("1/4 · Type de fournisseur / Provider type", Choices: Presets.Select(p => new Choice(p.Id, p.Name,
            ProviderPresets.Find(p.Id) is { } cloud ? cloud.DescriptionFr + " / " + cloud.DescriptionEn : p.Url)).ToList()));
        if (type == null) return null;
        var preset = Presets.Single(p => p.Id == type);
        var cloudPreset = ProviderPresets.Find(preset.Id);
        if (AcpProviders.Find(preset.Kind) is { } acpPreset) return await AcpAsync(acpPreset, ct);
        if (preset.Kind == "local") return await LocalAsync(ct);
        var draft = new Provider { Name = preset.Name.Split(" / ")[0], Kind = preset.Kind, BaseUrl = preset.Url, Model = "", Username = preset.Kind == "opencode" ? "opencode" : "" };
        // Credentials stay in memory until the final confirmation, including during discovery.
        var key = await prompt(new("2/4 · " + (draft.IsOpenCode ? "Mot de passe serveur / Server password" : "Clé API / API key"),
            preset.OptionalKey ? "Saisie masquée. Vide si aucune authentification. / Masked. Empty if no authentication."
                : "Saisie masquée ; clé chiffrée après validation. / Masked; encrypted after confirmation." +
                    (cloudPreset == null ? "" : "\n" + cloudPreset.DescriptionFr + "\n" + cloudPreset.DescriptionEn + "\nClé API / API key: " + cloudPreset.KeysUrl), Secret: true));
        if (key == null) return null;
        while (!preset.OptionalKey && string.IsNullOrWhiteSpace(key))
        {
            key = await prompt(new("2/4 · Clé API requise / API key required", Secret: true));
            if (key == null) return null;
        }
        while (true)
        {
            var url = await prompt(new("2/4 · URL de l’API / API URL", "HTTP(S), avec /v1 si nécessaire. / Include /v1 when required.", Initial: draft.BaseUrl));
            if (url == null) return null;
            try { _ = ChatEngine.Endpoint(url.Trim(), "models"); draft.BaseUrl = url.Trim().TrimEnd('/'); break; }
            catch (ArgumentException) { progress?.Invoke("URL HTTP(S) invalide / Invalid HTTP(S) URL"); }
        }
        if (draft.IsOpenCode)
        {
            var username = await prompt(new("2/4 · Utilisateur OpenCode / OpenCode username", Initial: "opencode"));
            if (username == null) return null;
            draft.Username = string.IsNullOrWhiteSpace(username) ? "opencode" : username.Trim();
            var bypass = await prompt(new("2/4 · [BETA] Bypass free limitation",
                "Permet d’utiliser les modèles gratuits sur un autre harnais / Use free models on another harness",
                [new("yes", "Oui / Yes (Recommandé / Recommended)"), new("no", "Non / No")]));
            if (bypass == null) return null;
            draft.BypassFreeLimitation = bypass != "no";
        }
        List<string> models = [];
        while (models.Count == 0)
        {
            var mode = await prompt(new("3/4 · Modèles / Models", "Détecter via l’API ou saisir les identifiants exacts. / Discover via API or enter exact IDs.",
                [new("auto", "Détection automatique / Auto-detect"), new("manual", "Saisie manuelle / Enter manually")]));
            if (mode == null) return null;
            if (mode == "auto")
            {
                progress?.Invoke("Détection des modèles… / Detecting models…");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    models = ProviderModels.Normalize(draft.IsOpenCode
                        ? (await new OpenCodeEngine(http).ModelsAsync(draft, key, null, timeout.Token)).Select(m => m.Reference)
                        : await new ChatEngine(http).ModelsAsync(draft, key, timeout.Token));
                    if (models.Count == 0) throw new InvalidOperationException("Aucun modèle retourné / No models returned");
                }
                catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException or IOException or InvalidOperationException || error is OperationCanceledException && !ct.IsCancellationRequested)
                {
                    // Do not echo server bodies, which can contain credentials or sensitive context.
                    var reason = error is OperationCanceledException ? "Délai dépassé / Timed out" : error is HttpRequestException ? "Erreur HTTP / HTTP error" : "Liste indisponible / Models unavailable";
                    var retry = await prompt(new("Détection impossible / Discovery failed", reason + ". Vérifiez URL et clé ; vous pouvez saisir les modèles. / Check URL and key or enter models manually.",
                        [new("manual", "Saisie manuelle / Enter manually"), new("retry", "Réessayer / Retry"), new("cancel", "Annuler / Cancel")]));
                    if (retry is null or "cancel") return null;
                    if (retry == "retry") continue;
                    mode = "manual";
                }
            }
            if (mode == "manual")
            {
                var examples = cloudPreset?.SuggestedChatModels is { Count: > 0 } suggested ? "\nExemples documentés · accès non vérifié / Documented examples · access not verified: " + string.Join(", ", suggested) : "";
                var value = await prompt(new("3/4 · Identifiants des modèles / Model IDs", "Un ou plusieurs modèles séparés par des virgules. OpenCode : fournisseur/modèle. / Comma-separated IDs. OpenCode: provider/model." + examples));
                if (value == null) return null;
                models = ProviderModels.Normalize(value.Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(x => ProviderPresets.NormalizeModelId(draft, x.Trim())));
            }
        }
        var active = models.Count == 1 ? models[0] : await prompt(new("3/4 · Modèle par défaut / Default model", "Tous les modèles seront disponibles via /models. / All models will be available via /models.", models.Select(m => new Choice(m, m)).ToList()));
        if (active == null) return null;
        ModelContexts.Select(draft, active);
        ProviderModels.Refresh(draft, models); ProviderModels.Select(draft, models);
        var name = await prompt(new("4/4 · Nom de cette connexion / Connection name", "Plusieurs connexions du même type sont possibles. / Multiple connections of the same type are supported.", Initial: draft.Name));
        if (name == null) return null;
        if (!string.IsNullOrWhiteSpace(name)) draft.Name = name.Trim();
        var bypassInfo = draft.IsOpenCode && draft.BypassFreeLimitation ? " · [BETA bypass]" : "";
        var decision = await prompt(new("4/4 · Valider la connexion / Confirm connection", $"{draft.Name}\n{draft.BaseUrl}\n{models.Count} modèle(s) · {draft.Model}{bypassInfo}\n" + (key.Length == 0 ? "Sans clé / No key" : "Clé fournie (masquée) / Key provided (hidden)"),
            [new("save", "Valider et utiliser / Save and use"), new("cancel", "Annuler / Cancel")]));
        if (decision != "save") return null;
        ct.ThrowIfCancellationRequested();
        return await save(draft, key, ct);
    }

    async Task<int?> LocalAsync(CancellationToken ct)
    {
        var path = await prompt(new("2/4 · Modèle local GGUF / Local GGUF model", "Chemin du fichier existant ; il sera copié dans model/ près de l’exécutable. / Existing file path; it will be copied to model/ next to the executable."));
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Trim().Trim('"');
        var backend = await prompt(new("3/4 · Calcul / Computing", Choices: [new("auto", "Auto"), new("cpu", "CPU"), new("vulkan", "Vulkan")]));
        if (backend == null) return null;
        var config = new LocalProviderSettings { Backend = backend };
        var machine = await MachineCapabilities.ReadAsync(ct);
        var estimate = LocalModelCompatibility.Estimate(path, "chat", "", new FileInfo(path.Trim().Trim('"')).Length, machine, backend: config.Backend);
        if (!estimate.CanLoad) throw new InvalidOperationException(estimate.Reason);
        var confirmation = await prompt(new("4/4 · Préparer Local / Prepare Local", machine.Summary + "\n\n" + estimate.Label + " · " + estimate.Reason +
            "\nTélécharge le moteur officiel llama.cpp avec vérification SHA-256, importe le modèle et le charge. / Downloads the official llama.cpp engine with SHA-256 verification, imports and loads the model.",
            Choices: [new("prepare", "Importer et charger / Import and load"), new("cancel", "Annuler / Cancel")]));
        if (confirmation != "prepare") return null;
        var updates = new Progress<LocalTransferProgress>(value => progress?.Invoke(value.Detail + (value.Percent is { } percent ? $" · {percent:0}%" : "")));
        var model = await LocalModelImport.ImportAsync(path.Trim().Trim('"'), "chat", "", config, true, updates, ct); config.Add(model);
        await LocalRuntimeInstaller.EnsureAsync(config, "chat", machine, updates, ct);
        var draft = new Provider { Name = "Local", Kind = "local", BaseUrl = "http://127.0.0.1", Model = model.Id, SupportsImages = false, LocalModelsJson = config.Json() };
        ModelContexts.MergeLocal(draft);
        ProviderModels.Refresh(draft, [model.Id]); ProviderModels.Select(draft, [model.Id]);
        progress?.Invoke("Chargement du modèle… / Loading model…"); using var ready = await LocalModelRuntime.ChatAsync(draft, ct);
        return await save(draft, "", ct);
    }
}
