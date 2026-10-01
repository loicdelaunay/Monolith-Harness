using System.Globalization;

namespace MonolithHarness.Core;

public enum ContextRequestStage { Preparing, Sending, AwaitingOutput }

/// <summary>Observable client request stages; never asserts server prefill or cache progress.</summary>
public sealed record ContextRequestProgress(ContextRequestStage Stage, int EstimatedTokens, int MessageCount, long? PayloadBytes = null)
{
    public string Caption(string language, double elapsedSeconds)
    {
        var culture = CultureInfo.GetCultureInfo(language == "en" ? "en-US" : "fr-FR");
        return (language == "en" ? "Preloading context" : "Préchargement du contexte") +
            " · ≈ " + EstimatedTokens.ToString("N0", culture) + " tokens · " + ToolActivity.Duration(elapsedSeconds);
    }
    public string StageDescription(string language) => language == "en" ? Stage switch
    {
        ContextRequestStage.Preparing => "Preparing the request",
        ContextRequestStage.Sending => "Sending / connecting to the provider",
        _ => "Waiting for the first output tokens"
    } : Stage switch
    {
        ContextRequestStage.Preparing => "Préparation de la requête",
        ContextRequestStage.Sending => "Envoi / connexion au fournisseur",
        _ => "Attente des premiers tokens de sortie"
    };
    public string Details(string language, int contextLimit, double elapsedSeconds)
    {
        bool english = language == "en";
        var culture = CultureInfo.GetCultureInfo(english ? "en-US" : "fr-FR");
        var size = PayloadBytes is { } bytes ? "\n" + (english ? "Request size: " : "Taille de la requête : ") +
            (bytes >= 1024 * 1024 ? (bytes / (1024d * 1024)).ToString("0.#", culture) + (english ? " MB" : " Mo") : (bytes / 1024d).ToString("0.#", culture) + (english ? " KB" : " Ko")) : "";
        return (english ? "Stage: " : "Phase : ") + StageDescription(language) +
            "\n" + (english ? "Estimated context: ≈ " : "Contexte estimé : ≈ ") + EstimatedTokens.ToString("N0", culture) +
            " / " + contextLimit.ToString("N0", culture) + " tokens" +
            "\n" + MessageCount.ToString("N0", culture) + (english ? " messages in the request, including instructions and tool results." : " messages dans la requête, instructions et résultats d’outils compris.") + size +
            "\n" + (english ? "Elapsed wait: " : "Attente écoulée : ") + ToolActivity.Duration(elapsedSeconds) +
            "\n\n" + (english ? "The wait can include context processing or the provider queue. The API does not report internal progress or current cache state. Token count also includes tool definitions and is an estimate." : "L’attente peut inclure le traitement du contexte ou la file d’attente du fournisseur. L’API ne fournit pas de progression interne ni l’état actuel du cache. Le nombre de tokens inclut aussi les définitions d’outils et reste une estimation.");
    }
}
