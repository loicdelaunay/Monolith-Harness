using System.Text.Json.Nodes;
using MonolithHarness.Core;

static class ReasoningModeChecks
{
    public static void Run(Action<bool, string> check)
    {
        const string glm = "Compatibilité : GLM utilise le raisonnement activé ; son intensité est gérée par le modèle.";
        check(ReasoningSelection.WithoutTrace("Réflexion · demandée : Auto · envoyée : Auto · repli : aucun\ngenerationConfig.thinkingConfig={\"includeThoughts\":true}\n" + glm) == glm
            && ReasoningSelection.WithoutTrace("Thinking · requested: High · sent: Auto · fallback: none\nreasoning_effort=high").Length == 0
            && ReasoningSelection.WithoutTrace("Text fallback") == "Text fallback",
            "Réflexion : traces enregistrées retirées de l’affichage, notices de compatibilité conservées");

        var provider = new Provider { BaseUrl = "https://llm.example/v1", Model = "custom-model" };
        check(!ReasoningCapabilities.For(provider).Known && ReasoningCapabilities.For(provider).Levels.SequenceEqual(["auto"]), "Réflexion : modèle inconnu limité à Auto en découverte automatique");
        ModelContexts.SetReasoningPreference(provider, "custom-model", "manual", ["low", "high", "auto", "bogus"]);
        var manual = ReasoningCapabilities.For(provider);
        check(manual.Source == "Manuel" && manual.Levels.SequenceEqual(["auto", "low", "high"]) && ReasoningCapabilities.Select(provider, "high").Sent == "high",
            "Réflexion : niveaux manuels proposés et envoyés");
        ModelContexts.SetReasoningPreference(provider, "custom-model", "default", []);
        check(ReasoningCapabilities.For(provider).Levels.SequenceEqual(["auto"]) && ReasoningCapabilities.Select(provider, "high").Sent == "auto",
            "Réflexion : mode par défaut sans paramètre de réflexion");
        ModelContexts.SetReasoningPreference(provider, "custom-model", "auto", []);
        check(ModelContexts.ReasoningPreference(provider, "custom-model").Mode == "auto" && !ReasoningCapabilities.For(provider).Known, "Réflexion : retour à la découverte automatique");

        var gateway = new Provider { BaseUrl = "https://openrouter.ai/api/v1", Model = "openai/gpt-5.1" };
        var inferred = ReasoningCapabilities.For(gateway);
        check(inferred.Supports("none") && inferred.Supports("high") && inferred.Source.StartsWith("Déduit de l’identifiant", StringComparison.Ordinal),
            "Réflexion : famille de modèle reconnue derrière une passerelle");
        var listed = new Provider { BaseUrl = "https://gateway.example/v1", Model = "vendor/thinker" };
        ReasoningCapabilities.Capture(listed, "vendor/thinker", new JsonObject { ["supported_parameters"] = new JsonArray("tools", "reasoning") });
        ReasoningCapabilities.Capture(listed, "vendor/plain", new JsonObject { ["supported_parameters"] = new JsonArray("tools") });
        check(ReasoningCapabilities.For(listed).Levels.SequenceEqual(["auto", "none", "low", "medium", "high"]) && ReasoningCapabilities.For(listed, "vendor/plain").NonReasoning,
            "Réflexion : paramètres déclarés par la passerelle détectés à l’import des modèles");
        ModelContexts.SetReasoningPreference(listed, "vendor/thinker", "manual", ["max"]);
        check(ReasoningCapabilities.Detected(listed).Supports("medium") && ReasoningCapabilities.For(listed).Levels.SequenceEqual(["auto", "max"]),
            "Réflexion : la découverte reste conservée sous un choix manuel");
        check(ReasoningCapabilities.For(new Provider { BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai/", Model = "gemini-5-pro" }).Supports("high"),
            "Réflexion : nouvelles générations Gemini Pro reconnues");
    }
}
