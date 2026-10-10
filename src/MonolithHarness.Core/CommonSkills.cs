using System.Text.RegularExpressions;
namespace MonolithHarness.Core;

public record SkillDefinition(string Id, string FrenchName, string EnglishName, string FrenchDescription, string EnglishDescription, string Instruction);

// Host-independent instructions. Tool-backed skills stay in their platform adapters.
public static class CommonSkills
{
    public static SkillDefinition Review { get; } = new("review", "Revue de code", "Code review", "Examiner les bugs, la sécurité et les cas limites ; proposer des corrections.", "Examine bugs, security and edge cases; suggest fixes.", "When reviewing code, prioritize concrete bugs, security issues and edge cases. Explain impact, cite locations and suggest targeted fixes. Do not invent findings.");
    public static SkillDefinition Planning { get; } = new("planning", "Planification", "Planning", "Décomposer les demandes complexes en étapes et critères de validation.", "Break complex requests into steps and validation criteria.", "For complex tasks, propose a concise actionable plan, identify dependencies and define validation criteria. Keep simple answers direct.");
    public static SkillDefinition Summary { get; } = new("summary", "Synthèse", "Summarization", "Résumer les documents en conservant faits, décisions et questions ouvertes.", "Summarize documents while retaining facts, decisions and open questions.", "When summarizing, preserve key facts and decisions, identify open questions, and do not add unsupported information.");
    public static IReadOnlyList<SkillDefinition> PromptOnly { get; } = [Summary, Planning, Review];
    public sealed record SlashToken(int Start, int Length, string Prefix);
    public static SlashToken? CompletionAt(string text, int caret, int selectionLength)
    {
        if(selectionLength!=0 || caret<0 || caret>text.Length)return null;
        static bool Name(char c)=>char.IsLetterOrDigit(c)||c is '_' or '-' or '.';
        var start=caret;while(start>0 && Name(text[start-1]))start--;
        if(start==0 || text[start-1]!='/')return null;start--;
        if(start>0 && !char.IsWhiteSpace(text[start-1]))return null;
        var end=caret;while(end<text.Length && Name(text[end]))end++;
        if(end<text.Length && (text[end] is '/' or '\\' || !char.IsWhiteSpace(text[end]) && !char.IsPunctuation(text[end])))return null;
        return new(start,end-start,text[(start+1)..caret]);
    }
    public static IReadOnlyList<SkillDefinition> Requested(string text,IEnumerable<SkillDefinition> available)
    {
        var names=Regex.Matches(text,@"(?<!\S)/([\w.-]+)(?=\s|$|[,;:!?()\[\]{}])",RegexOptions.None,TimeSpan.FromSeconds(1))
            .Select(x=>x.Groups[1].Value.TrimEnd('.')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return available.Where(x=>names.Contains(x.Id)).ToArray();
    }
}
