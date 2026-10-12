using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public interface ICompletionRecoverySettings { bool RecoverPrematureCompletion { get; } }

/// <summary>Observed provider/transport information, separate from text and tool calls sent to a model.</summary>
public sealed record GenerationTermination
{
    public const string MetadataKey = "_monolith_termination";
    public string Kind { get; init; } = "unspecified";
    public string ProviderReason { get; init; } = "";
    public bool? StreamCompleted { get; init; }
    public string Detail { get; init; } = "";
    public string ErrorType { get; init; } = "";
    public int? HttpStatus { get; init; }
    public int ResumeAttempts { get; init; }
    public int RetryAttempts { get; init; }
    public List<GenerationTermination> PreviousEndings { get; init; } = [];
    public DateTime ObservedUtc { get; init; } = DateTime.UtcNow;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool CanResume => Kind is "length" or "empty";

    public static GenerationTermination Observe(string? reason, bool completed, string text, bool hasCalls)
    {
        reason = reason?.Trim() ?? "";
        if (reason.Length > 100) reason = reason[..100];
        var kind = reason.ToLowerInvariant() switch
        {
            "length" or "max_tokens" or "max_output_tokens" => "length",
            "content_filter" or "safety" or "blocked" or "refusal" => "blocked",
            _ when !completed => "transport_interrupted",
            _ when hasCalls => "tool_calls",
            _ when string.IsNullOrWhiteSpace(text) => "empty",
            "stop" or "end_turn" => "stop",
            _ => "unspecified"
        };
        return new() { Kind = kind, ProviderReason = reason, StreamCompleted = completed };
    }

    public string Describe(bool french) => Kind switch
    {
        "stop" => french ? "Fin normale annoncée par le fournisseur. Sa décision interne n’est pas communiquée." : "Normal ending reported by the provider. Its internal decision is not supplied.",
        "length" => french ? "Limite de sortie du modèle atteinte : la réponse peut être tronquée." : "Model output limit reached: the response may be truncated.",
        "empty" => french ? "Le fournisseur a terminé sans réponse visible ni appel d’outil : fin prématurée possible." : "The provider ended without visible text or tool calls: a premature ending is possible.",
        "blocked" => french ? "Réponse arrêtée par le filtre du fournisseur." : "Response stopped by the provider filter.",
        "tool_calls" => french ? "Le modèle a demandé un ou plusieurs outils." : "The model requested one or more tools.",
        "transport_interrupted" => french ? "Connexion ou flux interrompu avant le marqueur de fin." : "Connection or stream interrupted before the end marker.",
        "cancelled" => french ? "Génération annulée par l’utilisateur ou par l’application." : "Generation cancelled by the user or application.",
        "timeout" => french ? "Délai de la requête dépassé." : "Request timed out.",
        "internal_cancelled" => french ? "Requête annulée sans annulation du chat ; le détail indique la cause observée." : "Request cancelled without chat cancellation; details contain the observed cause.",
        "error" => french ? "Une erreur a interrompu la génération." : "An error interrupted generation.",
        "step_limit" => french ? "Limite d’étapes de l’agent atteinte ; continuation automatique désactivée." : "Agent step limit reached; automatic continuation disabled.",
        _ => french ? "Motif non communiqué par le fournisseur ou non enregistré pour cet ancien message." : "Reason not supplied by the provider, or not recorded for this older message."
    };

    public static GenerationTermination Failure(Exception error, GenerationTermination? observed = null, string? secret = null, bool cancelled = false)
    {
        var detail = error.Message;
        if (!string.IsNullOrEmpty(secret)) detail = detail.Replace(secret, "[redacted]", StringComparison.Ordinal);
        if (detail.Length > 1200) detail = detail[..1200];
        var kind = error is OperationCanceledException ? cancelled ? "cancelled" : error.InnerException is TimeoutException ? "timeout" : "internal_cancelled"
            : error is IOException ? "transport_interrupted" : "error";
        return (observed ?? new()) with { Kind = kind, Detail = detail, ErrorType = error.GetType().FullName ?? error.GetType().Name,
            HttpStatus = error is HttpRequestException { StatusCode: { } status } ? (int)status : null,
            StreamCompleted = observed?.StreamCompleted ?? false };
    }

    public static GenerationTermination? Read(Message message)
    {
        try { return JsonNode.Parse(message.WireJson)?[MetadataKey]?.Deserialize<GenerationTermination>(); }
        catch (JsonException) { return null; }
    }

    public static GenerationTermination? Latest(Message message, GenerationUpdate? progress)
    {
        var stored = Read(message);
        return stored?.StreamCompleted == true ? stored : progress?.Termination ?? stored;
    }

    public static void Save(Message message, GenerationTermination termination)
    {
        JsonObject? wire = null;
        try { if (message.WireJson.Length > 0) wire = JsonNode.Parse(message.WireJson) as JsonObject; } catch (JsonException) { }
        wire ??= new() { ["role"] = message.Role, ["content"] = message.Content };
        wire["content"] = message.Content;
        wire[MetadataKey] = JsonSerializer.SerializeToNode(termination);
        message.WireJson = wire.ToJsonString();
    }
}
