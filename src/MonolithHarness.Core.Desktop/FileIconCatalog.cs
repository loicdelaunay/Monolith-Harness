namespace MonolithHarness.Core;

/// <summary>Vendored Material Icon Theme SVGs and matching native color-font glyphs.</summary>
public static class FileIconCatalog
{
    static readonly string[] names = ["c", "cpp", "csharp", "css", "dart", "document", "go", "godot", "html", "java", "javascript", "json", "kotlin", "markdown", "powershell", "python", "rust", "typescript", "vue", "xml", "yaml"];
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> images = new();
    public static string NameFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".cs" or ".csproj" or ".sln" or ".slnx" => "csharp",
        ".ts" or ".tsx" => "typescript", ".js" or ".jsx" or ".mjs" or ".cjs" => "javascript",
        ".py" or ".pyi" => "python", ".json" or ".jsonc" => "json",
        ".html" or ".htm" or ".razor" => "html", ".xml" or ".xaml" or ".svg" or ".props" or ".targets" => "xml",
        ".css" or ".scss" or ".sass" or ".less" => "css", ".md" or ".markdown" => "markdown",
        ".ps1" or ".psm1" or ".psd1" or ".sh" or ".bat" or ".cmd" => "powershell",
        ".dart" => "dart", ".rs" => "rust", ".go" => "go", ".java" => "java",
        ".kt" or ".kts" => "kotlin", ".gd" or ".gdshader" or ".tscn" or ".tres" => "godot",
        ".yaml" or ".yml" => "yaml", ".c" or ".h" => "c", ".cpp" or ".hpp" or ".cc" => "cpp",
        ".vue" => "vue", _ => "document"
    };
    public static string Glyph(string extension) => char.ConvertFromUtf32(0xE100 + Array.IndexOf(names, NameFor(extension)));
    public static string DataUrl(string extension) => images.GetOrAdd(NameFor(extension), name =>
    {
        using var stream = typeof(FileIconCatalog).Assembly.GetManifestResourceStream($"MonolithHarness.FileIcons.{name}.svg")
            ?? throw new InvalidOperationException("Bundled file icon is missing.");
        using var data = new MemoryStream(); stream.CopyTo(data);
        return "data:image/svg+xml;base64," + Convert.ToBase64String(data.ToArray());
    });
}
