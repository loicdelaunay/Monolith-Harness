using Microsoft.EntityFrameworkCore;

namespace MonolithHarness.Core.Remote;

/// <summary>
/// The speed and consumption dashboard of this device, as the server receives it when the user turns sharing on: one line of numbers per model
/// call (tokens, duration, provider and model names). Nothing that was said, no project or conversation name leaves the device.
/// </summary>
public static class UsageSharing
{
    public const int BackfillDays = 30;
    public const int BatchSize = 500;

    /// <summary>The next calls to send (after the last one already sent, no older than the first sending allows), and the id to remember afterwards.</summary>
    public sealed record Batch(IReadOnlyList<UsageSampleDto> Samples, long LastId, bool More);

    /// <summary>Records created while sharing was off are not sent until it is turned on again; turning it on also covers the last days.</summary>
    public static void Enable(ServerLinkData link)
    {
        link.ShareUsage = true;
        if (link.DeviceId.Length == 0) link.DeviceId = Guid.NewGuid().ToString("N")[..12];
        link.UsageCursor = 0;
        link.UsageSinceUtc = DateTime.UtcNow.AddDays(-BackfillDays);
    }

    public static async Task<Batch> NextBatchAsync(HarnessDb db, ServerLinkData link, CancellationToken ct = default)
    {
        var since = link.UsageSinceUtc ?? DateTime.UtcNow.AddDays(-BackfillDays);
        var cursor = link.UsageCursor;
        var rows = await db.TokenUsages.AsNoTracking().Where(x => x.Id > cursor).OrderBy(x => x.Id).Take(BatchSize + 1).ToListAsync(ct);
        var more = rows.Count > BatchSize;
        if (more) rows.RemoveAt(rows.Count - 1);
        if (rows.Count == 0) return new([], cursor, false);
        var viaServer = link.Providers.Values.ToHashSet();
        var samples = rows.Where(x => !x.Legacy && x.CompletedUtc >= since)
            .Select(x => ToSample(x, x.ProviderId is { } id && viaServer.Contains(id))).ToList();
        return new(samples, rows[^1].Id, more);
    }

    public static UsageSampleDto ToSample(TokenUsage usage, bool viaServer) => new(usage.Id.ToString(), DateTime.SpecifyKind(usage.CompletedUtc, DateTimeKind.Utc), CleanProvider(usage.ProviderName),
        usage.Model, usage.Activity, usage.InputTokens, usage.OutputTokens, usage.CachedInputTokens, usage.Seconds, usage.Status, usage.Estimated, viaServer);

    /// <summary>The name of a provider as the user gave it; the suffix added to server providers says where they come from, not what they are.</summary>
    static string CleanProvider(string name)
    {
        foreach (var suffix in new[] { ServerSync.ServerProviderSuffix, ServerSync.CloudProviderSuffix })
            if (name.EndsWith(suffix, StringComparison.Ordinal)) return name[..^suffix.Length];
        return name;
    }
}
