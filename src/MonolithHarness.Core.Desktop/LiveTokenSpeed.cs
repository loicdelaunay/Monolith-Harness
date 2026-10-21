using System.Collections.Concurrent;
using System.Diagnostics;

namespace MonolithHarness.Core;

public static partial class TokenConsumption
{
    public sealed record LiveRate(int ChatId, double TokensPerSecond);
    static readonly ConcurrentDictionary<Guid, LiveSpeed> liveSpeeds = new();

    /// <summary>In-memory counters only, including concurrent subagents; never copies model text or credentials.</summary>
    public static IReadOnlyList<LiveRate> LiveRates(string database) => liveSpeeds.Values
        .Where(speed => PlatformSupport.PathComparer.Equals(speed.Database, database))
        .Select(speed => new LiveRate(speed.ChatId, speed.Read())).ToArray();

    /// <summary>Start/end notifications only; observers must not affect model execution.</summary>
    public static event Action<string>? LiveActivityChanged;
    public static bool HasLiveGenerations(string database) => liveSpeeds.Values.Any(speed => PlatformSupport.PathComparer.Equals(speed.Database, database));
    static void NotifyLiveActivity(string database)
    {
        if (LiveActivityChanged is not { } handlers) return;
        foreach (Action<string> handler in handlers.GetInvocationList())
            try { handler(database); } catch (Exception ex) { System.Diagnostics.Trace.TraceError("Live activity observer: " + ex.Message); }
    }

    sealed class LiveSpeed(string database, int chatId)
    {
        public string Database { get; } = database;
        public int ChatId { get; } = chatId;
        readonly object sync = new();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly List<(double At, double Tokens)> samples = [(0, 0)];
        bool? estimated;
        double epoch;
        double? frozen;

        public void Observe(GenerationUpdate update)
        {
            if (update.Retry != null) return;
            var tokens = update.OutputTokens ?? Math.Ceiling((update.Text.Length + update.Reasoning.Length) / 4d);
            lock (sync)
            {
                var now = clock.Elapsed.TotalSeconds;
                var approximation = !update.OutputTokens.HasValue;
                if (estimated.HasValue && (estimated != approximation || tokens < samples[^1].Tokens))
                {
                    frozen = Rate(now); epoch = now; samples.Clear(); samples.Add((now, tokens));
                }
                estimated = approximation;
                if (samples.Count > 1 && now - samples[^1].At < .02) samples[^1] = (now, tokens);
                else samples.Add((now, tokens));
                Prune(now);
                if (samples.Count > 1 && samples[^1].At > samples[0].At) frozen = null;
            }
        }
        void Prune(double now)
        {
            while (samples.Count > 1 && samples[1].At <= now - 2) samples.RemoveAt(0);
        }
        double Rate(double now)
        {
            Prune(now);
            return frozen ?? Math.Max(0, samples[^1].Tokens - samples[0].Tokens) / Math.Max(.1, Math.Min(2, now - epoch));
        }
        public double Read() { lock (sync) return Rate(clock.Elapsed.TotalSeconds); }
    }
}
