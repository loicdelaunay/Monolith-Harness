using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MonolithHarness.Core;

public static class ToolActivity
{
    public static string Duration(double seconds)
    {
        if (seconds < 10) return $"{Math.Max(.1, seconds):0.#} s";
        var whole = (int)Math.Round(seconds);
        return whole >= 60 ? $"{whole / 60} min {whole % 60} s" : $"{whole} s";
    }
    public static string Detail(string name, string arguments)
    {
        try
        {
            var args = JsonNode.Parse(string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments) as JsonObject;
            if (args == null) return "";
            foreach (var key in new[] { "path", "url", "command", "query", "pattern", "operation", "action", "task", "session_id", "id" })
            {
                if (args[key] is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)) continue;
                if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") text = new UriBuilder(uri) { UserName = "", Password = "" }.Uri.GetLeftPart(UriPartial.Path);
                text = Regex.Replace(text, @"(?i)(api[_-]?key|token|password|secret)\s*[=:]\s*[^\s;]+", "$1=[…]", RegexOptions.None, TimeSpan.FromMilliseconds(50));
                text = Regex.Replace(text, @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(50)).Trim();
                return text.Length <= 120 ? text : text[..117] + "…";
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or RegexMatchTimeoutException) { }
        return "";
    }
    public static bool Failed(string result) => result.StartsWith("Erreur", StringComparison.OrdinalIgnoreCase) || result.StartsWith("Error", StringComparison.OrdinalIgnoreCase) || result.StartsWith("Permission denied", StringComparison.OrdinalIgnoreCase) || result.StartsWith("Accès refusé", StringComparison.OrdinalIgnoreCase);
}
