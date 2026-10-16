using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    enum StatusKind { Activity, Notice, Error }
    readonly record struct StatusEntry(string Text, StatusKind Kind, DateTimeOffset? ExpiresAt);

    readonly DispatcherTimer statusPulseTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    Storyboard? statusGlowAnimation;
    readonly DispatcherTimer statusActivityGlow = new() { Interval = TimeSpan.FromMilliseconds(80) };
    DateTimeOffset statusActivityGlowStarted;
    bool statusActivityAnimating;
    Grid? statusGlowLayer;
    Border? statusChipView;
    StatusKind visibleStatusKind;
    DateTimeOffset visibleStatusStarted;
    DateTimeOffset? visibleStatusExpiresAt;
    int? visibleStatusChatId;
    bool IsTransientOverlayVisible => visibleStatusKind == StatusKind.Notice && visibleStatusChatId == null
        && visibleStatusExpiresAt is { } expiry && expiry > DateTimeOffset.UtcNow && !string.IsNullOrWhiteSpace(status.Text);

    static DateTimeOffset? StatusExpiry(string text, StatusKind kind) => kind == StatusKind.Notice
        ? DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(2 + text.Length / 20d, 4.5, 8)) : null;

    void ConnectStatusGlow(Border chip, Grid glow)
    {
        statusChipView = chip;
        statusGlowLayer = glow;
        glow.Opacity = 0;
        statusPulseTimer.Tick += (_, _) => AnimateStatus();
        statusActivityGlow.Tick += (_, _) => PulseActivityGlow();
        Closed += (_, _) => { statusPulseTimer.Stop(); statusActivityGlow.Stop(); statusGlowAnimation?.Stop(); };
    }

    void ShowStatus(string text, StatusKind kind = StatusKind.Notice, int? chatId = null, DateTimeOffset? expiresAt = null)
    {
        if (string.IsNullOrWhiteSpace(text)) { ClearStatus(); return; }
        var now = DateTimeOffset.UtcNow;
        var continuing = kind == StatusKind.Activity && visibleStatusKind == StatusKind.Activity
            && visibleStatusChatId == chatId && !string.IsNullOrWhiteSpace(status.Text);
        visibleStatusKind = kind;
        if (!continuing) visibleStatusStarted = now;
        visibleStatusExpiresAt = expiresAt ?? StatusExpiry(text, kind);
        visibleStatusChatId = chatId;
        status.Text = text;
        if (statusChipView != null) statusChipView.Opacity = 1;
        if (!continuing) StartStatusGlow(kind);
        if (kind == StatusKind.Notice) statusPulseTimer.Start(); else statusPulseTimer.Stop();
    }

    void ClearStatus()
    {
        statusPulseTimer.Stop(); statusActivityGlow.Stop(); statusGlowAnimation?.Stop(); statusActivityAnimating = false;
        status.Text = "";
        visibleStatusExpiresAt = null;
        visibleStatusChatId = null;
        if (statusChipView != null) statusChipView.Opacity = 1;
        if (statusGlowLayer != null) statusGlowLayer.Opacity = 0;
    }

    void RestoreConversationStatus()
    {
        if (chat != null && conversationStatuses.TryGetValue(chat.Id, out var entry))
        {
            if (entry.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
                conversationStatuses.Remove(chat.Id);
            else { ShowStatus(entry.Text, entry.Kind, chat.Id, entry.ExpiresAt); return; }
        }
        ClearStatus();
    }

    void StartStatusGlow(StatusKind kind)
    {
        if (statusGlowLayer == null) return;
        if (kind == StatusKind.Activity && statusActivityAnimating) return;
        statusGlowAnimation?.Stop(); statusActivityGlow.Stop(); statusActivityAnimating = kind == StatusKind.Activity;
        statusGlowLayer.Opacity = kind == StatusKind.Activity ? .18 : 0;
        if (kind == StatusKind.Activity)
        {
            // An activity can last minutes: pulse at about 12 steps per second instead of a
            // storyboard that repaints the window on every display frame.
            statusActivityGlowStarted = DateTimeOffset.UtcNow; statusActivityGlow.Start();
            return;
        }
        var cycle = 1.4;
        var pulse = new DoubleAnimationUsingKeyFrames {
            Duration = new Duration(TimeSpan.FromSeconds(cycle)), RepeatBehavior = new RepeatBehavior(1), EnableDependentAnimation = true };
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 0, KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero) });
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame { Value = .85,
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(cycle / 2)), EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame { Value = 0, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(cycle)),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        Storyboard.SetTarget(pulse, statusGlowLayer); Storyboard.SetTargetProperty(pulse, "Opacity");
        statusGlowAnimation = new(); statusGlowAnimation.Children.Add(pulse); statusGlowAnimation.Begin();
    }

    void PulseActivityGlow()
    {
        if (statusGlowLayer == null || !statusActivityAnimating) { statusActivityGlow.Stop(); return; }
        const double cycle = 2.4, low = .18, high = .72;
        var phase = (DateTimeOffset.UtcNow - statusActivityGlowStarted).TotalSeconds / cycle;
        statusGlowLayer.Opacity = low + (high - low) * (.5 - .5 * Math.Cos(2 * Math.PI * phase));
    }

    void AnimateStatus()
    {
        if (string.IsNullOrWhiteSpace(status.Text)) { statusPulseTimer.Stop(); return; }
        var now = DateTimeOffset.UtcNow;
        if (visibleStatusKind == StatusKind.Notice && visibleStatusExpiresAt is { } expiry)
        {
            var remaining = (expiry - now).TotalSeconds;
            if (remaining <= 0)
            {
                if (visibleStatusChatId is { } owner && conversationStatuses.TryGetValue(owner, out var entry)
                    && entry.ExpiresAt is { } savedExpiry && savedExpiry <= now)
                    conversationStatuses.Remove(owner);
                RestoreConversationStatus();
                return;
            }
            if (statusChipView != null) statusChipView.Opacity = Math.Clamp(remaining / .35, 0, 1);
        }

    }
}
