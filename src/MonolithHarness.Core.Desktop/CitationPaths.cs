using System.Text.Json;

namespace MonolithHarness.Core;

/// <summary>Display-only recovery of missing file citations, never used to authorize tools.</summary>
public static class CitationPaths
{
    public static string Prompt(IEnumerable<string> roots)
    {
        var aliases = new SourceAccess(roots).Aliases;
        return aliases.Count == 0 ? "" : "\nATTACHED SOURCE LOCATIONS (JSON data): " + JsonSerializer.Serialize(aliases) +
            "\nCite exact tool-returned paths or paths relative to these aliases. The project title is not a filesystem path. Never reconstruct an absolute path from the project title.";
    }
    public static string? RecoverMissingAbsolute(string missing, SourceAccess sources)
    {
        var matches = new HashSet<string>(PlatformSupport.PathComparer);
        foreach (var source in sources.Aliases)
        {
            if (File.Exists(source.Value))
            {
                if (Path.GetFileName(missing).Equals(Path.GetFileName(source.Value), PlatformSupport.PathComparison))
                    matches.Add(source.Value);
                continue;
            }
            var parent = Path.GetDirectoryName(source.Value.TrimEnd(Path.DirectorySeparatorChar));
            if (parent == null) continue;
            var sibling = Path.GetRelativePath(parent, missing);
            if (Path.IsPathRooted(sibling) || sibling == ".." || sibling.StartsWith(".." + Path.DirectorySeparatorChar, PlatformSupport.PathComparison)) continue;
            var split = sibling.IndexOf(Path.DirectorySeparatorChar);
            if (split < 1 || split == sibling.Length - 1) continue;
            // Correct only a missing sibling project root and preserve the exact file tail.
            // Resolve through the attached source to reject traversal and linked directories.
            try
            {
                var candidate = sources.Resolve(source.Key + "/" + sibling[(split + 1)..]);
                if (File.Exists(candidate)) matches.Add(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { }
        }
        return matches.Count switch { 0 => null, 1 => matches.Single(), _ => throw new InvalidOperationException("Référence ambiguë : plusieurs fichiers joints correspondent à ce chemin.") };
    }
}
