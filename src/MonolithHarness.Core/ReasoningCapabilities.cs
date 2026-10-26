using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public sealed record ReasoningCapability(string[] Levels, string Source, bool Known = true, bool NonReasoning = false)
{
    public bool Supports(string level) => Levels.Contains(level, StringComparer.OrdinalIgnoreCase);
}

// A request trace describes parameters, never the model's private reasoning effort.
public sealed record ReasoningSelection(string Requested, string Sent, string Fallback = "", string Parameter = "", string Source = "")
{
    public string Describe(bool french = true) => french
        ? $"Réflexion · demandée : {ReasoningCapabilities.Label(Requested)} · envoyée : {ReasoningCapabilities.Label(Sent)}" +
          (Fallback.Length > 0 ? " · repli : " + Fallback : " · repli : aucun") + (Parameter.Length > 0 ? "\n" + Parameter : "")
        : $"Thinking · requested: {ReasoningCapabilities.Label(Requested, false)} · sent: {ReasoningCapabilities.Label(Sent, false)}" +
          (Fallback.Length > 0 ? " · fallback: " + Fallback : " · fallback: none") + (Parameter.Length > 0 ? "\n" + Parameter : "");
}

// Integrated fallbacks checked against official vendor documentation on 2026-10-23:
// https://ai.google.dev/gemini-api/docs/openai
// https://developers.openai.com/api/docs/models
// https://api-docs.deepseek.com/guides/thinking_mode/
// https://docs.z.ai/guides/capabilities/thinking
public static class ReasoningCapabilities
{
    public static readonly string[] All = ["auto", "none", "minimal", "low", "medium", "high", "xhigh", "max", "enabled"];
    public static string Normalize(string? level) => string.IsNullOrWhiteSpace(level) ? "auto" : level.Trim().ToLowerInvariant();
    public static string Label(string level, bool french = true) => level switch
    {
        "enabled" => french ? "Activée" : "Enabled", "auto" => "Auto", "none" => french ? "Sans réflexion" : "No reasoning",
        "minimal" => french ? "Minimale" : "Minimal", "low" => french ? "Faible" : "Low",
        "medium" => french ? "Moyenne" : "Medium", "high" => french ? "Élevée" : "High",
        "xhigh" => french ? "Très élevée" : "Extra high", "max" => french ? "Maximale" : "Maximum", _ => level
    };
    static ReasoningCapability Known(string source, params string[] levels) => new(["auto", .. levels], source);
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentDictionary<string, byte>> rejected = new();
    static string Key(Provider provider, string model) => provider.Id + "|" + provider.Kind + "|" + provider.BaseUrl.TrimEnd('/') + "|" + model;
    public static void Reject(Provider provider, string level) => rejected.GetOrAdd(Key(provider, provider.Model), _ => new())[level] = 0;
    public static ReasoningCapability For(Provider? provider, string? model = null)
    {
        var result = Discover(provider, model);
        if (provider != null && rejected.TryGetValue(Key(provider, model ?? provider.Model), out var denied))
            return result with { Levels = result.Levels.Where(x => x == "auto" || !denied.ContainsKey(x)).ToArray(), Source = result.Source + " · niveaux refusés par l’API retirés" };
        return result;
    }
    static ReasoningCapability Discover(Provider? provider, string? model)
    {
        if (provider == null) return new(["auto"], "Aucun modèle", false);
        model ??= provider.Model;
        var stored = ModelContexts.Reasoning(provider, model);
        if (stored.Levels != null)
            return new(["auto", .. stored.Levels.Where(x => x != "auto")], stored.Source, true,
                stored.Source.EndsWith(":non-reasoning", StringComparison.Ordinal));
        // External adapters must declare their variants/options. Never infer them from their model name.
        if (provider.IsExternalAgent || provider.IsLocal) return new(["auto"], "Capacités non déclarées : actualisez les modèles", false);
        var preset = ProviderPresets.Resolve(provider.BaseUrl)?.Id;
        var id = model.ToLowerInvariant();
        if (provider.IsVertex || preset == "gemini")
        {
            if (id.StartsWith("google/")) id = id[7..];
            if (id.StartsWith("models/")) id = id[7..];
            if (id.StartsWith("gemini-4-argon")) return Known("Google · catalogue intégré", "low", "medium", "high");
            if (id.StartsWith("gemini-3-pro") && !id.Contains("image")) return Known("Google · catalogue intégré", "low", "high");
            if (id.StartsWith("gemini-3.1-pro") && !id.Contains("image")) return Known("Google · catalogue intégré", "low", "medium", "high");
            if (id.StartsWith("gemini-3.8-flash") || id.StartsWith("gemini-3.7-flash")) return Known("Google · catalogue intégré", "low", "medium", "high");
            if (id.StartsWith("gemini-3") && id.Contains("flash") && !id.Contains("image") && !id.Contains("live")) return Known("Google · catalogue intégré", "minimal", "low", "medium", "high");
            if (id.StartsWith("gemini-2.5-pro")) return Known("Google · catalogue intégré", "low", "medium", "high");
            if (id.StartsWith("gemini-2.5-flash")) return Known("Google · catalogue intégré", "none", "minimal", "low", "medium", "high");
        }
        if (preset == "zai" && id.StartsWith("glm-5.3")) return Known("Z.ai · catalogue intégré", "low", "high", "max");
        if (preset == "zai" && id.StartsWith("glm-5.2")) return Known("Z.ai · catalogue intégré", "none", "high", "max");
        if (preset == "zai" && (id.StartsWith("glm-5.1") || id == "glm-5" || id.StartsWith("glm-4.7") || id.StartsWith("glm-4.6")))
            return Known("GLM · activation uniquement", "none", "enabled");
        if (Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var url) && url.Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase))
        {
            if (id.StartsWith("deepseek-v4") || id is "deepseek-flash" or "deepseek-pro") return Known("DeepSeek · catalogue intégré", "none", "low", "high", "max");
        }
        if (Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var api) && api.Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase))
        {
            if (id.StartsWith("gpt-6") && id.Contains("luna")) return Known("OpenAI · catalogue intégré", "none", "low", "medium", "high", "xhigh", "max");
            if (id.StartsWith("gpt-6") && (id.Contains("sol") || id.Contains("astra"))) return Known("OpenAI · catalogue intégré", "low", "medium", "high", "xhigh", "max");
            if (id.StartsWith("gpt-5.1")) return Known("OpenAI · catalogue intégré", "none", "low", "medium", "high");
            if (id.StartsWith("gpt-5.2") || id.StartsWith("gpt-5.4") || id.StartsWith("gpt-5.5")) return id.Contains("pro")
                ? Known("OpenAI · catalogue intégré", "medium", "high", "xhigh") : Known("OpenAI · catalogue intégré", "none", "low", "medium", "high", "xhigh");
            if (id is "gpt-5" or "gpt-5-mini" or "gpt-5-nano") return Known("OpenAI · catalogue intégré", "minimal", "low", "medium", "high");
            if (id.StartsWith("o3") || id.StartsWith("o4-mini")) return Known("OpenAI · catalogue intégré", "low", "medium", "high");
            if (id.StartsWith("gpt-4.1") || id.StartsWith("gpt-4o")) return new(["auto", "none"], "OpenAI:non-reasoning", true, true);
        }
        return new(["auto"], "Capacités inconnues : seul le comportement par défaut est disponible", false);
    }
    public static ReasoningSelection Select(Provider provider, string? requested)
    {
        var level = Normalize(requested); var caps = For(provider);
        if (level == "none" && !caps.Supports(level)) throw ChatReasoning.Unsupported(provider.Model, caps.Source);
        return caps.Supports(level) ? new(level, level, Source: caps.Source) :
            new(level, "auto", "Niveau non déclaré pour ce modèle ; paramètres par défaut", Source: caps.Source);
    }
    public static void Capture(Provider provider, string model, JsonObject data, string source = "API")
    {
        rejected.TryRemove(Key(provider, model), out _);
        // OpenRouter declares per-model effort levels and mandatory reasoning, including the distinction
        // between a null list (all gateway efforts) and an absent list (activation only).
        // https://openrouter.ai/docs/guides/best-practices/reasoning-tokens
        if (data["reasoning"] is JsonObject reasoning)
        {
            var levels = new List<string>();
            if (reasoning.TryGetPropertyValue("supported_efforts", out var efforts))
                levels.AddRange(efforts is JsonArray list ? list.Select(x => Normalize(x?.ToString())).Where(All.Contains) :
                    All.Where(x => x is not ("auto" or "none" or "enabled")));
            else levels.Add("enabled");
            if (reasoning["mandatory"]?.ToString().Equals("true", StringComparison.OrdinalIgnoreCase) != true) levels.Add("none");
            ModelContexts.SetReasoning(provider, model, All.Where(levels.Contains), source); return;
        }
        var declared = (data["effort"] as JsonObject)?["supported_levels"] as JsonArray ?? data["reasoning_effort_levels"] as JsonArray;
        if (declared != null)
        {
            var levels = declared.Select(x => Normalize(x?.ToString())).Where(All.Contains).ToList();
            if (provider.Kind == "deepseek" || Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var ds) && ds.Host == "api.deepseek.com") levels.Add("none");
            ModelContexts.SetReasoning(provider, model, levels, source); return;
        }
        if ((data["capabilities"] as JsonObject)?["reasoning"] is JsonValue value && value.TryGetValue<bool>(out var enabled) && !enabled)
            ModelContexts.SetReasoning(provider, model, ["none"], source + ":non-reasoning");
    }
    public static void CaptureOpenCode(Provider provider, string model, JsonObject? data)
    {
        if (data == null) return;
        var levels = new List<string>();
        if (data["variants"] is JsonObject variants)
            foreach (var variant in variants)
            {
                if (ChatReasoning.ExplicitlyDisabled(variant.Value)) levels.Add("none");
                var effort = Normalize((variant.Value as JsonObject)?["reasoningEffort"]?.ToString() ?? (variant.Value as JsonObject)?["reasoning_effort"]?.ToString() ?? variant.Key);
                if (All.Contains(effort) && effort != "none") levels.Add(effort);
            }
        if (ChatReasoning.IsFalse(data["capabilities"]?["reasoning"]))
            ModelContexts.SetReasoning(provider, model, ["none"], "OpenCode:non-reasoning");
        else ModelContexts.SetReasoning(provider, model, levels, "OpenCode · variantes déclarées");
    }
    public static string Parameter(JsonObject payload) => payload["reasoning_effort"] != null ? "reasoning_effort=" + payload["reasoning_effort"] + (payload["thinking"] != null ? "; thinking=" + payload["thinking"]!.ToJsonString() : "") :
        payload["reasoning"] != null ? "reasoning=" + payload["reasoning"]!.ToJsonString() : payload["thinking"] != null ? "thinking=" + payload["thinking"]!.ToJsonString() :
        payload["chat_template_kwargs"] != null ? "chat_template_kwargs=" + payload["chat_template_kwargs"]!.ToJsonString() : "Paramètre d’effort omis ; défaut du fournisseur";
}
