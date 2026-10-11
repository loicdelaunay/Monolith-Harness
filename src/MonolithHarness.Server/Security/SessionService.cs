using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Services;

namespace MonolithHarness.Server.Security;

public sealed record SessionIdentity(Guid SessionId, string Kind, Guid? RunId, User User, IReadOnlySet<string> Permissions, IReadOnlyList<string> Roles)
{
    public ClaimsPrincipal ToPrincipal(string scheme)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, User.Id.ToString()),
            new(ClaimTypes.Email, User.Email),
            new(ClaimTypes.Name, string.IsNullOrWhiteSpace(User.DisplayName) ? User.Email : User.DisplayName),
            new(ClaimNames.Session, SessionId.ToString()),
            new(ClaimNames.Kind, Kind),
        };
        if (RunId != null) claims.Add(new(ClaimNames.Run, RunId.Value.ToString()));
        if (User.PasswordChangePending) claims.Add(new(ClaimNames.PasswordPending, "1"));
        claims.AddRange(Roles.Select(x => new Claim(ClaimTypes.Role, x)));
        // A run token only reaches the provider relay: it carries no user permission at all.
        if (Kind != SessionKinds.Run) claims.AddRange(Permissions.Select(x => new Claim(ClaimNames.Permission, x)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    }
}

/// <summary>Opaque, revocable bearer sessions. Only a SHA-256 hash of each token is stored.</summary>
public sealed class SessionService(IDbContextFactory<ServerDb> factory, SettingsService settings)
{
    public const string Prefix = "mhs_";

    /// <summary>Raised after revocation so open WebSocket connections of those sessions are closed at once.</summary>
    public event Action<Guid>? SessionRevoked;
    public event Action<Guid, Guid?>? UserSessionsRevoked;

    public async Task<(string Token, UserSession Session)> CreateAsync(Guid userId, string kind, string label, string address, string userAgent, TimeSpan? lifetime = null, Guid? runId = null)
    {
        var token = PasswordHasher.NewToken(Prefix);
        var session = new UserSession
        {
            UserId = userId,
            TokenHash = PasswordHasher.HashToken(token),
            Kind = kind,
            Label = Clip(label, 100),
            Address = Clip(address, 64),
            UserAgent = Clip(userAgent, 200),
            RunId = runId,
            ExpiresUtc = DateTime.UtcNow + (lifetime ?? (kind == SessionKinds.Web
                ? TimeSpan.FromHours(settings.Int(SettingKeys.SessionHoursWeb))
                : TimeSpan.FromDays(settings.Int(SettingKeys.SessionDaysClient))))
        };
        await using var db = await factory.CreateDbContextAsync();
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return (token, session);
    }

    public async Task<SessionIdentity?> ValidateAsync(string token)
    {
        if (!token.StartsWith(Prefix, StringComparison.Ordinal) || token.Length > 100) return null;
        var hash = PasswordHasher.HashToken(token);
        await using var db = await factory.CreateDbContextAsync();
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash);
        if (session == null || session.RevokedUtc != null || session.ExpiresUtc <= DateTime.UtcNow) return null;
        var identity = await LoadAsync(db, session);
        if (identity == null) return null;
        if (DateTime.UtcNow - session.LastSeenUtc > TimeSpan.FromMinutes(1))
            await db.Sessions.Where(x => x.Id == session.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.LastSeenUtc, DateTime.UtcNow));
        return identity;
    }

    /// <summary>Used by interactive circuits to notice a revoked session without a new HTTP request.</summary>
    public async Task<bool> IsActiveAsync(Guid sessionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId);
        if (session == null || session.RevokedUtc != null || session.ExpiresUtc <= DateTime.UtcNow) return false;
        return await db.Users.AnyAsync(x => x.Id == session.UserId && x.IsActive);
    }

    async Task<SessionIdentity?> LoadAsync(ServerDb db, UserSession session)
    {
        var user = await db.Users.AsNoTracking().Include(x => x.Roles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Id == session.UserId);
        if (user == null || !user.IsActive) return null;
        var roles = user.Roles.Where(x => x.Role != null).Select(x => x.Role!).ToList();
        return new SessionIdentity(session.Id, session.Kind, session.RunId, user, EffectivePermissions(roles), roles.Select(x => x.Name).ToList());
    }

    public static IReadOnlySet<string> EffectivePermissions(IEnumerable<Role> roles)
    {
        var granted = new HashSet<string>();
        foreach (var role in roles)
        {
            if (role.IsSystem && role.Name == Roles.Administrator) return Permissions.All.Select(x => x.Key).ToHashSet();
            foreach (var permission in role.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Permissions.IsKnown(permission)) granted.Add(permission);
        }
        return granted;
    }

    public async Task<bool> RevokeAsync(Guid sessionId, Guid? ownerId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.Sessions.Where(x => x.Id == sessionId && x.RevokedUtc == null);
        if (ownerId != null) query = query.Where(x => x.UserId == ownerId);
        var revoked = await query.ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedUtc, DateTime.UtcNow)) > 0;
        if (revoked) SessionRevoked?.Invoke(sessionId);
        return revoked;
    }

    public async Task<int> RevokeAllAsync(Guid userId, Guid? except = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var count = await db.Sessions.Where(x => x.UserId == userId && x.RevokedUtc == null && x.Id != except)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedUtc, DateTime.UtcNow));
        UserSessionsRevoked?.Invoke(userId, except);
        return count;
    }

    /// <summary>Reloads roles and permissions of a live session (long-lived WebSocket connections).</summary>
    public async Task<SessionIdentity?> RefreshAsync(Guid sessionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId);
        if (session == null || session.RevokedUtc != null || session.ExpiresUtc <= DateTime.UtcNow) return null;
        return await LoadAsync(db, session);
    }

    public async Task RevokeRunAsync(Guid runId)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Sessions.Where(x => x.RunId == runId && x.RevokedUtc == null).ExecuteUpdateAsync(set => set.SetProperty(x => x.RevokedUtc, DateTime.UtcNow));
    }

    public async Task<List<UserSession>> ListAsync(Guid userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Sessions.AsNoTracking().Where(x => x.UserId == userId && x.RevokedUtc == null && x.ExpiresUtc > DateTime.UtcNow && x.Kind != SessionKinds.Run)
            .OrderByDescending(x => x.LastSeenUtc).ToListAsync();
    }

    /// <summary>Housekeeping: expired and long-revoked sessions carry no value.</summary>
    public async Task PurgeAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var cutoff = DateTime.UtcNow.AddDays(-7);
        await db.Sessions.Where(x => x.ExpiresUtc < cutoff || x.RevokedUtc < cutoff).ExecuteDeleteAsync();
        await db.RecoveryTokens.Where(x => x.ExpiresUtc < cutoff).ExecuteDeleteAsync();
    }

    static string Clip(string value, int length) => value.Length <= length ? value : value[..length];
}
