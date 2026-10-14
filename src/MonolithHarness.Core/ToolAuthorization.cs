using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MonolithHarness.Core;

public sealed record ToolApprovalEvidence(bool Allowed, string Method, string Mode, string Scope, string Action,
    string? ProjectRule, DateTimeOffset TimeUtc, string? Validator = null, string? Classification = null,
    double? Score = null, string? Reason = null);

/// <summary>Local evidence only; never an authorization capability or model instruction.</summary>
public static class ToolAuthorization
{
    public const string MetadataKey = "_monolith_authorization";
    public static ToolApprovalEvidence[] Read(Message message)
    {
        try { return JsonNode.Parse(message.WireJson)?[MetadataKey]?.Deserialize<ToolApprovalEvidence[]>() ?? []; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException) { return []; }
    }
    public static void Save(Message message, IReadOnlyList<ToolApprovalEvidence> evidence)
    {
        if (evidence.Count == 0) return;
        var wire = JsonNode.Parse(message.WireJson) as JsonObject ?? new() { ["role"] = message.Role, ["content"] = message.Content };
        wire[MetadataKey] = JsonSerializer.SerializeToNode(evidence); message.WireJson = wire.ToJsonString();
    }
    // Bound local diagnostic fields and hide common credentials without storing tool arguments.
    public static string Clean(string? value, int maxLength = 2048)
    {
        if (string.IsNullOrEmpty(value)) return "";
        value = value[..Math.Min(value.Length, 12000)];
        try
        {
            value = Regex.Replace(value, @"(?i)\b(?:sk-|sk_|hf_|gh[pousr]_|github_pat_)[a-z0-9_\-]{12,}", "[redacted]", RegexOptions.None, TimeSpan.FromMilliseconds(50));
            value = Regex.Replace(value, @"(?i)(bearer\s+)[a-z0-9._~+/-]{8,}", "$1[redacted]", RegexOptions.None, TimeSpan.FromMilliseconds(50));
            value = Regex.Replace(value, """(?i)(\b(?:api[_-]?key|token|secret|password|authorization)\b["']?\s*[:=]\s*["']?)[^\s,;&'"\r\n]+""", "$1[redacted]", RegexOptions.None, TimeSpan.FromMilliseconds(50));
        }
        catch (RegexMatchTimeoutException) { return "[redacted]"; }
        return value[..Math.Min(maxLength, value.Length)];
    }
}
