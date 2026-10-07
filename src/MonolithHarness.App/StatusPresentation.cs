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
        Closed += (_, _) => { statusPulseTimer.Stop(); statusGlowAnimation?.Stop(); };
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
        statusPulseTimer.Stop(); statusGlowAnimation?.Stop(); statusActivityAnimating = false;
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
        if (kind == StatusKind.Activity && statusActivityAnimating && statusGlowAnimation != null) return;
        statusGlowAnimation?.Stop(); statusActivityAnimating = kind == StatusKind.Activity;
        statusGlowLayer.Opacity = kind == StatusKind.Activity ? .18 : 0;
        var cycle = kind == StatusKind.Activity ? 2.4 : 1.4;
        var low = kind == StatusKind.Activity ? .18 : 0;
        var pulse = new DoubleAnimationUsingKeyFrames {
            Duration = new Duration(TimeSpan.FromSeconds(cycle)),
            RepeatBehavior = kind == StatusKind.Activity ? RepeatBehavior.Forever : new RepeatBehavior(1), EnableDependentAnimation = true };
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame { Value = low, KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero) });
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame { Value = kind == StatusKind.Activity ? .72 : .85,
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(cycle / 2)), EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame { Value = low, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(cycle)),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        Storyboard.SetTarget(pulse, statusGlowLayer); Storyboard.SetTargetProperty(pulse, "Opacity");
        statusGlowAnimation = new(); statusGlowAnimation.Children.Add(pulse); statusGlowAnimation.Begin();
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
