using System.Collections.Concurrent;
using MonolithHarness.Server.Services;

namespace MonolithHarness.Server.Security;

/// <summary>
/// Slows down password guessing per account and per address. The same logic applies to unknown e-mails,
/// so a lock-out never reveals whether an account exists. State is memory-only: a restart clears it.
/// </summary>
public sealed class LoginThrottle(SettingsService settings)
{
    sealed record Entry(int Failures, DateTime LockedUntil, DateTime LastFailure);

    readonly ConcurrentDictionary<string, Entry> entries = new();
    static readonly TimeSpan Memory = TimeSpan.FromMinutes(30);
    const int AddressFactor = 5;

    /// <summary>Seconds to wait before another attempt is accepted, or 0.</summary>
    public int RetryAfterSeconds(string email, string address)
    {
        Sweep();
        var wait = Math.Max(Remaining("account:" + email), Remaining("address:" + address));
        return wait;
    }

    public void Failure(string email, string address)
    {
        var limit = settings.Int(SettingKeys.MaxFailedLogins);
        Record("account:" + email, limit);
        Record("address:" + address, limit * AddressFactor);
    }

    public void Success(string email) => entries.TryRemove("account:" + email, out _);

    public void Unlock(string email) => entries.TryRemove("account:" + email.Trim().ToLowerInvariant(), out _);

    int Remaining(string key) =>
        entries.TryGetValue(key, out var entry) && entry.LockedUntil > DateTime.UtcNow ? (int)Math.Ceiling((entry.LockedUntil - DateTime.UtcNow).TotalSeconds) : 0;

    void Record(string key, int limit)
    {
        var now = DateTime.UtcNow;
        entries.AddOrUpdate(key, _ => Next(null, limit, now), (_, previous) => Next(previous, limit, now));
    }

    static Entry Next(Entry? previous, int limit, DateTime now)
    {
        var failures = (previous != null && now - previous.LastFailure < Memory ? previous.Failures : 0) + 1;
        // 30 s at the limit, doubling per further failure, capped at 30 minutes.
        var lockSeconds = failures < limit ? 0 : Math.Min(1800, 30 * Math.Pow(2, Math.Min(failures - limit, 6)));
        return new Entry(failures, lockSeconds == 0 ? DateTime.MinValue : now.AddSeconds(lockSeconds), now);
    }

    void Sweep()
    {
        if (entries.Count < 1000) return;
        foreach (var item in entries.Where(x => DateTime.UtcNow - x.Value.LastFailure > Memory)) entries.TryRemove(item.Key, out _);
    }
}
