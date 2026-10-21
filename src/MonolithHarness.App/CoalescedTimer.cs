using Microsoft.UI.Xaml;

namespace MonolithHarness.App;

// One UI wake-up queue for infrequent maintenance. No polling when every job is stopped.
sealed class CoalescedTimer
{
    static readonly DispatcherTimer clock = new();
    static readonly HashSet<CoalescedTimer> active = [];
    public static long WakeCount { get; private set; }
    static CoalescedTimer() => clock.Tick += (_, _) => Dispatch();
    TimeSpan interval = TimeSpan.FromHours(1);
    DateTimeOffset due;
    public TimeSpan Interval
    {
        get => interval;
        set { if (value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(value)); interval = value; if (IsEnabled) { due = DateTimeOffset.UtcNow + interval; Arm(); } }
    }
    public bool IsEnabled => active.Contains(this);
    public event EventHandler<object>? Tick;
    public void Start() { if (!active.Add(this)) return; due = DateTimeOffset.UtcNow + interval; Arm(); }
    public void Stop() { if (active.Remove(this)) Arm(); }
    static void Arm()
    {
        clock.Stop();
        if (active.Count == 0) return;
        clock.Interval = TimeSpan.FromMilliseconds(Math.Clamp((active.Min(job => job.due) - DateTimeOffset.UtcNow).TotalMilliseconds, 1, int.MaxValue));
        clock.Start();
    }
    static void Dispatch()
    {
        WakeCount++; clock.Stop();
        var now = DateTimeOffset.UtcNow;
        foreach (var job in active.Where(job => job.due <= now.AddMilliseconds(100)).ToArray())
        {
            if (!job.IsEnabled) continue;
            job.due = now + job.interval;
            job.Tick?.Invoke(job, EventArgs.Empty);
        }
        Arm();
    }
}
