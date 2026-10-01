using System.Net.Http;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public sealed record CloudProviderPreset(string Id, string Name, string BaseUrl, string DescriptionFr, string DescriptionEn, string DocumentationUrl, string KeysUrl)
{
    public bool ImageGeneration { get; init; }
    public string DefaultModel { get; init; } = "";
    public IReadOnlyList<string> SuggestedChatModels { get; init; } = [];
    public IReadOnlyList<string> SuggestedImageModels { get; init; } = [];
}

/// <summary>Shared GUI/CLI presets. Account keys, quotas and selected models remain specific to each connection.</summary>
public static class ProviderPresets
{
    public static IReadOnlyList<CloudProviderPreset> Cloud { get; } = Array.AsReadOnly(new[]
    {
        new CloudProviderPreset("openrouter", "OpenRouter", "https://openrouter.ai/api/v1",
            "Modèles gratuits et payants avec une seule clé API. Les modèles gratuits sont soumis à des quotas.",
            "Free and paid models with one API key. Free models are subject to quotas.",
            "https://openrouter.ai/docs/quickstart", "https://openrouter.ai/settings/keys"),
        new CloudProviderPreset("groq", "Groq", "https://api.groq.com/openai/v1",
            "Hébergement de modèles ouverts : accès gratuit limité ou offre payante.",
            "Hosted open models: limited free access or paid usage.",
            "https://console.groq.com/docs/openai", "https://console.groq.com/keys"),
        new CloudProviderPreset("gemini", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai",
            "Texte, vision et raisonnement. Certains modèles disposent d’un quota gratuit ; les modèles d’image ont leurs propres conditions.",
            "Text, vision and reasoning. Some models have a free quota; image models have their own conditions.",
            "https://ai.google.dev/gemini-api/docs/openai", "https://aistudio.google.com/apikey") { ImageGeneration = true },
        new CloudProviderPreset("mistral", "Mistral", "https://api.mistral.ai/v1",
            "Accès direct aux modèles Mistral, avec un mode gratuit limité et des offres payantes.",
            "Direct access to Mistral models, with limited free access and paid plans.",
            "https://docs.mistral.ai/getting-started/quickstarts/studio/activate-and-generate-api-key", "https://console.mistral.ai"),
        new CloudProviderPreset("zai", "Z.ai", "https://api.z.ai/api/paas/v4",
            "Certains GLM Flash sont gratuits ; les autres modèles et les images sont payants. Utilisez une clé API de la plateforme. Les exemples de modèles ne vérifient pas les droits de votre compte.",
            "Some GLM Flash models are free; other models and images are paid. Use a platform API key. Model examples do not verify your account's access.",
            "https://docs.z.ai/api-reference/introduction", "https://z.ai/manage-apikey/apikey-list")
        {
            ImageGeneration = true, DefaultModel = "glm-4.7-flash",
            SuggestedChatModels = ["glm-4.7-flash", "glm-4.6v-flash", "glm-5.3-flash", "glm-5.3"],
            SuggestedImageModels = ["cogview-4-250304", "glm-image"]
        },
        new CloudProviderPreset("nvidia", "NVIDIA NIM", "https://integrate.api.nvidia.com/v1",
            "Catalogue NVIDIA NIM avec accès gratuit pour le prototypage, selon les conditions et limites du service.",
            "NVIDIA NIM catalogue with free prototyping access, subject to the service's terms and limits.",
            "https://docs.api.nvidia.com/nim/re/docs/api-quickstart", "https://build.nvidia.com")
    });

    public static CloudProviderPreset? Find(string? id) => Cloud.FirstOrDefault(x => x.Id == id);
    public static CloudProviderPreset? Resolve(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint)) return null;
        return Cloud.FirstOrDefault(preset =>
        {
            var expected = new Uri(preset.BaseUrl);
            return endpoint.Host.Equals(expected.Host, StringComparison.OrdinalIgnoreCase)
                && endpoint.Port == expected.Port
                && endpoint.AbsolutePath.TrimEnd('/').Equals(expected.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        });
    }

    public static bool CanGenerateImages(Provider provider) => provider.IsLocal || (!provider.IsComposite && !provider.IsOpenCode && (Resolve(provider.BaseUrl)?.ImageGeneration ?? true));

    public static void ConfigureRequest(Provider provider, JsonObject payload, HttpRequestMessage request)
    {
        var id = Resolve(provider.BaseUrl)?.Id;
        if (id is "groq" or "mistral" or "zai" or "nvidia")
            foreach (var message in payload["messages"]!.AsArray().OfType<JsonObject>())
            {
                // Keep stored history intact when changing provider; these signatures belong to another API.
                message.Remove("reasoning_details"); message.Remove("extra_content");
                if (message["tool_calls"] is JsonArray calls)
                    foreach (var call in calls.OfType<JsonObject>()) call.Remove("extra_content");
                if (id == "mistral" && message["role"]?.ToString() == "assistant" && message.Remove("reasoning_content", out var trace))
                {
                    var content = new JsonArray();
                    if (!string.IsNullOrEmpty(trace?.ToString())) content.Add(new JsonObject { ["type"] = "thinking", ["thinking"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = trace!.ToString() }) });
                    if (message["content"] is JsonArray parts) foreach (var part in parts) content.Add(part?.DeepClone());
                    else if (message["content"]?.ToString() is { Length: > 0 } text) content.Add(new JsonObject { ["type"] = "text", ["text"] = text });
                    if (content.Count > 0) message["content"] = content;
                }
            }
        switch (id)
        {
            case "openrouter":
                request.Headers.TryAddWithoutValidation("X-Title", "Monolith Harness");
                if (payload.Remove("reasoning_effort", out var effort)) payload["reasoning"] = new JsonObject { ["effort"] = effort };
                break;
            case "zai":
                // GLM's thinking switch is independent of the OpenAI effort levels.
                if (payload.Remove("reasoning_effort", out var thinking))
                    payload["thinking"] = new JsonObject { ["type"] = thinking?.ToString() == "none" && !provider.Model.StartsWith("glm-5.3", StringComparison.OrdinalIgnoreCase) ? "disabled" : "enabled" };
                break;
        }
    }

    public static string NormalizeModelId(Provider provider, string model) => Resolve(provider.BaseUrl)?.Id == "gemini" && model.StartsWith("models/", StringComparison.Ordinal)
        ? model[7..] : model;

    public static string ReasoningNotice(Provider provider, string? effort)
    {
        if (Resolve(provider.BaseUrl)?.Id != "zai" || string.IsNullOrWhiteSpace(effort) || effort.Equals("auto", StringComparison.OrdinalIgnoreCase)) return "";
        if (effort.Equals("none", StringComparison.OrdinalIgnoreCase)) return provider.Model.StartsWith("glm-5.3", StringComparison.OrdinalIgnoreCase)
            ? "Compatibilité : ce modèle GLM impose le raisonnement ; il reste activé." : "";
        return "Compatibilité : GLM utilise le raisonnement activé ; son intensité est gérée par le modèle.";
    }
}
