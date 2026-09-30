using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using System.Diagnostics;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    string ResolveChatDiskPath(string requested, Project? messageProject)
    {
        var path = LocalFileLinks.WithoutLocation(requested.Trim());
        if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile) path = uri.LocalPath;
        if (path.StartsWith("~/", StringComparison.Ordinal))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        if (Path.IsPathFullyQualified(path))
        {
            var full = Path.GetFullPath(path);
            if (File.Exists(full) || Directory.Exists(full)) return full;
            throw new FileNotFoundException(T("Chemin introuvable sur le disque : ") + full);
        }

        // Manual OS actions resolve the message's roots, including generated files.
        // Reading in the embedded browser retains LocalPreview's separate access checks.
        var sources = new SourceAccess(messageProject?.GetSourceFolders() ?? []);
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        var candidates = new List<string>();
        foreach (var source in sources.Aliases)
        {
            var relative = normalized;
            if (string.Equals(relative, source.Key, PlatformSupport.PathComparison)) relative = ".";
            else if (relative.StartsWith(source.Key + Path.DirectorySeparatorChar, PlatformSupport.PathComparison)) relative = relative[(source.Key.Length + 1)..];
            else if (sources.Aliases.Keys.Any(alias => string.Equals(normalized, alias, PlatformSupport.PathComparison) || normalized.StartsWith(alias + Path.DirectorySeparatorChar, PlatformSupport.PathComparison))) continue;
            var candidate = File.Exists(source.Value) && (relative == "." || relative == Path.GetFileName(source.Value))
                ? source.Value : Path.GetFullPath(Path.Combine(source.Value, relative));
            if (File.Exists(candidate) || Directory.Exists(candidate)) candidates.Add(candidate);
        }
        var matches = candidates.Distinct(PlatformSupport.PathComparer).ToArray();
        if (matches.Length == 1) return matches[0];
        if (matches.Length > 1) throw new InvalidOperationException(T("Chemin ambigu : indiquez le dossier source ou un chemin absolu.") + "\n" + string.Join('\n', matches));
        throw new FileNotFoundException(T("Chemin introuvable sur le disque : ") + requested);
    }

    MenuFlyout CreateChatFileMenu(string requested, Project? messageProject)
    {
        var menu = new MenuFlyout();
        string? resolved = null;
        string? error = null;
        try { resolved = ResolveChatDiskPath(requested, messageProject); }
        catch (Exception ex) { error = ex.Message; ShowStatus(error, StatusKind.Error); }
        void Add(string label, string glyph, Func<string, Task> action)
        {
            var item = new MenuFlyoutItem { Text = T(label), Icon = new FontIcon { Glyph = glyph }, IsEnabled = resolved != null };
            ToolTipService.SetToolTip(item, resolved ?? error ?? requested);
            item.Click += async (_, _) =>
            {
                if (resolved == null) return;
                try { await action(resolved); }
                catch (Exception ex) { ShowStatus(ex.Message, StatusKind.Error); }
            };
            menu.Items.Add(item);
        }
        Add("Ouvrir", "\uE8A7", path => OpenChatDiskItemAsync(path, false));
        Add("Ouvrir dans le navigateur", "\uE774", path => OpenChatFileAsync(path, messageProject));
        Add("Ouvrir le dossier du fichier", "\uE8B7", path => OpenChatDiskItemAsync(path, true));
        return menu;
    }

    static Task OpenChatDiskItemAsync(string path, bool containingFolder)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException(T("Chemin introuvable sur le disque : ") + path);
        var target = containingFolder && !Directory.Exists(path) ? Path.GetDirectoryName(path)! : path;
        Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        return Task.CompletedTask;
    }
}
