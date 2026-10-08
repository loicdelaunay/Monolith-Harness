using MonolithHarness.Core;
using System.Text.Json.Nodes;

static class FileProposalStorageChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var previous = HarnessDb.DatabasePath;
        var temporaryBase = Path.GetFullPath(Path.GetTempPath());
        var root = Path.Combine(temporaryBase, "monolith-proposal-storage-" + Guid.NewGuid().ToString("N"));
        var database = Path.Combine(root, "primary", "database.sqlite");
        var hostDatabase = Path.Combine(root, "host", "database.sqlite");
        var project = Path.Combine(root, "project");
        try
        {
            // Deliberately use a different global root: proposals belong to their explicit database.
            PortableStorage.UseDatabase(hostDatabase);
            check(await FileProposals.ReadAsync(database, 1) == null,
                "Opening a conversation without proposals returns null instead of Unknown portable resource folder");
            var folder = Path.Combine(root, "primary", "workspace", "proposals");
            check(!Directory.Exists(folder), "Reading an empty proposal does not create folders or touch a database");
            check(PortableStorage.Folder("proposals", Path.GetDirectoryName(database)) == folder,
                "Proposal storage is registered in the portable workspace");
            foreach (var relative in new[] { "proposals/nested", "proposals\\nested" })
                check(PortableStorage.Folder(relative, Path.GetDirectoryName(database)) == Path.Combine(folder, "nested"),
                    "Proposal subfolders normalize directory separators: " + relative);
            foreach (var invalid in new[] { "unknown", "proposals-other", "proposals/../outside", "proposals/./nested", "../proposals", Path.Combine(root, "proposals") })
            {
                try { PortableStorage.Folder(invalid, Path.GetDirectoryName(database)); throw new Exception("Expected rejection: " + invalid); }
                catch (ArgumentException ex) { check(ex.ParamName == "relative", "Unauthorized or traversing resource path is still rejected: " + invalid); }
            }
            Directory.CreateDirectory(project);
            var source = new SourceAccess(project);
            var existing = Path.Combine(project, "existing.txt");
            await File.WriteAllTextAsync(existing, "before");
            await FileProposals.PrepareAsync(database, 1, source, new JsonObject { ["files"] = new JsonArray(
                new JsonObject { ["path"] = "existing.txt", ["content"] = "after" },
                new JsonObject { ["path"] = "new.txt", ["content"] = "new proposal" }) }, default);
            var batch = await FileProposals.ReadAsync(database, 1) ?? throw new Exception("Missing proposal.");
            check(batch.Files.Count == 2 && batch.Files.Single(f => f.Path == "existing.txt").Content == "after",
                "File proposals persist and reload their content");
            check(batch.Files.Single(f => f.Path == "existing.txt").Original!.AsSpan().SequenceEqual(System.Text.Encoding.UTF8.GetBytes("before")),
                "The original file bytes survive proposal serialization");
            check(await File.ReadAllTextAsync(existing) == "before" && !File.Exists(Path.Combine(project, "new.txt")),
                "Preparing a proposal never modifies the project files");
            check(Directory.Exists(folder) && Directory.GetFiles(folder, "*.json").Length == 1,
                "The batch is stored beside the explicitly selected database");
            check(!Directory.Exists(Path.Combine(root, "host", "workspace", "proposals")) && PortableStorage.Root == Path.GetDirectoryName(hostDatabase),
                "Preparing a proposal does not write into or change the global host root");
            check(await FileProposals.ReadAsync(database, 2) == null, "Different conversations have isolated proposal batches");
            var secondDatabase = Path.Combine(root, "secondary", "database.sqlite");
            check(await FileProposals.ReadAsync(secondDatabase, 1) == null, "Different database folders have isolated proposal batches");
            var siblingDatabase = Path.Combine(root, "primary", "other.sqlite");
            check(await FileProposals.ReadAsync(siblingDatabase, 1) == null, "Different databases in the same directory have isolated proposal batches");
            await FileProposals.ApplyAsync(database, 1, batch.Revision, ["new.txt"], source, default);
            check(await File.ReadAllTextAsync(Path.Combine(project, "new.txt")) == "new proposal" && await File.ReadAllTextAsync(existing) == "before",
                "Review applies only the selected file");
            var remaining = await FileProposals.ReadAsync(database, 1) ?? throw new Exception("Missing remaining proposal.");
            check(remaining.Files.Count == 1 && remaining.Files[0].Path == "existing.txt" && remaining.Revision != batch.Revision,
                "Unselected proposals remain available with a new revision");
            try { await FileProposals.ApplyAsync(database, 1, batch.Revision, ["existing.txt"], source, default); throw new Exception("Expected stale review rejection."); }
            catch (IOException) { check(await File.ReadAllTextAsync(existing) == "before", "An outdated review cannot apply or overwrite a file"); }
            await File.WriteAllTextAsync(existing, "manual change");
            try { await FileProposals.ApplyAsync(database, 1, remaining.Revision, ["existing.txt"], source, default); throw new Exception("Expected changed file rejection."); }
            catch (IOException) { check(await File.ReadAllTextAsync(existing) == "manual change", "A file changed after proposal preparation is preserved"); }
            check((await FileProposals.ReadAsync(database, 1))?.Revision == remaining.Revision, "A rejected application keeps the proposal batch intact");
            await File.WriteAllTextAsync(existing, "before");
            await FileProposals.ApplyAsync(database, 1, remaining.Revision, ["existing.txt"], source, default);
            check(await File.ReadAllTextAsync(existing) == "after" && (await FileProposals.ReadAsync(database, 1))?.Files.Count == 0,
                "Reviewed changes apply successfully and empty the batch");

            var legacyRoot = Path.Combine(root, "legacy");
            var legacyFolder = Path.Combine(legacyRoot, "proposals"); Directory.CreateDirectory(legacyFolder);
            await File.WriteAllTextAsync(Path.Combine(legacyFolder, "saved.json"), "legacy proposal");
            PortableStorage.MigrateLayout(legacyRoot);
            check(PortableStorage.Folder("proposals", legacyRoot) == Path.Combine(legacyRoot, "workspace", "proposals")
                && await File.ReadAllTextAsync(Path.Combine(PortableStorage.Folder("proposals", legacyRoot), "saved.json")) == "legacy proposal",
                "Legacy proposal folders migrate into workspace without losing files");
            check(PortableStorage.ResolvePath(Path.Combine(legacyFolder, "saved.json"), legacyRoot)
                == Path.Combine(legacyRoot, "workspace", "proposals", "saved.json"), "Saved legacy proposal paths resolve after migration");
            PortableStorage.MigrateLayout(legacyRoot);
            check(await File.ReadAllTextAsync(Path.Combine(PortableStorage.Folder("proposals", legacyRoot), "saved.json")) == "legacy proposal",
                "Repeating proposal migration preserves the original data");

            var collisionRoot = Path.Combine(root, "collision");
            var canonical = Path.Combine(collisionRoot, "workspace", "proposals"); Directory.CreateDirectory(canonical);
            var old = Path.Combine(collisionRoot, "proposals"); Directory.CreateDirectory(old);
            await File.WriteAllTextAsync(Path.Combine(canonical, "saved.json"), "canonical proposal");
            await File.WriteAllTextAsync(Path.Combine(old, "saved.json"), "legacy collision");
            PortableStorage.MigrateLayout(collisionRoot);
            var imported = PortableStorage.Folder("proposals", collisionRoot);
            check(imported != canonical && await File.ReadAllTextAsync(Path.Combine(imported, "saved.json")) == "legacy collision",
                "A migration collision keeps legacy proposals in a separate imported folder");
            check(await File.ReadAllTextAsync(Path.Combine(canonical, "saved.json")) == "canonical proposal", "Migration does not overwrite existing workspace proposals");
            PortableStorage.MigrateLayout(collisionRoot);
            check(PortableStorage.Folder("proposals", collisionRoot) == imported && File.Exists(Path.Combine(imported, "saved.json")),
                "A repeated collision migration retains the same imported proposal location");
        }
        finally
        {
            PortableStorage.UseDatabase(previous);
            var absoluteRoot = Path.GetFullPath(root);
            if (!absoluteRoot.StartsWith(Path.TrimEndingDirectorySeparator(temporaryBase) + Path.DirectorySeparatorChar, PlatformSupport.PathComparison))
                throw new IOException("Invalid test cleanup root.");
            if (Directory.Exists(absoluteRoot))
            {
                if ((File.GetAttributes(absoluteRoot) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked test cleanup root rejected.");
                Directory.Delete(absoluteRoot, true);
            }
        }
    }
}
