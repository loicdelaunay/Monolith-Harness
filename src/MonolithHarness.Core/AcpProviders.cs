using System.Diagnostics;
using System.Text.Json;

namespace MonolithHarness.Core;

public sealed record AcpPreset(string Kind, string Name, string Package, string DescriptionFr, string DescriptionEn, string Documentation);

public static class AcpProviders
{
    public const string DefaultModel = "default";
    public static IReadOnlyList<AcpPreset> Presets { get; } = [
        new("antigravity-acp", "Antigravity · ACP", "google-antigravity-acp@2.0.1",
            "Connexion à Antigravity CLI (agy) via un adaptateur ACP. Installez et connectez agy à votre compte Google. Node.js est requis ; l’adaptateur est téléchargé au premier lancement. Les outils suivent les permissions d’Antigravity, sans approbation automatique. L’adaptateur actuel ne relaie pas les demandes d’autorisation et ne propose pas de catalogue de modèles : default conserve son modèle configuré. Le mode Agent / Exécution est requis.",
            "Connect to Antigravity CLI (agy) through an ACP adapter. Install agy and sign in to Google. Node.js is required; the adapter downloads on first launch. Tools follow Antigravity permissions without automatic approval. The current adapter does not forward permission requests or expose a model catalog: default keeps its configured model. Agent / Execute mode is required.",
            "https://www.antigravity.google/docs/cli/install/"),
        new("chatgpt-acp", "ChatGPT Plus / Pro · Codex", "@agentclientprotocol/codex-acp@2.1.1",
            "Connexion à votre compte ChatGPT avec l’authentification officielle de Codex, sans clé API. Utilise les modèles et limites Codex de votre abonnement Plus ou Pro. Cliquez sur Connecter mon compte pour ouvrir la connexion OpenAI. Node.js est requis ; l’adaptateur ACP et Codex sont téléchargés au premier lancement. Les outils sont ceux de Codex ; les demandes d’autorisation sont relayées dans Monolith Harness. En mode Chat, les outils web et Python Monolith sont indisponibles ; utilisez le mode Agent pour les outils natifs.",
            "Sign in to ChatGPT through Codex without an API key. Uses your Plus or Pro plan’s Codex models and limits. Click Connect my account to open OpenAI sign-in. Node.js is required; the ACP adapter and Codex download on first launch. Codex owns the tools; permission requests are forwarded to Monolith Harness. Monolith web and Python tools are unavailable in Chat mode; use Agent mode for native tools.",
            "https://learn.chatgpt.com/docs/auth")
    ];
    public static AcpPreset? Find(string? kind) => Presets.FirstOrDefault(x => x.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase));
    public static Provider Create(AcpPreset preset) => new() { Name = preset.Name, Kind = preset.Kind, BaseUrl = "", Model = DefaultModel,
        SupportsImages = false, OpenCodeTools = true, DetectedModelsJson = "[\"default\"]", SelectedModelsJson = "[\"default\"]" };
    public static string SystemPrompt(string language, string mode) =>
        (language == "en" ? "You are connected through ACP inside Monolith Harness. Answer directly in English. " : "Tu es connecté via ACP dans Monolith Harness. Réponds directement en français. ") +
        "Use only your agent's native tools; the application's API tools and subagent orchestrator are not available through this connection. Respect every permission denial. " +
        (AgentPolicy.ReadOnly(mode) && mode != "chat" ? "Read-only planning: never change files, execute commands or delegate actions. " : mode == "chat" ? "Conversational mode: answer using the provided context; do not inspect the filesystem or execute tools. " : "") + MarkdownDisplay.Instructions;

    public static string[] Arguments(Provider provider)
    {
        try { return JsonSerializer.Deserialize<string[]>(provider.AcpArgumentsJson) is { } args && args.All(x => x != null && !x.Contains('\0'))
            ? args : throw new ArgumentException("Arguments ACP : tableau JSON de chaînes requis."); }
        catch (JsonException ex) { throw new ArgumentException("Arguments ACP : tableau JSON de chaînes requis.", ex); }
    }
    public static void Validate(Provider provider)
    {
        if (Find(provider.Kind) == null) throw new ArgumentException("Fournisseur ACP inconnu.");
        _ = Arguments(provider);
        if (provider.ExecutablePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || provider.ExecutablePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choisissez un exécutable ACP (.exe) ou son script .js ; les commandes shell ne sont pas acceptées.");
    }

    public static ProcessStartInfo StartInfo(Provider provider, string directory, string? effort = null)
    {
        Validate(provider);
        var preset = Find(provider.Kind)!;
        var args = Arguments(provider);
        var custom = !string.IsNullOrWhiteSpace(provider.ExecutablePath);
        var executable = custom ? provider.ExecutablePath.Trim().Trim('"') : Node();
        var start = new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetFullPath(directory),
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false), StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        if (custom && executable.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) { start.FileName = Node(); start.ArgumentList.Add(executable); }
        if (!custom)
        {
            var npx = Path.Combine(Path.GetDirectoryName(start.FileName)!, "node_modules", "npm", "bin", "npx-cli.js");
            if (!File.Exists(npx)) throw new FileNotFoundException("Installez Node.js avec npm pour démarrer la connexion ACP.");
            start.ArgumentList.Add(npx); start.ArgumentList.Add("--yes"); start.ArgumentList.Add(preset.Package);
        }
        foreach (var argument in args) start.ArgumentList.Add(argument);
        if (provider.Kind == "antigravity-acp" && !custom)
        {
            // This adapter defaults to bypassing permissions; always opt out.
            start.ArgumentList.Add("--no-skip-permissions");
            if (!args.Any(x => x == "--binary-path" || x == "-b" || x.StartsWith("--binary-path="))) { start.ArgumentList.Add("--binary-path"); start.ArgumentList.Add(Agy()); }
            if (provider.Model.Length > 0 && provider.Model != DefaultModel) { start.ArgumentList.Add("--model"); start.ArgumentList.Add(provider.Model); }
            if (effort is "low" or "medium" or "high") { start.ArgumentList.Add("--effort"); start.ArgumentList.Add(effort); }
        }
        if (provider.Kind == "chatgpt-acp")
        {
            // Never silently consume API billing or inherit a third-party model provider.
            start.Environment.Remove("OPENAI_API_KEY"); start.Environment.Remove("CODEX_API_KEY");
            if (!custom) start.Environment.Remove("CODEX_PATH");
            start.Environment["MODEL_PROVIDER"] = "openai";
            start.Environment["DEFAULT_AUTH_REQUEST"] = "{\"methodId\":\"chat-gpt\"}";
            start.Environment["CODEX_CONFIG"] = "{\"model_provider\":\"openai\"}";
            start.Environment["INITIAL_AGENT_MODE"] = "read-only";
        }
        return start;
    }
    static string Agy()
    {
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(x => Path.Combine(x.Trim('"'), OperatingSystem.IsWindows() ? "agy.exe" : "agy"))
            .Prepend(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe"))
            .Prepend(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "agy"));
        return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Antigravity CLI (agy) est requis. Installez-le puis connectez votre compte Google en lançant agy. Un chemin personnalisé peut être fourni avec les arguments ACP --binary-path.");
    }
    static string Node()
    {
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(x => Path.Combine(x.Trim('"'), OperatingSystem.IsWindows() ? "node.exe" : "node"))
            .Prepend(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"));
        return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Node.js est requis pour les adaptateurs ACP. Installez Node.js ou indiquez un exécutable ACP autonome.");
    }
}
