using System.Text.Json.Nodes;
using OhMyHarness.Core;

static class GitFileIndexChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "omh-git-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = new SourceAccess(folder);
        var skills = "git,file_index";
        Task<bool> Allow(string _, string __, CancellationToken ___) => Task.FromResult(true);
        Task<string> Call(string name, JsonObject? args = null, bool plan = false) =>
            SourceTools.ExecuteAsync(source, name, args ?? [], () => skills, Allow, default, plan);
        JsonObject Entry(string path) => JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "index.ohm")))!["entries"]!.AsArray()
            .Single(x => x?["path"]?.GetValue<string>() == path)!.AsObject();
        try
        {
            var definitions = new JsonArray();
            SourceTools.AddDefinitions(definitions, true, skills);
            check(definitions.Any(x => x?["function"]?["name"]?.ToString() == "git_commit") &&
                  definitions.Any(x => x?["function"]?["name"]?.ToString() == "file_index_describe"), "New Git and file index tools are exposed when enabled");
            AgentPolicy.Filter(definitions, "plan");
            check(definitions.Any(x => x?["function"]?["name"]?.ToString() == "file_index_read") &&
                  definitions.All(x => x?["function"]?["name"]?.ToString() != "git_commit"), "Plan exposes index and Git reads only");

            await File.WriteAllTextAsync(Path.Combine(folder, "README.md"), "# Example project\nUseful guide.");
            Directory.CreateDirectory(Path.Combine(folder, "src"));
            await File.WriteAllTextAsync(Path.Combine(folder, "src", "Worker.cs"), "public class Worker { }");
            await File.WriteAllTextAsync(Path.Combine(folder, ".env"), "SECRET=hidden");
            await Call("file_index_update");
            check(File.Exists(Path.Combine(folder, "index.ohm")) && Entry("src/Worker.cs")["generatedDescription"]!.ToString().Contains("Worker") &&
                  Entry("src")["kind"]!.ToString() == "directory", "Index inventories files, folders and structural descriptions");
            check(!File.ReadAllText(Path.Combine(folder, "index.ohm")).Contains("SECRET=hidden") &&
                  !File.ReadAllText(Path.Combine(folder, "index.ohm")).Contains("\"path\": \".env\""), "Index omits excluded secret files");
            var fingerprint = Entry("src/Worker.cs")["fingerprint"]!.ToString();
            await Call("file_index_describe", new() { ["entries"] = new JsonArray(new JsonObject {
                ["path"] = "src/Worker.cs", ["description"] = "Implements a worker.", ["fingerprint"] = fingerprint }) });
            await source.ModifyAsync("src/Worker.cs", "Worker", "Runner", default);
            check(Entry("src/Worker.cs")["descriptionStale"]!.GetValue<bool>() &&
                  Entry("src/Worker.cs")["generatedDescription"]!.ToString().Contains("Runner"), "Source edits update the index and flag stale model explanations");
            var beforePlan = await File.ReadAllTextAsync(Path.Combine(folder, "index.ohm"));
            var read = JsonNode.Parse(await Call("file_index_read", new() { ["directory"] = "src", ["limit"] = 10 }, plan: true))!;
            check(read["matching"]!.GetValue<int>() == 1 && !read["persisted"]!.GetValue<bool>() &&
                  beforePlan == await File.ReadAllTextAsync(Path.Combine(folder, "index.ohm")), "Plan navigation does not modify index.ohm");

            if (!(await Call("git_init")).StartsWith("Exit code: 0\n")) throw new Exception("Git initialization failed.");
            check((await Call("git_status")).Contains("README.md"), "Git reads the attached repository status");
            try { await Call("git_stage", new() { ["paths"] = new JsonArray(".env") }); throw new Exception("Git accepted an excluded file."); }
            catch (UnauthorizedAccessException) { check(true, "Git staging rejects excluded files"); }
            await Call("git_stage", new() { ["paths"] = new JsonArray("README.md") });
            check((await Call("git_diff", new() { ["staged"] = true })).Contains("Example project"), "Git staging and staged diff work on an explicit file");
            try { await Call("git_commit", new() { ["message"] = "Blocked in Plan" }, plan: true); throw new Exception("Plan committed."); }
            catch (UnauthorizedAccessException) { check(true, "Plan blocks Git commits"); }
        }
        finally
        {
            var full = Path.GetFullPath(folder);
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(temporaryRoot, PlatformSupport.PathComparison) ||
                !Path.GetFileName(full).StartsWith("omh-git-index-", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe fixture cleanup.");
            foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(full, true);
        }
    }
}
