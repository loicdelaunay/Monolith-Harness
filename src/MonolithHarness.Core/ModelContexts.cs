using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public sealed record ModelContextMetadata(string Model, int? MaximumTokens, string Source = "API");
public sealed record ModelContextInfo(int Limit, int? MaximumTokens, bool Automatic, string Source);

/// <summary>Limits are scoped to the exact model ID of one connection; output limits are never context capacities.</summary>
public static class ModelContexts
{
    public const int DefaultContextLimit = 256_000;
    public sealed class Entry
    {
        public int? MaximumTokens { get; set; }
        public int? OverrideTokens { get; set; }
        public string Source { get; set; } = "API";
    }
    sealed class Cache { public string Json = ""; public Dictionary<string, Entry> Entries = new(StringComparer.Ordinal); }
    static readonly ConditionalWeakTable<Provider, Cache> cache = new();
    public static int Ceiling(Provider provider) => provider.IsLocal ? 262144 : 10_000_000;
    static Dictionary<string, Entry> Read(Provider provider)
    {
        var value = cache.GetValue(provider, _ => new Cache());
        if (value.Json == provider.ModelContextsJson) return value.Entries;
        try { value.Entries = JsonSerializer.Deserialize<Dictionary<string, Entry>>(provider.ModelContextsJson) ?? new(StringComparer.Ordinal); }
        catch (JsonException) { value.Entries = new(StringComparer.Ordinal); }
        foreach (var key in value.Entries.Where(x => x.Value == null).Select(x => x.Key).ToArray()) value.Entries.Remove(key);
        value.Json = provider.ModelContextsJson;
        return value.Entries;
    }
    public static ModelContextInfo For(Provider provider, string? model = null)
    {
        model = (model ?? provider.Model).Trim();
        Read(provider).TryGetValue(model, out var entry);
        var maximum = entry?.MaximumTokens is >= 1024 and <= 10_000_000 ? entry.MaximumTokens : null;
        var custom = entry?.OverrideTokens is >= 1024 ? entry.OverrideTokens : null;
        var fallback = Fallback(provider, model);
        var limit = Math.Clamp(custom ?? maximum ?? fallback, 1024, Math.Min(maximum ?? Ceiling(provider), Ceiling(provider)));
        return new(limit, maximum, custom == null, maximum != null ? entry!.Source : "fallback");
    }
    public static int Fallback(Provider provider, string? model = null) => Math.Clamp(provider.IsLocal ? 8192 : ModelCatalog.GetDefaultContextLimit(model ?? provider.Model) ?? DefaultContextLimit, 1024, Ceiling(provider));
    public static void Select(Provider provider, string model)
    {
        provider.Model = model.Trim();
        Sync(provider);
    }
    public static void SetOverride(Provider provider, int? tokens, string? model = null)
    {
        var key = (model ?? provider.Model).Trim();
        if (key.Length == 0) return;
        var entries = Read(provider);
        if (!entries.TryGetValue(key, out var entry)) entries[key] = entry = new();
        entry.OverrideTokens = tokens.HasValue ? Math.Clamp(tokens.Value, 1024, Ceiling(provider)) : null;
        Save(provider, entries);
    }
    public static void Merge(Provider provider, IEnumerable<ModelContextMetadata> metadata)
    {
        var entries = Read(provider);
        bool changed = false;
        foreach (var item in metadata)
        {
            var key = item.Model.Trim();
            if (key.Length == 0 || item.MaximumTokens is not (>= 1024 and <= 10_000_000)) continue;
            if (!entries.TryGetValue(key, out var entry)) entries[key] = entry = new();
            entry.MaximumTokens = item.MaximumTokens; entry.Source = item.Source; changed = true;
        }
        if (changed) Save(provider, entries);
    }
    public static void MergeLocal(Provider provider)
    {
        if (provider.IsLocal) Merge(provider, LocalProviderSettings.Read(provider.LocalModelsJson).Models
            .Where(x => x.Purpose == "chat").Select(x => new ModelContextMetadata(x.Id, x.ContextTokens, "GGUF")));
    }
    public static ModelContextMetadata FromApi(string model, JsonObject data)
    {
        // Explicit full-window fields take precedence over an input-only limit; max output is separate.
        var count = Count(data["context_length"]) ?? Count(data["context_window"]) ?? Count(data["max_context_length"])
            ?? Count(data["max_model_len"]) ?? Count((data["limit"] as JsonObject)?["context"])
            ?? Count((data["limits"] as JsonObject)?["context_window"])
            ?? Count((data["top_provider"] as JsonObject)?["context_length"]);
        var input = count == null ? Count(data["inputTokenLimit"]) : null;
        return new(model, count ?? input, input.HasValue ? "API input" : "API");
    }
    static int? Count(JsonNode? node) => TokenCache.ReadCount(node) is >= 1024 and <= 10_000_000 and var count ? count : null;
    static void Save(Provider provider, Dictionary<string, Entry> entries)
    {
        provider.ModelContextsJson = JsonSerializer.Serialize(entries);
        cache.GetValue(provider, _ => new Cache()).Json = provider.ModelContextsJson;
        Sync(provider);
    }
    public static void Sync(Provider provider) => provider.StoreContextLimit(For(provider).Limit);
}
