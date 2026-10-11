using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Realtime;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

public sealed record ParticipantView(Guid UserId, string Email, DateTime AddedUtc, DateTime? LeftUtc);

public sealed record ShareView(Guid Id, string Title, string ClientConversationId, Guid HostId, string HostEmail, bool IsHost, DateTime CreatedUtc, DateTime? RevokedUtc, bool Live, IReadOnlyList<ParticipantView> Participants);

/// <summary>
/// Real-time sharing of a local conversation. The conversation and its tools stay on the host device; a bridge connection forwards
/// display events here. Viewers can read and comment, nothing else. The server keeps no history: the buffer exists only while the host
/// device is connected, so late joiners can catch up.
/// </summary>
public sealed class ShareService(IDbContextFactory<ServerDb> factory, SettingsService settings, AccessService access, RealtimeHub hub, AuditService audit)
{
    public const int MaxCommentLength = 2000;
    public const int MaxEventBytes = 256 * 1024;

    sealed class LiveShare
    {
        public readonly object Gate = new();
        public Guid? HostConnection;
        public JsonElement? Snapshot;
        public readonly Queue<RealtimeMessage> Events = new();
    }

    readonly ConcurrentDictionary<Guid, LiveShare> live = new();
    readonly ConcurrentDictionary<Guid, DateTime> lastComment = new();

    public bool IsLive(Guid id) => live.TryGetValue(id, out var share) && share.HostConnection != null;

    // ---- Management ----

    public async Task<ShareView> CreateAsync(ClaimsPrincipal user, string? title, string? clientConversationId)
    {
        access.Require(user, Permissions.SharingHost);
        title = (title ?? "").Trim();
        if (title.Length is 0 or > 200) throw ApiException.BadRequest("invalid_title", "A shared conversation title has 1 to 200 characters.");
        var share = new SharedConversation { HostId = user.RequireUserId(), Title = title, ClientConversationId = (clientConversationId ?? "").Trim()[..Math.Min((clientConversationId ?? "").Trim().Length, 100)] };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Shares.Add(share);
            await db.SaveChangesAsync();
        }
        await audit.WriteAsync("share.created", title, share.Id.ToString());
        return await GetAsync(user, share.Id);
    }

    public async Task<List<ShareView>> ListAsync(ClaimsPrincipal user)
    {
        var userId = user.RequireUserId();
        await using var db = await factory.CreateDbContextAsync();
        var invited = await db.ShareParticipants.AsNoTracking().Where(x => x.UserId == userId && x.LeftUtc == null).Select(x => x.ShareId).ToListAsync();
        var shares = await db.Shares.AsNoTracking().Where(x => x.HostId == userId || (invited.Contains(x.Id) && x.RevokedUtc == null)).OrderByDescending(x => x.CreatedUtc).ToListAsync();
        var result = new List<ShareView>();
        foreach (var share in shares) result.Add(await ToViewAsync(db, share, userId));
        return result;
    }

    public async Task<ShareView> GetAsync(ClaimsPrincipal user, Guid id)
    {
        var userId = user.RequireUserId();
        await using var db = await factory.CreateDbContextAsync();
        var share = await db.Shares.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Shared conversation not found.");
        if (share.HostId != userId && !await IsViewerAsync(db, id, userId)) throw ApiException.NotFound("Shared conversation not found.");
        return await ToViewAsync(db, share, userId);
    }

    public async Task<ShareView> InviteAsync(ClaimsPrincipal user, Guid id, string? email)
    {
        var share = await RequireHostAsync(user, id);
        if (share.RevokedUtc != null) throw ApiException.Conflict("share_revoked", "This sharing was revoked.");
        email = AccountService.NormalizeEmail(email);
        await using var db = await factory.CreateDbContextAsync();
        var target = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email && x.IsActive) ?? throw ApiException.BadRequest("unknown_user", "No active account uses " + email);
        if (target.Id == share.HostId) throw ApiException.BadRequest("self_invite", "You are the host of this conversation.");
        var current = await db.ShareParticipants.SingleOrDefaultAsync(x => x.ShareId == id && x.UserId == target.Id);
        if (current == null)
        {
            if (await db.ShareParticipants.CountAsync(x => x.ShareId == id && x.LeftUtc == null) >= settings.Int(SettingKeys.MaxViewers))
                throw ApiException.Conflict("too_many_viewers", "The maximum number of viewers is reached.");
            db.ShareParticipants.Add(new ShareParticipant { ShareId = id, UserId = target.Id });
        }
        else { current.LeftUtc = null; current.AddedUtc = DateTime.UtcNow; }
        await db.SaveChangesAsync();
        hub.PublishToUser(target.Id, "share.invited", new { shareId = id, share.Title });
        await audit.WriteAsync("share.invited", share.Title, email);
        return await GetAsync(user, id);
    }

    public async Task RemoveParticipantAsync(ClaimsPrincipal user, Guid id, Guid participant)
    {
        var share = await RequireHostAsync(user, id);
        await using (var db = await factory.CreateDbContextAsync())
            await db.ShareParticipants.Where(x => x.ShareId == id && x.UserId == participant).ExecuteDeleteAsync();
        Eject(id, participant, "removed");
        await audit.WriteAsync("share.participant_removed", share.Title, participant.ToString());
    }

    /// <summary>A viewer quits the discussion; the host can invite them again.</summary>
    public async Task LeaveAsync(ClaimsPrincipal user, Guid id)
    {
        var userId = user.RequireUserId();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var changed = await db.ShareParticipants.Where(x => x.ShareId == id && x.UserId == userId && x.LeftUtc == null).ExecuteUpdateAsync(set => set.SetProperty(x => x.LeftUtc, DateTime.UtcNow));
            if (changed == 0) throw ApiException.NotFound("Shared conversation not found.");
        }
        Eject(id, userId, "left");
    }

    /// <summary>The host ends the sharing for everybody. The row stays for the audit trail.</summary>
    public async Task RevokeAsync(ClaimsPrincipal user, Guid id)
    {
        var share = await RequireHostAsync(user, id);
        await using (var db = await factory.CreateDbContextAsync())
            await db.Shares.Where(x => x.Id == id && x.RevokedUtc == null).ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedUtc, DateTime.UtcNow));
        CloseLive(id, "revoked");
        await audit.WriteAsync("share.revoked", share.Title, id.ToString());
    }

    async Task<SharedConversation> RequireHostAsync(ClaimsPrincipal user, Guid id)
    {
        access.Require(user, Permissions.SharingHost);
        var userId = user.RequireUserId();
        await using var db = await factory.CreateDbContextAsync();
        return await db.Shares.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.HostId == userId) ?? throw ApiException.NotFound("Shared conversation not found.");
    }

    static Task<bool> IsViewerAsync(ServerDb db, Guid shareId, Guid userId) =>
        db.ShareParticipants.AnyAsync(x => x.ShareId == shareId && x.UserId == userId && x.LeftUtc == null && db.Shares.Any(s => s.Id == shareId && s.RevokedUtc == null));

    async Task<ShareView> ToViewAsync(ServerDb db, SharedConversation share, Guid viewer)
    {
        var host = await db.Users.AsNoTracking().Where(x => x.Id == share.HostId).Select(x => x.Email).SingleAsync();
        var participants = share.HostId == viewer
            ? await (from p in db.ShareParticipants.AsNoTracking() join u in db.Users.AsNoTracking() on p.UserId equals u.Id where p.ShareId == share.Id orderby u.Email select new ParticipantView(u.Id, u.Email, p.AddedUtc, p.LeftUtc)).ToListAsync()
            : [];
        return new ShareView(share.Id, share.Title, share.ClientConversationId, share.HostId, host, share.HostId == viewer, share.CreatedUtc, share.RevokedUtc, IsLive(share.Id), participants);
    }

    // ---- Bridge (host device) ----

    public async Task AttachAsync(RealtimeConnection connection, Guid id)
    {
        access.Require(connection.User, Permissions.SharingHost);
        var share = await RequireHostAsync(connection.User!, id);
        if (share.RevokedUtc != null) throw ApiException.Conflict("share_revoked", "This sharing was revoked.");
        var state = live.GetOrAdd(id, _ => new());
        lock (state.Gate)
        {
            state.HostConnection = connection.Id;
            state.Snapshot = null;
            state.Events.Clear();
            hub.Subscribe(connection, RealtimeHub.ShareTopic(id));
            hub.Publish(RealtimeHub.ShareTopic(id), "bridge", new { online = true });
        }
    }

    public void Snapshot(RealtimeConnection connection, Guid id, JsonElement data)
    {
        var state = RequireBridge(connection, id);
        lock (state.Gate)
        {
            // A snapshot supersedes everything before it.
            state.Snapshot = data.Clone();
            state.Events.Clear();
            hub.Publish(RealtimeHub.ShareTopic(id), "snapshot", data);
        }
    }

    public void Event(RealtimeConnection connection, Guid id, string name, JsonElement data)
    {
        var state = RequireBridge(connection, id);
        if (name is "bridge" or "snapshot" or "comment" || name.Length is 0 or > 60) throw ApiException.BadRequest("invalid_event", "This event name is reserved or invalid.");
        Publish(id, state, name, data);
    }

    /// <summary>The device went away: the live copy is dropped, nothing is kept server-side.</summary>
    public void Disconnected(RealtimeConnection connection)
    {
        foreach (var (id, state) in live.ToList())
        {
            if (state.HostConnection != connection.Id) continue;
            lock (state.Gate)
            {
                state.HostConnection = null;
                state.Snapshot = null;
                state.Events.Clear();
                hub.Publish(RealtimeHub.ShareTopic(id), "bridge", new { online = false });
            }
        }
    }

    LiveShare RequireBridge(RealtimeConnection connection, Guid id)
    {
        if (!live.TryGetValue(id, out var state) || state.HostConnection != connection.Id) throw ApiException.Forbidden("not_bridge", "This connection is not the bridge of the shared conversation.");
        return state;
    }

    void Publish(Guid id, LiveShare state, string name, JsonElement data)
    {
        lock (state.Gate)
        {
            state.Events.Enqueue(new RealtimeMessage(RealtimeHub.ShareTopic(id), name, data.Clone(), DateTime.UtcNow));
            while (state.Events.Count > settings.Int(SettingKeys.BufferEvents)) state.Events.Dequeue();
            hub.Publish(RealtimeHub.ShareTopic(id), name, data);
        }
    }

    // ---- Viewers ----

    /// <summary>Feature, permission and share rights needed to follow a shared conversation (host or invited viewer).</summary>
    async Task AuthorizeViewAsync(ClaimsPrincipal? user, Guid id)
    {
        access.RequireFeature(Features.Sharing);
        var userId = user.RequireUserId();
        await using var db = await factory.CreateDbContextAsync();
        var share = await db.Shares.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Shared conversation not found.");
        if (share.RevokedUtc != null) throw ApiException.NotFound("Shared conversation not found.");
        if (share.HostId == userId) access.Require(user, Permissions.SharingHost);
        else
        {
            access.Require(user, Permissions.SharingView);
            if (!await IsViewerAsync(db, id, userId)) throw ApiException.NotFound("Shared conversation not found.");
        }
    }

    /// <summary>Subscribes a WebSocket viewer (or the host) and replays the current state, atomically with respect to new events.</summary>
    public async Task SubscribeAsync(RealtimeConnection connection, Guid id)
    {
        await AuthorizeViewAsync(connection.User, id);
        var state = live.GetOrAdd(id, _ => new());
        lock (state.Gate)
        {
            hub.Subscribe(connection, RealtimeHub.ShareTopic(id));
            foreach (var message in Replay(id, state)) hub.SendEvent(connection, message);
        }
    }

    /// <summary>Same as <see cref="SubscribeAsync"/> for the in-process web interface: the callback receives the replay, then live events.</summary>
    public async Task<IDisposable> WatchAsync(ClaimsPrincipal user, Guid id, Action<RealtimeMessage> callback)
    {
        await AuthorizeViewAsync(user, id);
        var state = live.GetOrAdd(id, _ => new());
        lock (state.Gate)
        {
            var subscription = hub.Listen(RealtimeHub.ShareTopic(id), callback);
            foreach (var message in Replay(id, state)) callback(message);
            return subscription;
        }
    }

    IEnumerable<RealtimeMessage> Replay(Guid id, LiveShare state)
    {
        var topic = RealtimeHub.ShareTopic(id);
        yield return new RealtimeMessage(topic, "bridge", JsonSerializer.SerializeToElement(new { online = state.HostConnection != null }, RealtimeHub.Json), DateTime.UtcNow);
        if (state.Snapshot is { } snapshot) yield return new RealtimeMessage(topic, "snapshot", snapshot, DateTime.UtcNow);
        foreach (var message in state.Events) yield return message;
    }

    /// <summary>Viewers may add a comment and nothing more: it is shown to everybody and reaches the host device.</summary>
    public async Task CommentAsync(ClaimsPrincipal? user, Guid id, string? text)
    {
        access.Require(user, Permissions.SharingComment);
        var userId = user.RequireUserId();
        text = (text ?? "").Trim();
        if (text.Length is 0 || text.Length > MaxCommentLength) throw ApiException.BadRequest("invalid_comment", $"A comment has 1 to {MaxCommentLength} characters.");
        var now = DateTime.UtcNow;
        if (lastComment.TryGetValue(userId, out var previous) && now - previous < TimeSpan.FromSeconds(1)) throw ApiException.Throttled(1);
        lastComment[userId] = now;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var share = await db.Shares.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.RevokedUtc == null) ?? throw ApiException.NotFound("Shared conversation not found.");
            if (share.HostId != userId && !await IsViewerAsync(db, id, userId)) throw ApiException.NotFound("Shared conversation not found.");
        }
        if (!live.TryGetValue(id, out var state) || state.HostConnection == null) throw ApiException.Conflict("host_offline", "The host device is offline.");
        Publish(id, state, "comment", JsonSerializer.SerializeToElement(new { id = Guid.NewGuid(), userId, author = user.Email(), text, time = now }, RealtimeHub.Json));
    }

    // ---- Closing ----

    void Eject(Guid id, Guid userId, string reason)
    {
        hub.PublishToUser(userId, "share.closed", new { shareId = id, reason });
        hub.Unsubscribe(RealtimeHub.ShareTopic(id), c => c.UserId == userId);
    }

    void CloseLive(Guid id, string reason)
    {
        hub.Publish(RealtimeHub.ShareTopic(id), "closed", new { reason });
        hub.Unsubscribe(RealtimeHub.ShareTopic(id), _ => true);
        live.TryRemove(id, out _);
    }

    /// <summary>Disabling the feature ends every live sharing at once.</summary>
    public void CloseAll(string reason)
    {
        foreach (var id in live.Keys.ToList()) CloseLive(id, reason);
    }
}
