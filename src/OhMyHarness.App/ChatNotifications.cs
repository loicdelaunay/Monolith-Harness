using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;
using System.Runtime.InteropServices;

namespace OhMyHarness.App;

static class NotificationAudio
{
    static readonly SemaphoreSlim gate = new(1, 1);
    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    static extern bool PlaySound(byte[] data, IntPtr module, uint flags);
    public static async Task PlayAsync(string tone, int volume)
    {
        if (!OperatingSystem.IsWindows() || volume <= 0 || !await gate.WaitAsync(0)) return;
        try
        {
            await Task.Run(() =>
            {
                const int rate = 22050;
                (double Start, double Frequency, double Duration)[] notes = tone switch {
                    "chime" => [(0, 659.25, .7), (.2, 987.77, .8)],
                    "glass" => [(0, 1567.98, .9), (.08, 2093, .8)],
                    "marimba" => [(0, 523.25, .35), (.14, 659.25, .35), (.28, 783.99, .4)],
                    "digital" => [(0, 1046.5, .12), (.2, 1318.51, .16)],
                    "success" => [(0, 523.25, .4), (.15, 659.25, .4), (.3, 783.99, .4), (.48, 1046.5, .6)],
                    "water" => [(0, 1200, .5)],
                    "bell" => [(0, 880, .6)], "alert" => [(0, 660, .5)],
                    _ => [(0, 523.25, .5)]
                };
                int samples = (int)(notes.Max(x => x.Start + x.Duration) * rate);
                using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
                writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write("data"u8); writer.Write(samples * 2);
                for (int i = 0; i < samples; i++)
                {
                    double wave = 0, time = i / (double)rate;
                    foreach (var note in notes)
                    {
                        double t = time - note.Start;
                        if (t < 0 || t >= note.Duration) continue;
                        double decay = tone == "marimba" ? 16 : tone == "glass" ? 5 : 8;
                        double envelope = Math.Min(1, t * 100) * Math.Exp(-t * decay) * Math.Min(1, (note.Duration - t) * 80);
                        double phase = 2 * Math.PI * (tone == "water" ? note.Frequency * t - 750 * t * t : note.Frequency * t);
                        double harmonic = tone == "bell" || tone == "glass" ? 2.76 : tone == "digital" ? 3 : 1.5;
                        wave += (Math.Sin(phase) + .25 * Math.Sin(phase * harmonic)) * envelope;
                    }
                    writer.Write((short)(Math.Clamp(wave * .45, -1, 1) * Math.Clamp(volume, 0, 100) / 100 * short.MaxValue));
                }
                // Synchronous playback on a worker keeps the WAV buffer alive until playback ends.
                PlaySound(memory.ToArray(), IntPtr.Zero, 0x0004 | 0x0002);
            });
        }
        catch (Exception ex) { AppLog.Write(AppLogLevel.Warning, "notification.sound_failed", ex); }
        finally { gate.Release(); }
    }
}

public sealed partial class MainWindow
{
    sealed record ChatNotice(int ChatId, string Title, bool ActionRequired, DateTime CreatedUtc);
    readonly List<ChatNotice> chatNotices = [];
    Button? notificationBell;
    Button BuildNotificationBell()
    {
        notificationBell = new Button { Padding = new(5, 0, 5, 0), MinWidth = 30, Height = 30, BorderThickness = new(0), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        notificationBell.Click += (_, _) =>
        {
            var flyout = new Flyout();
            var panel = new StackPanel { Spacing = 8, Width = 320 };
            panel.Children.Add(Label(WorkflowText("Notifications", "Notifications"), 18));
            if (chatNotices.Count == 0) panel.Children.Add(Label(WorkflowText("Aucune notification", "No notifications"), 13));
            foreach (var notice in chatNotices.AsEnumerable().Reverse().Take(30))
            {
                var button = Action(notice.Title + "\n" + (notice.ActionRequired ? WorkflowText("Action requise", "Action required") : WorkflowText("Conversation terminée", "Conversation completed")), async () =>
                {
                    flyout.Hide(); AcknowledgeChatNotice(notice.ChatId); await OpenConversationByIdAsync(notice.ChatId);
                });
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.Content = new TextBlock { Text = button.Content?.ToString(), TextWrapping = TextWrapping.Wrap };
                panel.Children.Add(button);
            }
            var clear = Action(WorkflowText("Tout marquer comme lu", "Mark all as read"), () => { chatNotices.Clear(); ClearUnreadProjectActivity(); RefreshNotificationBell(); RefreshConversationProgress(); flyout.Hide(); return Task.CompletedTask; });
            clear.HorizontalAlignment = HorizontalAlignment.Right; panel.Children.Add(clear);
            flyout.Content = new ScrollViewer { Content = panel, MaxHeight = 480 }; flyout.ShowAt(notificationBell);
        };
        RefreshNotificationBell(); return notificationBell;
    }
    void RefreshNotificationBell()
    {
        if (notificationBell == null) return;
        notificationBell.Visibility = FeatureSettings.Read(state.FeaturesJson).NotificationBell ? Visibility.Visible : Visibility.Collapsed;
        var content = Row(FluentDesign.Icon("\uEA8F", 16));
        if (chatNotices.Count > 0) content.Children.Add(Label(chatNotices.Count.ToString(), 11));
        notificationBell.Content = content;
        ToolTipService.SetToolTip(notificationBell, WorkflowText("Notifications", "Notifications"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(notificationBell, WorkflowText($"Notifications : {chatNotices.Count}", $"Notifications: {chatNotices.Count}"));
    }
    void NotifyChat(ConversationRun run, bool actionRequired)
    {
        MarkProjectChatNotification(run, actionRequired);
        var settings = FeatureSettings.Read(state.FeaturesJson);
        if (actionRequired ? !settings.NotifyActionRequired : !settings.NotifyCompleted) return;
        chatNotices.RemoveAll(x => x.ChatId == run.Chat.Id);
        if (settings.NotificationBell) chatNotices.Add(new(run.Chat.Id, run.Chat.Title, actionRequired, DateTime.UtcNow));
        if (chatNotices.Count > 100) chatNotices.RemoveAt(0);
        RefreshNotificationBell(); RefreshConversationProgress();
        if (settings.NotificationSound) _ = NotificationAudio.PlayAsync(settings.NotificationTone, settings.NotificationVolume);
        if (settings.NotificationOs) _ = SystemNotifications.ShowAsync(DisplayApplicationName, run.Chat.Title,
            actionRequired ? WorkflowText("Action requise", "Action required") : WorkflowText("Conversation terminée", "Conversation completed"));
    }
    void AcknowledgeChatNotice(int id, bool actionResolved = false)
    {
        ReadProjectChatActivity(id, actionResolved);
        if (!pendingChatAttention.ContainsKey(id)) chatNotices.RemoveAll(x => x.ChatId == id); RefreshNotificationBell(); RefreshConversationProgress();
    }
    async Task OpenConversationByIdAsync(int id)
    {
        var target = await ReadStoreAsync(store => store.Chats.AsNoTracking().FirstOrDefault(x => x.Id == id));
        if (target == null) return;
        var owner = db.Projects.Local.FirstOrDefault(x => x.Id == target.ProjectId);
        if (owner == null) return;
        loading = true; projects.SelectedItem = owner; state.ChatId = id; loading = false;
        collapsedSidebarProjects.Remove(owner.Id);
        await SelectProject(); Activate();
    }
}
