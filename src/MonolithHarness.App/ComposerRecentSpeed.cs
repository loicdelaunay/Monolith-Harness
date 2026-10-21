using System.Diagnostics;
using Microsoft.UI.Xaml;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly DispatcherTimer composerSpeedTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly Dictionary<int, (double Rate, bool Estimated)> recentComposerSpeeds = [];
    bool composerSpeedInitialized;
    void EnsureComposerSpeedTimer()
    {
        if (!composerSpeedInitialized)
        {
            composerSpeedInitialized = true;
            composerSpeedTimer.Tick += (_, _) => { RecordUiWork("speed"); RefreshGlobalTokenSpeed(); RefreshRecentComposerSpeed(); };
            Closed += (_, _) => composerSpeedTimer.Stop();
        }
    }
    void ObserveComposerSpeed(ConversationRun run, GenerationUpdate update)
    {
        EnsureComposerSpeedTimer();
        run.RecentSpeed.Observe(run.Tracker, update);
        RefreshSpeedTimer();
    }
    void SaveRecentComposerSpeed(ConversationRun run)
    {
        if (run.RecentSpeed.Read() is { } value) recentComposerSpeeds[run.Chat.Id] = value;
    }
    void RefreshRecentComposerSpeed()
    {
        var value = selectedSubagent == null ? ActiveRun?.RecentSpeed.Read() : null;
        if (selectedSubagent == null && value == null && chat != null && recentComposerSpeeds.TryGetValue(chat.Id, out var previous)) value = previous;
        if (VisualUpdatesAllowed) SetTextIfChanged(composerSpeedText, value is { } speed ? $"⚡ {speed.Rate:F1} tok/s" : "⚡ — tok/s");
        RefreshSpeedTimer();
    }
    void RefreshSpeedTimer()
    {
        var needed = VisualUpdatesAllowed &&
            (globalTokenSpeedEnabled && TokenConsumption.HasLiveGenerations(db.FilePath) ||
             selectedSubagent == null && ActiveRun is { WaitingForModel: true, Tracker: not null } && composerSpeedIndicator.Visibility == Visibility.Visible);
        if (needed) { EnsureComposerSpeedTimer(); if (!composerSpeedTimer.IsEnabled) composerSpeedTimer.Start(); }
        else composerSpeedTimer.Stop();
    }
    sealed class RecentTokenSpeed
    {
        readonly Stopwatch clock = new();
        readonly List<(double At, double Tokens)> samples = [];
        object? generation;
        bool estimated;
        double? frozen;
        double epoch;
        public void Begin(object? source)
        {
            generation = source; frozen = null; clock.Restart(); epoch = 0; samples.Clear(); samples.Add((0, 0));
        }
        public void Observe(object? source, GenerationUpdate update)
        {
            var tokens = update.OutputTokens ?? Math.Ceiling((update.Text.Length + update.Reasoning.Length) / 4d);
            var approximation = !update.OutputTokens.HasValue;
            if (samples.Count == 1 && samples[0].Tokens == 0) estimated = approximation;
            if (!ReferenceEquals(generation, source) || samples.Count == 0)
            {
                Begin(source); estimated = approximation;
            }
            // Provider totals can arrive only at completion. Do not count that basis change as a burst.
            if (estimated != approximation || tokens < samples[^1].Tokens)
            {
                var previous = Read(); estimated = approximation;
                epoch = clock.Elapsed.TotalSeconds; samples.Clear(); samples.Add((epoch, tokens)); frozen = previous?.Rate;
            }
            var now = clock.Elapsed.TotalSeconds;
            if (samples.Count > 1 && now - samples[^1].At < .02) samples[^1] = (now, tokens);
            else samples.Add((now, tokens));
            Prune(now); if (samples.Count > 1 && samples[^1].At > samples[0].At) frozen = null;
        }
        void Prune(double now)
        {
            var cutoff = Math.Max(0, now - 2);
            while (samples.Count > 1 && samples[1].At <= cutoff) samples.RemoveAt(0);
        }
        public (double Rate, bool Estimated)? Read()
        {
            if (samples.Count == 0) return null;
            if (frozen.HasValue) return (frozen.Value, estimated);
            var now = clock.Elapsed.TotalSeconds; Prune(now);
            var baseline = samples[0].Tokens;
            return (Math.Max(0, samples[^1].Tokens - baseline) / Math.Max(.1, Math.Min(2, now - epoch)), estimated);
        }
        public void Freeze()
        {
            if (Read() is { } value) frozen = value.Rate;
            clock.Stop();
        }
    }
}
