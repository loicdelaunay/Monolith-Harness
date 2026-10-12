using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Realtime;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

public sealed record ProjectView(Guid Id, string Name, Guid OwnerId, string OwnerEmail, string Access, DateTime CreatedUtc, long FilesBytes);

public sealed record MemberView(Guid UserId, string Email, string Access);

public sealed record MemberRequest(string? Email, string? Access);

/// <summary>Cloud projects: ownership, membership and lifecycle. Each project owns an isolated folder and engine database.</summary>
public sealed class ProjectService(IDbContextFactory<ServerDb> factory, ProjectStore store, ProjectFiles files, AccessService access, AuditService audit, RealtimeHub hub, SyncSignals signals)
{
    public const string Owner = "owner";

    public async Task<List<ProjectView>> ListAsync(ClaimsPrincipal user)
    {
        access.Require(user, Permissions.CloudUse);
        var userId = user.RequireUserId();
        await using var db = await factory.CreateDbContextAsync();
        var memberOf = await db.ProjectMembers.AsNoTracking().Where(x => x.UserId == userId).ToDictionaryAsync(x => x.ProjectId, x => x.Access);
        var projects = await db.Projects.AsNoTracking().Where(x => x.OwnerId == userId || memberOf.Keys.Contains(x.Id)).OrderBy(x => x.Name).ToListAsync();
        var owners = await db.Users.AsNoTracking().Where(x => projects.Select(p => p.OwnerId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Email);
        return projects.Select(x => new ProjectView(x.Id, x.Name, x.OwnerId, owners.GetValueOrDefault(x.OwnerId, ""), x.OwnerId == userId ? Owner : memberOf[x.Id], x.CreatedUtc, files.Size(x.Id))).ToList();
    }

    public async Task<ProjectView> CreateAsync(ClaimsPrincipal user, string? name)
    {
        access.Require(user, Permissions.CloudUse);
        name = ValidateName(name);
        var project = new CloudProject { OwnerId = user.RequireUserId(), Name = name };
        await store.CreateAsync(project.Id, name);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Projects.Add(project);
            await db.SaveChangesAsync();
        }
        await audit.WriteAsync("project.created", name, project.Id.ToString());
        await signals.ProjectAsync(project.Id, "projects");
        return (await ListAsync(user)).First(x => x.Id == project.Id);
    }

    /// <summary>Resolves a project the user may reach. <paramref name="need"/> is "read", "run" or "owner".</summary>
    public async Task<(CloudProject Project, string Access)> RequireAsync(ClaimsPrincipal user, Guid projectId, string need)
    {
        access.Require(user, Permissions.CloudUse);
        return await RequireForAsync(user.RequireUserId(), projectId, need);
    }

    /// <summary>Same rule for a user id (scheduler, workers): feature and permission checks are the caller's job.</summary>
    public async Task<(CloudProject Project, string Access)> RequireForAsync(Guid userId, Guid projectId, string need)
    {
        await using var db = await factory.CreateDbContextAsync();
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == projectId);
        // A project the user cannot see does not reveal its existence.
        if (project == null) throw ApiException.NotFound("Project not found.");
        var level = project.OwnerId == userId ? Owner : await db.ProjectMembers.AsNoTracking().Where(x => x.ProjectId == projectId && x.UserId == userId).Select(x => x.Access).SingleOrDefaultAsync();
        if (level == null) throw ApiException.NotFound("Project not found.");
        var allowed = need switch
        {
            ProjectAccess.Read => true,
            ProjectAccess.Run => level is Owner or ProjectAccess.Run,
            _ => level == Owner
        };
        return allowed ? (project, level) : throw ApiException.Forbidden("project_access_denied", "You do not have this right on the project.");
    }

    public async Task RenameAsync(ClaimsPrincipal user, Guid projectId, string? name)
    {
        var (project, _) = await RequireAsync(user, projectId, Owner);
        name = ValidateName(name);
        await using (var db = await factory.CreateDbContextAsync())
            await db.Projects.Where(x => x.Id == projectId).ExecuteUpdateAsync(set => set.SetProperty(x => x.Name, name));
        await store.RenameAsync(projectId, name);
        await audit.WriteAsync("project.renamed", project.Name, name);
        await signals.ProjectAsync(projectId, "projects");
    }

    public async Task DeleteAsync(ClaimsPrincipal user, Guid projectId)
    {
        var (project, _) = await RequireAsync(user, projectId, Owner);
        List<Guid> audience;
        await using (var db = await factory.CreateDbContextAsync())
        {
            audience = await db.ProjectMembers.AsNoTracking().Where(x => x.ProjectId == projectId).Select(x => x.UserId).ToListAsync();
            audience.Add(project.OwnerId);
            var busy = await db.Runs.AnyAsync(x => x.ProjectId == projectId && (x.State == RunStates.Queued || x.State == RunStates.Running || x.State == RunStates.AwaitingApproval));
            if (busy) throw ApiException.Conflict("project_busy", "Cancel the project's queued or running requests before deleting it.");
            db.Projects.Remove(await db.Projects.SingleAsync(x => x.Id == projectId));
            await db.SaveChangesAsync();
        }
        store.Destroy(projectId);
        await audit.WriteAsync("project.deleted", project.Name, projectId.ToString());
        signals.ToUsers(audience, "projects", projectId);
    }

    public async Task<List<MemberView>> MembersAsync(ClaimsPrincipal user, Guid projectId)
    {
        await RequireAsync(user, projectId, ProjectAccess.Read);
        await using var db = await factory.CreateDbContextAsync();
        return await (from m in db.ProjectMembers.AsNoTracking() join u in db.Users.AsNoTracking() on m.UserId equals u.Id where m.ProjectId == projectId orderby u.Email
                      select new MemberView(u.Id, u.Email, m.Access)).ToListAsync();
    }

    public async Task SetMembersAsync(ClaimsPrincipal user, Guid projectId, IEnumerable<MemberRequest> requested)
    {
        var (project, _) = await RequireAsync(user, projectId, Owner);
        await using var db = await factory.CreateDbContextAsync();
        var desired = new Dictionary<Guid, string>();
        foreach (var item in requested)
        {
            var email = AccountService.NormalizeEmail(item.Email);
            var target = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email && x.IsActive) ?? throw ApiException.BadRequest("unknown_user", "No active account uses " + email);
            if (target.Id == project.OwnerId) continue;
            desired[target.Id] = item.Access is ProjectAccess.Run ? ProjectAccess.Run : ProjectAccess.Read;
        }
        var existing = await db.ProjectMembers.Where(x => x.ProjectId == projectId).ToListAsync();
        db.ProjectMembers.RemoveRange(existing.Where(x => !desired.ContainsKey(x.UserId)));
        foreach (var (id, level) in desired)
            if (existing.FirstOrDefault(x => x.UserId == id) is { } row) row.Access = level;
            else db.ProjectMembers.Add(new ProjectMember { ProjectId = projectId, UserId = id, Access = level });
        await db.SaveChangesAsync();
        // Members who lost access stop receiving the project's live events.
        hub.Unsubscribe(RealtimeHub.ProjectTopic(projectId), c => c.UserId is { } u && u != project.OwnerId && !desired.ContainsKey(u));
        await signals.ProjectAsync(projectId, "projects", also: existing.Select(x => x.UserId));
        await audit.WriteAsync("project.members_changed", project.Name, $"{desired.Count} member(s)");
    }

    static string ValidateName(string? name)
    {
        name = (name ?? "").Trim();
        return name.Length is 0 or > 100 ? throw ApiException.BadRequest("invalid_name", "A project name has 1 to 100 characters.") : name;
    }
}
