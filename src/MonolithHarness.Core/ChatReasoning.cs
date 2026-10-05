using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

// Off is a request policy, not a UI preference or a hidden reasoning display.
public static class ChatReasoning
{
    public static InvalidOperationException Unsupported(string model, string detail = "") => new(
        "Mode Chat : le raisonnement ne peut pas être désactivé pour « " + model + " ». Choisissez un autre niveau de réflexion ou un modèle sans raisonnement. / Chat mode: reasoning cannot be disabled for this model. Choose another thinking level or a non-reasoning model." +
        (detail.Length == 0 ? "" : "\n" + detail));

    public static void CheckModel(Provider provider)
    {
        var id = provider.Model.ToLowerInvariant();
        // These families require thinking; never substitute a low/minimal effort for off.
        if (id.Contains("glm-5.3") || id.Contains("gemini-3") || id.Contains("gemini-2.5-pro")) throw Unsupported(provider.Model);
    }

    public static void Disable(Provider provider, JsonObject payload)
    {
        var preset = ProviderPresets.Resolve(provider.BaseUrl)?.Id;
        payload["reasoning_effort"] = "none";
        if (preset == "openrouter")
        {
            payload.Remove("reasoning_effort");
            payload["reasoning"] = new JsonObject { ["enabled"] = false, ["effort"] = "none" };
        }
        else if (preset == "zai" || provider.Kind == "deepseek" || Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var url) && url.Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase))
        {
            payload.Remove("reasoning_effort");
            payload["thinking"] = new JsonObject { ["type"] = "disabled" };
        }
        if (provider.IsLocal)
        {
            payload["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
        }
        // Keep persisted history intact; previous Agent thoughts are not needed by Chat.
        foreach (var message in payload["messages"]!.AsArray().OfType<JsonObject>())
        {
            message.Remove("reasoning_content"); message.Remove("reasoning"); message.Remove("reasoning_details");
        }
    }

    public static bool ExplicitlyDisabled(JsonNode? options) => options is JsonObject obj &&
        (obj["reasoningEffort"]?.ToString() == "none" || obj["reasoning_effort"]?.ToString() == "none" ||
         obj["thinking"]?["type"]?.ToString() == "disabled" || IsFalse(obj["reasoning"]?["enabled"]) ||
         IsFalse(obj["enableThinking"]) || IsFalse(obj["think"]) || IsFalse(obj["enable_thinking"]));
    public static bool IsFalse(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var enabled) && !enabled;
}
