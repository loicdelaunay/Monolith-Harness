using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

static class ConversationFileWorkspaceChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var previous = HarnessDb.DatabasePath;
        var root = Path.Combine(Path.GetTempPath(), "monolith-conversation-files-" + Guid.NewGuid().ToString("N"));
        var portable = Path.Combine(root, "portable"); var database = Path.Combine(portable, "database.sqlite");
        try
        {
            PortableStorage.UseDatabase(database);
            await using var db = new HarnessDb(database); await db.InitializeAsync();
            var owner = await db.Projects.SingleAsync(); var chat = await db.Chats.SingleAsync();
            var paths = ProjectResources.For(chat, owner, database);
            var workspace = Path.Combine(portable, "workspace", "conversations", "chat-" + chat.Id);
            check(paths.SequenceEqual([workspace]) && Directory.Exists(workspace), "Folderless chat gets its own portable conversation workspace");
            check(owner.SourceFolder == "" && chat.ResourcePathsJson == "", "Automatic workspace does not become a shared project folder or explicit resource");
            var source = new SourceAccess(paths);
            await source.WriteAsync("nested/result.txt", "created by the conversation", default);
            check(await source.ReadAsync("nested/result.txt", default) == "created by the conversation", "Normal source tools can create and read files in the automatic workspace");
            using (var run = new ConversationSession(chat, owner, new(), new(), "files", [], database))
                check(run.Project.GetSourceFolders().SequenceEqual([workspace]), "GUI/CLI generation sessions capture the same file workspace");
            using (var restarted = new ConversationSession(chat, owner, new(), new(), "resume", [], database))
                check(await new SourceAccess(restarted.Project.GetSourceFolders()).ReadAsync("nested/result.txt", default) == "created by the conversation", "A resumed conversation retains its files and directory");
            var other = new Chat { ProjectId = owner.Id }; db.Chats.Add(other); await db.SaveChangesAsync();
            var otherPath = ProjectResources.For(other, owner, database).Single();
            check(otherPath != workspace && !File.Exists(Path.Combine(otherPath, "nested/result.txt")), "Conversations in the same folderless project have separate file workspaces");
            try { source.Resolve(Path.Combine(otherPath, "result.txt")); throw new Exception("Expected isolation."); }
            catch (UnauthorizedAccessException) { check(true, "Source tools cannot cross into another conversation workspace"); }
            try { source.Resolve("../../../database.sqlite"); throw new Exception("Expected isolation."); }
            catch (UnauthorizedAccessException) { check(true, "The SQLite database and application root are outside the conversation file scope"); }
            try
            {
                await SourceTools.ExecuteAsync(source, "patch_sources", new JsonObject { ["dry_run"] = false,
                    ["edits"] = new JsonArray(new JsonObject { ["path"] = "plan.txt", ["old_text"] = null, ["new_text"] = "forbidden" }) },
                    () => "patch_sources", (_, _, _) => Task.FromResult(true), default, readOnly: true);
                throw new Exception("Expected Plan restriction.");
            }
            catch (UnauthorizedAccessException) { check(!File.Exists(Path.Combine(workspace, "plan.txt")), "Plan mode still forbids writes in an automatic workspace"); }
            var explicitFolder = Path.Combine(root, "real-project"); Directory.CreateDirectory(explicitFolder);
            chat.ResourcePathsJson = ProjectResources.Serialize([workspace, explicitFolder], chat.Id, database);
            check(ProjectResources.For(chat, owner, database).SequenceEqual([explicitFolder]), "Attaching a folder replaces the implicit workspace instead of persisting it as a source");
            chat.ResourcePathsJson = "";
            owner.SetSourceFolders([explicitFolder]);
            check(ProjectResources.For(chat, owner, database).SequenceEqual([explicitFolder]), "Attached project folders remain the working roots");
            chat.ResourcePathsJson = "[]";
            check(ProjectResources.For(chat, owner, database).SequenceEqual([workspace]), "An explicit empty resource override gets a workspace without exposing project defaults");
            var attachment = Path.Combine(root, "reference.txt"); await File.WriteAllTextAsync(attachment, "reference");
            chat.ResourcePathsJson = ProjectResources.Serialize([workspace, attachment], chat.Id, database);
            check(ProjectResources.For(chat, owner, database).SequenceEqual([attachment, workspace]), "File-only resources stay readable and receive a directory for generated files");
            check(!chat.ResourcePathsJson.Contains(workspace, StringComparison.Ordinal), "Implicit workspace paths are never saved or shared with forked resource lists");
            check(ConversationWorkspace.DirectoryPath(chat.Id, Path.Combine(root, "another-profile", "database.sqlite")) != workspace,
                "Identical chat IDs in different portable profiles cannot share a workspace");
            chat.ResourcePathsJson = ""; owner.SetSourceFolders([]); await db.SaveChangesAsync();

            string Fixture(string relative, string content)
            {
                var file = Path.Combine(portable, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, content); return file;
            }
            var oldModel = Fixture("model/nested/local.gguf", "model weights");
            var oldLogo = Fixture("branding/logo.png", "logo");
            var oldScript = Fixture("scripts/python/chat-1/code.py", "print('preserved')");
            Fixture("logs/history.jsonl", "history"); Fixture("WebView2/chat-1/profile", "cookies");
            Fixture("assets/chat-1/document.json", "drawing"); Fixture("assets/app.ico", "installed icon");
            Fixture("runtimes/python/version/bin/python", "python runtime"); Fixture("runtimes/linux-x64/native/library.so", "installed native library");
            var canonicalBrowser = Fixture("workspace/Browser/keep.txt", "current workspace profile");
            Fixture("Browser/keep.txt", "legacy profile");
            Fixture("workspace/skills/current/SKILL.md", "current skill");
            Fixture("skills/imported/SKILL.md", "legacy skill");
            var oldSkill = Fixture("skills/current/SKILL.md", "conflicting legacy skill");
            owner.SetSourceFolders([Path.Combine(portable, "model")]);
            chat.ResourcePathsJson = ProjectResources.Serialize([oldScript]);
            var local = new LocalProviderSettings { Directory = "model", Models = [new LocalModel { Id = "local", Path = "model/nested/local.gguf" }] };
            (await db.Providers.FirstAsync()).LocalModelsJson = local.Json();
            var state = await db.States.SingleAsync(); var features = FeatureSettings.Read(state.FeaturesJson); features.LogoPath = "branding/logo.png"; state.FeaturesJson = features.Json();
            owner.PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, string>
                { ["write_source|" + Path.Combine(portable, "model")] = "deny", ["write_source|" + Path.Combine(portable, "workspace", "model")] = "allow" });
            db.PermissionGrants.AddRange(new PermissionGrant { Scope = "source|" + Path.Combine(portable, "model"), Name = "Legacy grant" },
                new PermissionGrant { Scope = "source|" + Path.Combine(portable, "workspace", "model"), Name = "Current grant" });
            await db.SaveChangesAsync();
            await db.InitializeAsync();
            check(!Directory.Exists(Path.Combine(portable, "model")) && File.ReadAllText(PortableStorage.ResolvePath(oldModel)) == "model weights", "Legacy model folders move into workspace and old absolute paths still resolve");
            check(new LocalModel { Path = "model/nested/local.gguf" }.FullPath == PortableStorage.ResolvePath(oldModel), "Old relative model paths resolve after the portable migration");
            check(File.ReadAllText(BrandingAssets.Resolve("branding/logo.png")) == "logo" && !File.Exists(oldLogo), "Existing branding remains usable after migration");
            check(File.Exists(Path.Combine(PortableStorage.Folder("logs"), "history.jsonl")) && File.Exists(Path.Combine(PortableStorage.Folder("WebView2"), "chat-1/profile")), "Logs and browser profiles retain their contents in workspace");
            check(File.Exists(Path.Combine(portable, "workspace/assets/chat-1/document.json")) && File.Exists(Path.Combine(portable, "assets/app.ico")), "Conversation drawings migrate while installed application assets remain in place");
            check(File.Exists(Path.Combine(portable, "workspace/runtimes/python/version/bin/python")) && File.Exists(Path.Combine(portable, "runtimes/linux-x64/native/library.so")), "Managed Python moves without relocating installed native runtime libraries");
            check(File.ReadAllText(canonicalBrowser) == "current workspace profile" && File.ReadAllText(Path.Combine(PortableStorage.Folder("Browser"), "keep.txt")) == "legacy profile", "Directory collisions preserve both complete browser profiles without overwriting either");
            check(File.Exists(Path.Combine(PortableStorage.Skills, "current/SKILL.md")) && File.Exists(Path.Combine(PortableStorage.Skills, "imported/SKILL.md"))
                && File.ReadAllText(PortableStorage.ResolvePath(oldSkill)) == "conflicting legacy skill", "Global skills merge without hiding existing skills; conflicting copies remain recoverable");
            check(owner.GetSourceFolders().SequenceEqual([PortableStorage.Folder("model")]) && chat.ResourcePathsJson.Contains("workspace"), "Persisted project and conversation resource paths are updated");
            check(ProjectResources.Decision(owner.PermissionProfileJson, "write_source|" + PortableStorage.Folder("model")) == "deny", "Imported path-specific denial remains effective after folder migration");
            check(await db.PermissionGrants.CountAsync() == 1 && (await db.PermissionGrants.SingleAsync()).Scope.EndsWith(PortableStorage.Folder("model"), StringComparison.Ordinal),
                "Equivalent old/new path grants migrate without a unique-scope collision");
            check(LocalProviderSettings.Read((await db.Providers.FirstAsync()).LocalModelsJson).Models.Single().Path.StartsWith("workspace/", StringComparison.Ordinal)
                && FeatureSettings.Read(state.FeaturesJson).LogoPath == "workspace/branding/logo.png", "Local model catalogues and theme logos retain portable relative paths");
            check(new SourceAccess(owner.GetSourceFolders()).Resolve(oldModel) == PortableStorage.ResolvePath(oldModel), "Historical file links resolve inside the migrated authorized source root");
            PortableStorage.MigrateLayout(); PortableStorage.MigrateLayout();
            check(File.ReadAllText(canonicalBrowser) == "current workspace profile" && await source.ReadAsync("nested/result.txt", default) == "created by the conversation", "Repeated migration preserves both existing workspace data and conversation files");
            check(File.Exists(database) && PortableStorage.Root == portable, "Database location remains beside the portable executable/profile");
            var interruptedRoot = Path.Combine(root, "interrupted"); Directory.CreateDirectory(Path.Combine(interruptedRoot, "workspace"));
            Directory.CreateDirectory(Path.Combine(interruptedRoot, "images")); File.WriteAllText(Path.Combine(interruptedRoot, "images", "image.png"), "saved image");
            File.WriteAllText(Path.Combine(interruptedRoot, "workspace", ".storage-layout.json"), "{\"images\":\"workspace/images\"}");
            PortableStorage.MigrateLayout(interruptedRoot);
            check(File.ReadAllText(Path.Combine(interruptedRoot, "workspace", "images", "image.png")) == "saved image" && !Directory.Exists(Path.Combine(interruptedRoot, "images")),
                "A journaled migration interrupted before the rename resumes at startup");
            if (!OperatingSystem.IsWindows())
            {
                var unsafeRoot = Path.Combine(root, "unsafe"); var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(unsafeRoot); Directory.CreateDirectory(outside);
                Directory.CreateSymbolicLink(Path.Combine(unsafeRoot, "workspace"), outside);
                try { PortableStorage.MigrateLayout(unsafeRoot); throw new Exception("Expected link rejection."); }
                catch (UnauthorizedAccessException) { check(!Directory.EnumerateFileSystemEntries(outside).Any(), "Migration rejects a workspace symlink before touching another directory"); }
                Directory.Delete(Path.Combine(unsafeRoot, "workspace"));
            }
        }
        finally
        {
            PortableStorage.UseDatabase(previous); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
