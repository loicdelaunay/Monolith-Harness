using System.Globalization;
using MonolithHarness.Server.Realtime;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

/// <summary>The message of the moment: its text, how serious it is and until when it is shown.</summary>
public sealed record Announcement(string Text, string Level, DateTime UpdatedUtc, DateTime? ExpiresUtc, string By);

public static class AnnouncementLevels
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
    public static readonly string[] All = [Info, Warning, Error];
}

/// <summary>
/// A single message an administrator puts in front of everybody (maintenance, incident, news), with an alert level. It is stored with the
/// settings, shown by the web interface, returned by <c>GET /api/server</c> and pushed live on the <c>server</c> topic so that the clients
/// (Desktop band, CLI header) follow it without polling.
/// </summary>
public sealed class AnnouncementService(SettingsService settings, RealtimeHub hub)
{
    public const string Event = "announcement";
    public const int MaxLength = 500;
    const string TextKey = "announcement.text", LevelKey = "announcement.level", UpdatedKey = "announcement.updatedUtc", ExpiresKey = "announcement.expiresUtc", ByKey = "announcement.by";

    /// <summary>The message to show now, or null when there is none or its time is over.</summary>
    public Announcement? Current()
    {
        var text = settings.Text(TextKey);
        if (text.Length == 0) return null;
        var expires = Parse(settings.Text(ExpiresKey));
        if (expires is { } end && end <= DateTime.UtcNow) return null;
        var level = settings.Text(LevelKey, AnnouncementLevels.Info);
        if (!AnnouncementLevels.All.Contains(level)) level = AnnouncementLevels.Info;
        return new Announcement(text, level, Parse(settings.Text(UpdatedKey)) ?? DateTime.UtcNow, expires, settings.Text(ByKey));
    }

    public async Task<Announcement> SetAsync(string? text, string? level, int? expiresMinutes, string by)
    {
        text = Clean(text);
        if (text.Length == 0) throw ApiException.BadRequest("empty_announcement", "The message is empty.");
        if (text.Length > MaxLength) throw ApiException.BadRequest("announcement_too_long", $"The message is limited to {MaxLength} characters.");
        level = (level ?? AnnouncementLevels.Info).Trim().ToLowerInvariant();
        if (!AnnouncementLevels.All.Contains(level)) throw ApiException.BadRequest("invalid_level", "The level must be info, warning or error.");
        if (expiresMinutes is < 1 or > 60 * 24 * 365) throw ApiException.BadRequest("invalid_duration", "The duration must be between one minute and one year.");
        var now = DateTime.UtcNow;
        await settings.SetTextAsync(TextKey, text);
        await settings.SetTextAsync(LevelKey, level);
        await settings.SetTextAsync(UpdatedKey, now.ToString("O", CultureInfo.InvariantCulture));
        await settings.SetTextAsync(ByKey, by);
        if (expiresMinutes is { } minutes) await settings.SetTextAsync(ExpiresKey, now.AddMinutes(minutes).ToString("O", CultureInfo.InvariantCulture));
        else await settings.RemoveTextAsync(ExpiresKey);
        var current = Current()!;
        hub.Publish(RealtimeHub.ServerTopic, Event, current);
        return current;
    }

    public async Task ClearAsync()
    {
        if (settings.Text(TextKey).Length == 0) return;
        await settings.RemoveTextAsync(TextKey, LevelKey, UpdatedKey, ExpiresKey, ByKey);
        hub.Publish(RealtimeHub.ServerTopic, Event, null);
    }

    /// <summary>One line of printable text: the message ends up in banners and terminals.</summary>
    static string Clean(string? text) => string.Join(' ', (text ?? "").Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Where(c => !char.IsControl(c)).Aggregate(new System.Text.StringBuilder(), (builder, c) => builder.Append(c)).ToString().Trim();

    static DateTime? Parse(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
}
