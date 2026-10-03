using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MonolithHarness.Core;

internal static class ProviderErrorDetails
{
    public static async Task<string> ReadAsync(HttpResponseMessage response, string secret, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        var parts = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var text = Clean(value, secret);
            if (text.Length > 900) text = text[..900] + "…";
            if (text.Length > 0 && seen.Add(text)) parts.Add((label.Length > 0 ? label + " : " : "") + text);
        }
        string? Value(JsonNode? node) => node is JsonValue value ? value.ToString() : null;
        void Read(JsonNode? node, int depth = 0)
        {
            if (depth > 4 || node == null) return;
            if (node is JsonValue) { Add("Détail", Value(node)); return; }
            if (node is JsonArray errors) { foreach (var error in errors.Take(4)) Read(error, depth + 1); return; }
            if (node is not JsonObject data) return;
            Add("", Value(data["message"]));
            Add("Détail", Value(data["detail"]));
            Add("Motif", Value(data["reason"]));
            Add("Code fournisseur", Value(data["code"]));
            Add("Statut fournisseur", Value(data["status"]));
            Read(data["error"], depth + 1); Read(data["errors"], depth + 1);
            if (data["metadata"] is not JsonObject metadata) return;
            Add("Fournisseur", Value(metadata["provider_name"]));
            Add("Code fournisseur", Value(metadata["provider_code"]));
            Add("Limite", Value(metadata["limit_source"]));
            Add("Motif", Value(metadata["reason"]));
            Add("Type", Value(metadata["error_type"]));
            Add("Conseil", Value(metadata["remedy_hint"]));
            if (metadata["raw"] is JsonValue raw)
            {
                var text = raw.ToString();
                try { Read(JsonNode.Parse(text), depth + 1); }
                catch (System.Text.Json.JsonException) { Add("Détail fournisseur", text); }
            }
            else Read(metadata["raw"], depth + 1);
        }
        try { Read(JsonNode.Parse(body)); }
        catch (System.Text.Json.JsonException) { Add("Détail fournisseur", body); }
        if (parts.Count == 0) parts.Add("Aucun détail retourné par le fournisseur.");
        if (response.Headers.RetryAfter is { } retry)
        {
            var seconds = retry.Delta?.TotalSeconds ?? (retry.Date - DateTimeOffset.UtcNow)?.TotalSeconds;
            if (seconds.HasValue) Add("Délai conseillé avant une nouvelle tentative", Math.Ceiling(Math.Max(0, seconds.Value)).ToString(CultureInfo.InvariantCulture) + " s");
        }
        foreach (var name in new[] { "X-RateLimit-Limit", "X-RateLimit-Remaining", "X-RateLimit-Reset" })
            if (response.Headers.TryGetValues(name, out var values)) Add(name, string.Join(", ", values));
        var detail = string.Join("\n", parts);
        return detail.Length > 3500 ? detail[..3500] + "…" : detail;
    }
    static string Clean(string text, string secret)
    {
        // Keep only useful error fields; never expose a response dump, headers or credentials.
        if (secret.Length > 0) text = text.Replace(secret, "[secret]").Replace(System.Text.Json.JsonSerializer.Serialize(secret).Trim('"'), "[secret]");
        if (text.Length > 12000) text = text[..12000];
        text = Regex.Replace(text, @"\b(Bearer\s+)[A-Za-z0-9._~+/=\-]+", "$1[secret]", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text, @"\bsk-(?:or-v1-)?[A-Za-z0-9_\-]{8,}", "[secret]", RegexOptions.None, TimeSpan.FromSeconds(1));
        text = Regex.Replace(text, "(?i)\\b(api[_-]?key|authorization|access[_-]?token|secret)\\s*[:=]\\s*[\"']?[^\"'\\s,;}]+", "$1=[secret]", RegexOptions.None, TimeSpan.FromSeconds(1));
        return new string(text.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray()).Trim();
    }
}
