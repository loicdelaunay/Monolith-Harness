using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;
using MonolithHarness.Core.Hosting;

namespace MonolithHarness.Server.Workers;

/// <summary>What the server tells a worker about one run. The file is deleted by the worker as soon as it is read.</summary>
public sealed record WorkerJob(
    string Database,
    string FilesDirectory,
    string ProjectName,
    int ChatId,
    string ProviderBaseUrl,
    string ProviderKey,
    string Model,
    int ContextLimit,
    bool SupportsImages,
    string EnabledSkills,
    bool Sandbox,
    string PermissionMode);

/// <summary>
/// Worker entry point: the server executable re-launched with <c>worker --job file</c>. It prepares the project's engine database
/// (one provider only: the server relay, with a run-scoped token) and then speaks the engine's JSON-lines protocol on stdin/stdout.
/// </summary>
public static class WorkerMode
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        var index = args.ToList().IndexOf("--job");
        if (index < 0 || index + 1 >= args.Count) throw new ArgumentException("worker requires --job <file>.");
        var jobPath = args[index + 1];
        var job = JsonSerializer.Deserialize<WorkerJob>(File.ReadAllText(jobPath), Json) ?? throw new InvalidDataException("Invalid job.");
        try { File.Delete(jobPath); } catch (IOException) { }

        PortableStorage.UseDatabase(job.Database);
        PortableStorage.EnsureWritable();
        await Prepare(job);
        await ServiceHost.RunAsync(["--database", job.Database]);
        return 0;
    }

    static async Task Prepare(WorkerJob job)
    {
        await using var db = new HarnessDb(job.Database);
        await db.InitializeAsync();
        // A cloud project never reaches a provider directly: every previous provider is removed, the relay is the only one.
        db.Providers.RemoveRange(await db.Providers.ToListAsync());
        await db.SaveChangesAsync();
        var provider = new Provider
        {
            Name = "Monolith Harness Server",
            Kind = "openai",
            BaseUrl = job.ProviderBaseUrl,
            Model = job.Model,
            SupportsImages = job.SupportsImages,
            ProtectedKey = KeyVault.Encrypt(job.ProviderKey)
        };
        ModelContexts.Select(provider, job.Model);
        ModelContexts.SetOverride(provider, job.ContextLimit, job.Model);
        ModelContexts.Sync(provider);
        db.Providers.Add(provider);
        await db.SaveChangesAsync();

        var state = await db.States.SingleAsync();
        state.ProviderId = provider.Id;
        state.EnabledSkills = job.EnabledSkills;
        state.PermissionMode = job.PermissionMode;
        state.AutoContinue = true;
        state.Language = "fr";

        var project = await db.Projects.OrderBy(x => x.Id).FirstAsync();
        project.Name = job.ProjectName;
        project.SetSourceFolders([job.FilesDirectory]);

        var chat = await db.Chats.SingleAsync(x => x.Id == job.ChatId);
        chat.SandboxEnabled = job.Sandbox;
        chat.AllowOutsideResources = false;
        chat.InteractionMode = "agent";
        chat.ExecutionMode = "execute";
        chat.ChatWebEnabled = job.EnabledSkills.Split(',').Contains("web");
        chat.ChatPythonEnabled = false;
        await db.SaveChangesAsync();
    }
}
