using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public static partial class PortableStorage
{
    internal static async Task RemapReferencesAsync(HarnessDb db)
    {
        var root = Path.GetDirectoryName(db.FilePath)!;
        string PathValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            var absolute = Path.IsPathFullyQualified(Normalize(value));
            if (!absolute && !Owned(value)) return value;
            var mapped = ResolvePath(value, root);
            return absolute ? mapped : Path.GetRelativePath(root, mapped).Replace('\\', '/');
        }
        string Scope(string value) => string.Join('|', value.Split('|').Select(x => Path.IsPathFullyQualified(Normalize(x)) ? PathValue(x) : x));
        string JsonPaths(string json, bool allStrings = false)
        {
            if (string.IsNullOrWhiteSpace(json)) return json;
            JsonNode? node;
            try { node = JsonNode.Parse(json); }
            catch (System.Text.Json.JsonException) { return json; }
            void Walk(JsonNode? current, bool values)
            {
                if (current is JsonArray array)
                {
                    for (var i = 0; i < array.Count; i++)
                        if (array[i] is JsonValue value && value.TryGetValue<string>(out var path) && values) array[i] = PathValue(path);
                        else Walk(array[i], values);
                }
                else if (current is JsonObject obj)
                {
                    foreach (var pair in obj.ToArray())
                    {
                        var key = Scope(pair.Key);
                        var pathField = pair.Key is "Path" or "Directory" or "ChatExecutable" or "ImageExecutable"
                            or "LogoPath" or "LightLogoPath" or "DarkLogoPath";
                        if (pair.Value is JsonValue value && value.TryGetValue<string>(out var path) && (pathField || values)) obj[pair.Key] = PathValue(path);
                        else Walk(pair.Value, values);
                        if (key != pair.Key)
                        {
                            var saved = obj[pair.Key]; obj.Remove(pair.Key);
                            var existing = obj[key]?.GetValue<string>(); var imported = saved?.GetValue<string>();
                            obj[key] = existing == "deny" || imported == "deny" ? JsonValue.Create("deny")
                                : existing == "ask" || imported == "ask" ? JsonValue.Create("ask") : saved;
                        }
                    }
                }
            }
            Walk(node, allStrings);
            var normalized = node?.ToJsonString() ?? json;
            return JsonNode.DeepEquals(node, JsonNode.Parse(json)) ? json : normalized;
        }
        foreach (var project in await db.Projects.Where(x => x.SourceFolder != "" || x.PermissionProfileJson != "").ToListAsync())
        {
            project.SetSourceFolders(project.GetSourceFolders().Select(PathValue));
            project.PermissionProfileJson = JsonPaths(project.PermissionProfileJson);
        }
        foreach (var chat in await db.Chats.Where(x => x.ResourcePathsJson != "").ToListAsync()) chat.ResourcePathsJson = JsonPaths(chat.ResourcePathsJson, true);
        foreach (var provider in await db.Providers.ToListAsync())
        {
            provider.LocalModelsJson = JsonPaths(provider.LocalModelsJson);
            provider.ExecutablePath = PathValue(provider.ExecutablePath);
        }
        foreach (var state in await db.States.ToListAsync()) state.FeaturesJson = JsonPaths(state.FeaturesJson);
        foreach (var server in await db.McpServers.ToListAsync())
        {
            server.WorkingDirectory = PathValue(server.WorkingDirectory);
            server.Command = ResolveArgument(server.Command, root);
            string[] args;
            try { args = System.Text.Json.JsonSerializer.Deserialize<string[]>(server.ArgumentsJson) ?? []; }
            catch (System.Text.Json.JsonException) { continue; }
            var mapped = args.Select(x => ResolveArgument(x, root)).ToArray();
            if (!args.SequenceEqual(mapped)) server.ArgumentsJson = System.Text.Json.JsonSerializer.Serialize(mapped);
        }
        // Retain path-scoped grants; imported deny/ask rules above keep referring to the same files.
        var grants = await db.PermissionGrants.ToListAsync();
        var duplicates = grants.GroupBy(x => Scope(x.Scope)).Where(x => x.Count() > 1).ToArray();
        foreach (var group in duplicates)
        {
            var keep = group.FirstOrDefault(x => x.Scope == group.Key) ?? group.First();
            foreach (var grant in group.Where(x => x != keep)) { db.PermissionGrants.Remove(grant); grants.Remove(grant); }
        }
        if (duplicates.Length > 0) await db.SaveChangesAsync();
        foreach (var grant in grants) grant.Scope = Scope(grant.Scope);
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
    }
}
