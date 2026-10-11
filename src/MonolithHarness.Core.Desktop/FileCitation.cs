using System.Globalization;
using System.Text.RegularExpressions;

namespace MonolithHarness.Core;

/// <summary>A local Markdown reference; line ranges are inclusive and one based.</summary>
public sealed record FileCitation(string Path, int? StartLine = null, int? EndLine = null, int? Column = null)
{
    static readonly Regex location = new(@"(?::(?<start>\d+)(?:-(?<end>\d+)|:(?<column>\d+))?|#L(?<start>\d+)(?:-L?(?<end>\d+))?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public const string Instructions = " FILE CITATIONS: Cite local files using Markdown links, using exact source aliases returned by file tools, or verified absolute paths with forward slashes: [Service.cs](E:/project/Service.cs), [Service.cs](E:/project/Service.cs#L42), or [Service.cs](E:/project/Service.cs#L42-L57). The desktop shows a file badge and opens the cited inclusive, one-based line range. For paths containing spaces, wrap the target in angle brackets, for example [My File.cs](<E:/project/My File.cs#L42-L57>). Cite only files and line numbers supported by inspected content; never invent locations. Keep links outside code fences. Web URLs remain ordinary web links.";

    public string Name => Path.Replace('\\', '/').Split('/')[^1];
    public string Extension => System.IO.Path.GetExtension(Name).ToLowerInvariant();
    public string Suffix => StartLine is { } first ? $"#L{first}" + (EndLine > first ? $"-L{EndLine}" : "") : "";
    public string Target => Path + Suffix;
    public string LineLabel(bool french) => StartLine is not { } first ? "" : EndLine > first
        ? french ? $"lignes {first} à {EndLine}" : $"lines {first}–{EndLine}"
        : $"L.{first}";
    public string Tooltip(bool french) => Path + (StartLine != null ? "\n" + LineLabel(french) : "");
    public string Icon => Extension switch
    {
        ".cs" or ".csproj" or ".sln" or ".slnx" => "C#",
        ".ts" or ".tsx" => "TS", ".js" or ".jsx" or ".mjs" or ".cjs" => "JS",
        ".py" or ".pyi" => "Py", ".json" or ".jsonc" => "{}",
        ".html" or ".htm" or ".xml" or ".xaml" or ".svg" => "</>",
        ".css" or ".scss" or ".sass" or ".less" => "#",
        ".md" or ".markdown" => "M↓", ".ps1" or ".sh" or ".bat" or ".cmd" => ">_",
        ".dart" => "D", ".rs" => "Rs", ".go" => "Go", ".java" or ".kt" or ".kts" => "J",
        _ => "\uE8A5"
    };
    public string IconKind => Extension switch
    {
        ".cs" or ".csproj" or ".sln" or ".slnx" => "csharp",
        ".js" or ".jsx" or ".mjs" or ".cjs" or ".py" or ".pyi" or ".json" or ".jsonc" => "gold",
        ".html" or ".htm" or ".xml" or ".xaml" or ".svg" => "orange", _ => "blue"
    };
    public static FileCitation FromPath(string target)
    {
        var match = location.Match(target);
        if (!match.Success) return new(target);
        int? Read(string name) => int.TryParse(match.Groups[name].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
        var first = Read("start"); var last = Read("end"); var column = Read("column");
        if (first == null || match.Groups["end"].Success && (last == null || last < first) || match.Groups["column"].Success && column == null)
            return new(LocalFileLinks.WithoutLocation(target));
        return new(target[..match.Index], first, last ?? first, column);
    }
    public static FileCitation? FromUrl(string? url)
    {
        var path = LocalFileLinks.PathFromUrl(url);
        if (path == null || path.EndsWith('/') || path.EndsWith('\\')) return null;
        var citation = FromPath(path);
        return citation.Extension.Length > 0 || citation.Name is "README" or "LICENSE" or "Dockerfile" or "Makefile" ? citation : null;
    }
}
