using System.Collections.Concurrent;
using System.Text.Json;

namespace MonolithHarness.Core;

/// <summary>Async flow-local project routing, including host tools and native agent sessions.</summary>
public static class SubagentWorkspace
{
    sealed record Binding(ConversationSession Owner, Project Project);
    static readonly AsyncLocal<Binding?> current = new();
    public static Project? ProjectFor(ConversationSession owner) => current.Value is { } binding && ReferenceEquals(binding.Owner, owner) ? binding.Project : null;
    public static IDisposable Enter(ConversationSession owner, Project project)
    {
        var previous = current.Value; current.Value = new(owner, project);
        return new Scope(() => current.Value = previous);
    }
    sealed class Scope(Action restore) : IDisposable { public void Dispose() => restore(); }
}

public sealed class SubagentWorktrees
{
    public sealed record Checkout(string Repository, string ParentBranch, string Branch, string Directory, string BaseCommit);
    static readonly ConcurrentDictionary<string, SemaphoreSlim> merges = new(PlatformSupport.PathComparer);
    readonly List<Checkout> checkouts = [];
    readonly string directory;
    public Project Project { get; }
    public string Description => string.Join("\n", checkouts.Select(x => $"Git worktree · {x.Branch} · {x.Directory}"));
    SubagentWorktrees(Project project, string directory) { Project = project; this.directory = directory; }
    static string Slug(string value) => new(value.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').Take(32).ToArray());
    static async Task<string> Git(string root, IEnumerable<string> args, CancellationToken ct)
    {
        var result = await WorkspaceTools.GitAsync(root, args, ct);
        if (!result.StartsWith("Exit code: 0\n", StringComparison.Ordinal)) throw new IOException(result);
        if (result.Contains("[output truncated]")) throw new IOException("Git output exceeded the limit.");
        return result[(result.IndexOf('\n') + 1)..].TrimEnd('\r', '\n');
    }
    static string? FindRepository(string folder)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(folder)); current != null; current = current.Parent)
            if (WorkspaceTools.HasGitRepository(current.FullName)) return current.FullName;
        return null;
    }
    public static async Task<SubagentWorktrees?> CreateAsync(ConversationSession run, string name, CancellationToken ct)
    {
        var roots = run.Project.GetSourceFolders();
        if (!roots.Any(x => FindRepository(x) != null)) return null;
        var token = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(PortableStorage.Folder("worktrees"), "chat-" + run.Chat.Id, "agent-" + Slug(name) + "-" + token[..12]);
        var project = new Project { Id = run.Project.Id, Name = run.Project.Name, IsInbox = run.Project.IsInbox,
            Icon = run.Project.Icon, Color = run.Project.Color, PermissionProfileJson = run.Project.PermissionProfileJson };
        var workspace = new SubagentWorktrees(project, directory);
        var repositories = new Dictionary<string, Checkout>(PlatformSupport.PathComparer);
        var mappedRoots = new List<string>();
        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            var candidate = FindRepository(root);
            if (candidate == null) { mappedRoots.Add(root); continue; }
            var repository = Path.GetFullPath(await Git(candidate, ["rev-parse", "--show-toplevel"], ct));
            if (!repositories.TryGetValue(repository, out var checkout))
            {
                var baseCommit = await Git(repository, ["rev-parse", "--verify", "HEAD"], ct);
                var branch = "monolith/agent-" + run.Chat.Id + "-" + token[..12] + "-" + repositories.Count;
                var target = Path.Combine(directory, repositories.Count.ToString(), Path.GetFileName(repository));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await Git(repository, ["worktree", "add", "-b", branch, target, baseCommit], ct);
                checkout = new(repository, await Git(repository, ["rev-parse", "--abbrev-ref", "HEAD"], ct), branch, target, baseCommit);
                repositories.Add(repository, checkout); workspace.checkouts.Add(checkout);
            }
            mappedRoots.Add(Path.GetFullPath(Path.GetRelativePath(repository, root), checkout.Directory));
        }
        project.SetSourceFolders(mappedRoots);
        await File.WriteAllTextAsync(Path.Combine(directory, "worktrees.json"), JsonSerializer.Serialize(workspace.checkouts), ct);
        return workspace;
    }
    public async Task<string> IntegrateAsync(CancellationToken ct)
    {
        var results = new List<string>();
        foreach (var checkout in checkouts)
        {
            var gate = merges.GetOrAdd(checkout.Repository, _ => new(1, 1));
            await gate.WaitAsync(ct);
            try { results.Add(await Integrate(checkout, ct)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { results.Add($"Fusion non effectuée / Merge skipped · {checkout.Branch}: {ex.Message}"); }
            finally { gate.Release(); }
        }
        return string.Join("\n", results);
    }
    async Task<string> Integrate(Checkout checkout, CancellationToken ct)
    {
        var hooks = Path.Combine(directory, "empty-hooks"); Directory.CreateDirectory(hooks);
        string[] automatic = ["-c", "core.hooksPath=" + hooks, "-c", "commit.gpgsign=false", "-c", "user.name=Monolith Harness", "-c", "user.email=monolith@localhost"];
        if (await Git(checkout.Directory, ["rev-parse", "--abbrev-ref", "HEAD"], ct) != checkout.Branch)
            return $"Fusion différée : branche du sous-agent changée / Merge deferred: child branch changed · {checkout.Directory}";
        if ((await Git(checkout.Directory, ["status", "--porcelain=v1"], ct)).Length > 0)
        {
            await Git(checkout.Directory, ["add", "-A", "--", "."], ct);
            await Git(checkout.Directory, [.. automatic, "commit", "-m", "Monolith subagent: " + checkout.Branch], ct);
        }
        if (await Git(checkout.Directory, ["rev-parse", "HEAD"], ct) == checkout.BaseCommit)
            return $"Aucune modification / No changes · {checkout.Branch}";
        var branch = await Git(checkout.Repository, ["rev-parse", "--abbrev-ref", "HEAD"], ct);
        if (branch == "HEAD" || branch != checkout.ParentBranch || (await Git(checkout.Repository, ["status", "--porcelain=v1"], ct)).Length > 0)
            return $"Fusion différée : branche parente changée ou modifications locales. Worktree conservé / Merge deferred: parent branch changed or local edits. Worktree retained · {checkout.Branch}";
        // Merge in a private checkout first. The parent only ever receives a fast-forward,
        // so conflicts cannot leave the user's index or working directory half-merged.
        var integration = Path.Combine(directory, "integration-" + Guid.NewGuid().ToString("N"));
        var created = false;
        try
        {
            var parentHead = await Git(checkout.Repository, ["rev-parse", "HEAD"], ct);
            await Git(checkout.Repository, ["worktree", "add", "--detach", integration, parentHead], ct); created = true;
            await Git(integration, [.. automatic, "merge", "--no-edit", "--no-ff", checkout.Branch], ct);
            var mergedHead = await Git(integration, ["rev-parse", "HEAD"], ct);
            if (await Git(checkout.Repository, ["rev-parse", "HEAD"], ct) != parentHead ||
                await Git(checkout.Repository, ["rev-parse", "--abbrev-ref", "HEAD"], ct) != checkout.ParentBranch ||
                (await Git(checkout.Repository, ["status", "--porcelain=v1"], ct)).Length > 0)
                return $"Fusion différée : le projet parent a changé / Merge deferred: parent changed · {checkout.Branch}";
            await Git(checkout.Repository, [.. automatic, "merge", "--ff-only", "--no-edit", mergedHead], ct);
            return $"Fusion automatique réussie / Automatic merge completed · {checkout.Branch}";
        }
        finally
        {
            if (created)
            {
                // Only this generated disposable integration directory is removed.
                var owned = Path.GetFullPath(integration);
                if (owned.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, PlatformSupport.PathComparison))
                    try { await Git(checkout.Repository, ["worktree", "remove", "--force", owned], CancellationToken.None); }
                    catch (IOException) { /* Retain a failed cleanup rather than touching other worktrees. */ }
            }
        }
    }
}
