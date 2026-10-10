using System.Text.RegularExpressions;

namespace MonolithHarness.Core;

public static class SkillInvocation
{
    public sealed record CompletionToken(int Start, int Length, string Prefix, bool AtMessageStart);

    static bool NameCharacter(char value) => char.IsLetterOrDigit(value) || value is '_' or '-' or '.';

    // The caret, rather than the beginning of the draft, identifies the token to complete.
    // Do not interpret URL/path separators or selected prose as slash invocations.
    public static CompletionToken? CompletionAt(string text, int caret, int selectionLength = 0)
    {
        if (selectionLength != 0 || caret < 0 || caret > text.Length) return null;
        var start = caret;
        while (start > 0 && NameCharacter(text[start - 1])) start--;
        if (start == 0 || text[start - 1] != '/') return null;
        start--;
        if (start > 0 && !char.IsWhiteSpace(text[start - 1])) return null;
        var end = caret;
        while (end < text.Length && NameCharacter(text[end])) end++;
        if (end < text.Length && (text[end] is '/' or '\\' || !char.IsWhiteSpace(text[end]) && !char.IsPunctuation(text[end]))) return null;
        return new(start, end - start, text[(start + 1)..caret], string.IsNullOrWhiteSpace(text[..start]));
    }

    public static IReadOnlyList<SkillDefinition> Requested(string text, IEnumerable<string>? roots = null, int projectId = 0)
    {
        var available = Skills.Available(roots, projectId);
        var names = Regex.Matches(text, @"(?<!\S)/([\w.-]+)(?=\s|$|[,;:!?()\[\]{}])")
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names.ToArray())
            if (name.EndsWith('.') && !available.Any(skill => skill.Id.Equals(name, StringComparison.OrdinalIgnoreCase))) names.Add(name.TrimEnd('.'));
        return available.Where(skill => names.Contains(skill.Id)).DistinctBy(s => s.Id).ToArray();
    }
    public static string Instructions(string text, string enabled, IEnumerable<string>? roots = null, int projectId = 0)
    {
        var requested = Requested(text, roots, projectId);
        if (requested.Count == 0) return "";
        return "\nEXPLICIT USER SKILL REQUESTS: The user invoked the following slash skills. Prioritize these skills for this task, follow their workflow before implementation, and mention their use. Existing work modes, tool availability and permissions remain authoritative.\n" +
            string.Join("\n", requested.Select(skill => Skills.Enabled(enabled, skill.Id)
                ? "REQUIRED SKILL /" + skill.Id + ": " + skill.Instruction
                : "The requested skill /" + skill.Id + " is disabled. Explain that the user must enable it in Settings / Skills. Do not bypass its disabled tools."));
    }
}
