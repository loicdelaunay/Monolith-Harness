namespace MonolithHarness.Server;

/// <summary>Portable layout: one SQLite database and one workspace folder beside the executable, like the Desktop.</summary>
public sealed class ServerPaths
{
    public ServerPaths(string root) => Root = Path.GetFullPath(root);

    public string Root { get; }
    public string Database => Path.Combine(Root, "database.sqlite");
    public string Config => Path.Combine(Root, "server.json");
    public string Workspace => Path.Combine(Root, "workspace");
    public string Projects => Path.Combine(Workspace, "projects");
    public string Keys => Path.Combine(Workspace, "keys");
    public string Backups => Path.Combine(Workspace, "backups");
    public string Logs => Path.Combine(Workspace, "logs");
    public string Jobs => Path.Combine(Workspace, "jobs");

    public string ProjectRoot(Guid id) => Path.Combine(Projects, id.ToString("N"));
    public string ProjectFiles(Guid id) => Path.Combine(ProjectRoot(id), "files");
    public string ProjectEngine(Guid id) => Path.Combine(ProjectRoot(id), "engine");
    public string ProjectDatabase(Guid id) => Path.Combine(ProjectEngine(id), "database.sqlite");
    public string ProjectTemp(Guid id) => Path.Combine(ProjectRoot(id), "temp");

    /// <summary>The data folder of a development run (dotnet MonolithHarness.Server.dll) is the build output, not the dotnet host.</summary>
    public static string DefaultRoot()
    {
        var process = Environment.ProcessPath;
        var name = process == null ? "" : Path.GetFileNameWithoutExtension(process);
        return process == null || name.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(process)!;
    }

    public void Ensure()
    {
        foreach (var folder in new[] { Root, Workspace, Projects, Keys, Backups, Logs, Jobs }) Directory.CreateDirectory(folder);
        var probe = Path.Combine(Root, ".write-test-" + Guid.NewGuid().ToString("N"));
        try { using var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Le dossier du serveur doit être accessible en écriture / The server folder must be writable: " + Root, ex);
        }
    }
}
