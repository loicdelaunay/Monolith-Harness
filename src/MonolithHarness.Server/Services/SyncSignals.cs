using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Realtime;

namespace MonolithHarness.Server.Services;

/// <summary>
/// Tells connected clients that something they mirror changed (providers, cloud projects, conversations, shared conversations) so that they
/// refresh by themselves. The event carries no data, only the area: the client reads the new state through the API, with its own rights.
/// </summary>
public sealed class SyncSignals(IDbContextFactory<ServerDb> factory, RealtimeHub hub)
{
    public const string Event = "sync";

    /// <summary>The provider catalog, or who may use it, changed: everybody re-reads it.</summary>
    public void Providers() => hub.Publish(RealtimeHub.ServerTopic, Event, new { area = "providers" });

    /// <summary>A shared conversation became public, private or disappeared: everybody re-reads the public list.</summary>
    public void PublicShares() => hub.Publish(RealtimeHub.ServerTopic, Event, new { area = "shares" });

    public void Shares(params Guid[] users) => ToUsers(users, "shares");

    public void ToUsers(IEnumerable<Guid> users, string area, Guid? project = null, int? chat = null)
    {
        foreach (var user in users.Distinct()) hub.PublishToUser(user, Event, new { area, projectId = project, chatId = chat });
    }

    /// <summary>The owner and every member of a cloud project (plus users who just left it).</summary>
    public async Task ProjectAsync(Guid project, string area, int? chat = null, IEnumerable<Guid>? also = null)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var owner = await db.Projects.AsNoTracking().Where(x => x.Id == project).Select(x => (Guid?)x.OwnerId).SingleOrDefaultAsync();
            var members = await db.ProjectMembers.AsNoTracking().Where(x => x.ProjectId == project).Select(x => x.UserId).ToListAsync();
            var users = members.Concat(also ?? []);
            if (owner is { } id) users = users.Append(id);
            ToUsers(users, area, project, chat);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException or Microsoft.Data.Sqlite.SqliteException or ObjectDisposedException)
        {
            // A missed signal only delays the clients' next periodic resynchronization.
        }
    }
}
