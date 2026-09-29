using System.Text.RegularExpressions;

namespace OhMyHarness.Core;

/// <summary>Only summarizes reasoning explicitly supplied by the provider.</summary>
public static class ModelActivity
{
    public static string ReasoningLine(string? reasoning)
    {
        if (string.IsNullOrWhiteSpace(reasoning)) return "";
        var tail = reasoning.Length > 1600 ? reasoning[^1600..] : reasoning;
        var lines = tail.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var line = lines.LastOrDefault(x => x.Length > 6 && !x.StartsWith("```")) ?? lines.LastOrDefault() ?? "";
        line = Regex.Replace(line, @"^[\s#>*\-]+", "").Replace("**", "").Replace("`", "");
        line = Regex.Replace(line, @"\s+", " ").Trim();
        if (line.Length <= 140) return line;
        var stop = line.LastIndexOf(' ', 137, 45);
        return line[..(stop > 90 ? stop : 137)] + "…";
    }
    public static int IdleSeconds(DateTimeOffset lastProgress, DateTimeOffset now) => Math.Max(0, (int)(now - lastProgress).TotalSeconds);
}
