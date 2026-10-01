using System.Globalization;

namespace MonolithHarness.Core;

public enum ContextRequestStage { Preparing, Sending, AwaitingOutput }

/// <summary>Observable client request stages; never asserts server prefill or cache progress.</summary>
public sealed record ContextRequestProgress(ContextRequestStage Stage, int EstimatedTokens, int MessageCount, long? PayloadBytes = null)
{
    public string Caption(string language, double elapsedSeconds)
    {
        var culture = Culture(language);
        return Label(language) +
            " · ≈ " + EstimatedTokens.ToString("N0", culture) + " tokens · " + ToolActivity.Duration(elapsedSeconds);
    }
    public string Label(string language) => Text(language, "Chargement du modèle", "Loading model", "Modell wird geladen", "Cargando modelo") + " · " + StageDescription(language);
    public string StageDescription(string language) => Stage switch
    {
        ContextRequestStage.Preparing => Text(language, "Préparation", "Preparation", "Vorbereitung", "Preparación"),
        ContextRequestStage.Sending => Text(language, "Envoi", "Sending", "Senden", "Envío"),
        _ => Text(language, "Attente des premiers tokens", "Waiting for first tokens", "Warten auf erste Tokens", "Esperando los primeros tokens")
    };
    public string Details(string language, int contextLimit, double elapsedSeconds)
    {
        bool english = language == "en";
        var culture = Culture(language);
        var size = PayloadBytes is { } bytes ? "\n" + (english ? "Request size: " : "Taille de la requête : ") +
            (bytes >= 1024 * 1024 ? (bytes / (1024d * 1024)).ToString("0.#", culture) + (english ? " MB" : " Mo") : (bytes / 1024d).ToString("0.#", culture) + (english ? " KB" : " Ko")) : "";
        return (english ? "Stage: " : "Phase : ") + StageDescription(language) +
            "\n" + (english ? "Estimated context: ≈ " : "Contexte estimé : ≈ ") + EstimatedTokens.ToString("N0", culture) +
            " / " + contextLimit.ToString("N0", culture) + " tokens" +
            "\n" + MessageCount.ToString("N0", culture) + (english ? " messages in the request, including instructions and tool results." : " messages dans la requête, instructions et résultats d’outils compris.") + size +
            "\n" + (english ? "Elapsed wait: " : "Attente écoulée : ") + ToolActivity.Duration(elapsedSeconds) +
            "\n\n" + (english ? "These are the request stages, not evidence of a model restart. The wait can include context processing or the provider queue. The API does not report internal progress. Reused tokens are shown after the response when reported by the provider. The estimated context includes tool definitions." : "Ces phases décrivent la requête, sans indiquer un redémarrage du modèle. L’attente peut inclure le traitement du contexte ou la file d’attente du fournisseur. L’API ne fournit pas de progression interne. Les tokens réutilisés sont affichés après la réponse si le fournisseur les communique. Le contexte estimé inclut les définitions d’outils.");
    }
    static CultureInfo Culture(string language) => CultureInfo.GetCultureInfo(language switch { "en" => "en-US", "de" => "de-DE", "es" => "es-ES", _ => "fr-FR" });
    static string Text(string language, string fr, string en, string de, string es) => language switch { "en" => en, "de" => de, "es" => es, _ => fr };
}
