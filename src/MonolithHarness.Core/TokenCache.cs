using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

/// <summary>Only provider-reported cache reads; absent or invalid counters stay unknown.</summary>
public static class TokenCache
{
    public static int? Read(JsonNode? value)
    {
        if (value is not JsonObject usage) return null;
        return ReadCount((usage["prompt_tokens_details"] as JsonObject)?["cached_tokens"])
            ?? ReadCount((usage["input_tokens_details"] as JsonObject)?["cached_tokens"])
            ?? ReadCount(usage["prompt_cache_hit_tokens"])
            ?? ReadCount(usage["cachedContentTokenCount"])
            ?? ReadCount(usage["total_cached_tokens"]);
    }

    public static int? ReadCount(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<long>(out var count) && count is >= 0 and <= int.MaxValue) return (int)count;
        if (value.TryGetValue<int>(out var small) && small >= 0) return small;
        return null;
    }
}
