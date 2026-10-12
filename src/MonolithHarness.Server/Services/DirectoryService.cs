using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

public sealed record RoleRef(Guid Id, string Name);

/// <summary>An active account as proposed by the member picker: enough to recognize it, nothing else.</summary>
public sealed record UserSuggestion(Guid Id, string Email, string DisplayName);

public sealed record UserView(Guid Id, string Email, string DisplayName, bool IsActive, bool PasswordChangePending, DateTime CreatedUtc, DateTime? LastLoginUtc, IReadOnlyList<RoleRef> Roles);

public sealed record RoleView(Guid Id, string Name, string Description, bool IsSystem, IReadOnlyList<string> Permissions, int Members);

/// <summary>Administrator operations on accounts and roles.</summary>
public sealed class DirectoryService(IDbContextFactory<ServerDb> factory, AccountService accounts, SessionService sessions, AuditService audit, SyncSignals signals)
{
    // ---- Users ----

    public async Task<List<UserView>> ListUsersAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var users = await db.Users.AsNoTracking().Include(x => x.Roles).ThenInclude(x => x.Role).OrderBy(x => x.Email).ToListAsync();
        return users.Select(ToView).ToList();
    }

    /// <summary>Active accounts (other than the requester) whose e-mail or name contains the text, for member and viewer pickers.</summary>
    public async Task<List<UserSuggestion>> SearchActiveAsync(Guid requester, string? query, int take)
    {
        var text = (query ?? "").Trim().ToLowerInvariant();
        if (text.Length > 100) text = text[..100];
        await using var db = await factory.CreateDbContextAsync();
        var users = db.Users.AsNoTracking().Where(x => x.IsActive && x.Id != requester);
        if (text.Length > 0) users = users.Where(x => x.Email.Contains(text) || x.DisplayName.ToLower().Contains(text));
        return await users.OrderBy(x => x.Email).Take(Math.Clamp(take, 1, 200)).Select(x => new UserSuggestion(x.Id, x.Email, x.DisplayName)).ToListAsync();
    }

    public async Task<UserView> GetUserAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return ToView(await db.Users.AsNoTracking().Include(x => x.Roles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Id == id)
            ?? throw ApiException.NotFound("Account not found."));
    }

    /// <summary>The administrator chooses the first password; the user is then offered to change it at first sign-in.</summary>
    public async Task<UserView> CreateUserAsync(string? email, string? password, string? displayName, IEnumerable<Guid>? roleIds)
    {
        var normalized = AccountService.NormalizeEmail(email);
        accounts.ValidatePassword(password, normalized);
        await using var db = await factory.CreateDbContextAsync();
        if (await db.Users.AnyAsync(x => x.Email == normalized)) throw ApiException.Conflict("email_taken", "An account already uses this e-mail address.");
        var user = new User
        {
            Email = normalized,
            DisplayName = (displayName ?? "").Trim(),
            PasswordHash = PasswordHasher.Hash(password!),
            PasswordChangePending = true,
            Roles = await RolesFor(db, roleIds)
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await audit.WriteAsync("user.created", normalized, "Roles: " + string.Join(", ", user.Roles.Select(x => x.Role!.Name)));
        return await GetUserAsync(user.Id);
    }

    public async Task<UserView> UpdateUserAsync(Guid id, string? displayName, bool? isActive, IEnumerable<Guid>? roleIds)
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.Include(x => x.Roles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Account not found.");
        var wasAdministrator = IsAdministrator(user);
        if (displayName != null) user.DisplayName = displayName.Trim();
        if (roleIds != null)
        {
            user.Roles.Clear();
            foreach (var role in await RolesFor(db, roleIds)) user.Roles.Add(role);
        }
        if (isActive != null) user.IsActive = isActive.Value;
        if (wasAdministrator && (!user.IsActive || !IsAdministrator(user, db)) && !await AnotherAdministratorExists(db, id))
            throw ApiException.Conflict("last_administrator", "At least one active administrator must remain.");
        await db.SaveChangesAsync();
        if (!user.IsActive) await sessions.RevokeAllAsync(id);
        await audit.WriteAsync("user.updated", user.Email, $"active={user.IsActive}; roles={string.Join(", ", user.Roles.Select(x => x.Role?.Name))}");
        // What an account may reach (providers, shares, projects) depends on its roles.
        signals.Providers(); signals.PublicShares();
        return await GetUserAsync(id);
    }

    public async Task DeleteUserAsync(Guid id, Guid actor)
    {
        if (id == actor) throw ApiException.Conflict("self_delete", "You cannot delete your own account.");
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.Include(x => x.Roles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Account not found.");
        if (IsAdministrator(user) && !await AnotherAdministratorExists(db, id)) throw ApiException.Conflict("last_administrator", "At least one active administrator must remain.");
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        await audit.WriteAsync("user.deleted", user.Email);
    }

    /// <summary>An administrator-issued password is temporary: the user is offered to replace it, and all sessions end.</summary>
    public async Task ResetPasswordAsync(Guid id, string? password)
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Account not found.");
        accounts.ValidatePassword(password, user.Email);
        user.PasswordHash = PasswordHasher.Hash(password!);
        user.PasswordChangePending = true;
        await db.SaveChangesAsync();
        await sessions.RevokeAllAsync(id);
        accounts.Unlock(user.Email);
        await audit.WriteAsync("user.password_reset", user.Email, "All sessions revoked.");
    }

    public async Task<int> RevokeSessionsAsync(Guid id)
    {
        var count = await sessions.RevokeAllAsync(id);
        await audit.WriteAsync("user.sessions_revoked", id.ToString(), $"{count} session(s)");
        return count;
    }

    // ---- Roles ----

    public async Task<List<RoleView>> ListRolesAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var roles = await db.Roles.AsNoTracking().Include(x => x.Users).OrderBy(x => x.Name).ToListAsync();
        return roles.Select(ToView).ToList();
    }

    public async Task<RoleView> CreateRoleAsync(string? name, string? description, IEnumerable<string>? permissions)
    {
        name = ValidateRoleName(name);
        await using var db = await factory.CreateDbContextAsync();
        if (await db.Roles.AnyAsync(x => x.Name == name)) throw ApiException.Conflict("role_exists", "A role with this name already exists.");
        var role = new Role { Name = name, Description = (description ?? "").Trim(), Permissions = Join(permissions) };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        await audit.WriteAsync("role.created", name, role.Permissions);
        return ToView(role);
    }

    public async Task<RoleView> UpdateRoleAsync(Guid id, string? name, string? description, IEnumerable<string>? permissions)
    {
        await using var db = await factory.CreateDbContextAsync();
        var role = await db.Roles.Include(x => x.Users).SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Role not found.");
        if (role.IsSystem && ((name != null && name.Trim() != role.Name) || permissions != null))
            throw ApiException.Conflict("system_role", "The Administrator role always holds every permission and keeps its name.");
        if (name != null && !role.IsSystem)
        {
            name = ValidateRoleName(name);
            if (await db.Roles.AnyAsync(x => x.Name == name && x.Id != id)) throw ApiException.Conflict("role_exists", "A role with this name already exists.");
            role.Name = name;
        }
        if (description != null) role.Description = description.Trim();
        if (permissions != null && !role.IsSystem) role.Permissions = Join(permissions);
        await db.SaveChangesAsync();
        await audit.WriteAsync("role.updated", role.Name, role.Permissions);
        signals.Providers(); signals.PublicShares();
        return ToView(role);
    }

    public async Task DeleteRoleAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var role = await db.Roles.SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Role not found.");
        if (role.IsSystem) throw ApiException.Conflict("system_role", "A system role cannot be deleted.");
        db.Roles.Remove(role);
        await db.SaveChangesAsync();
        await audit.WriteAsync("role.deleted", role.Name);
    }

    // ---- Helpers ----

    static async Task<List<UserRole>> RolesFor(ServerDb db, IEnumerable<Guid>? roleIds)
    {
        var ids = (roleIds ?? []).Distinct().ToList();
        var roles = await db.Roles.Where(x => ids.Contains(x.Id)).ToListAsync();
        if (roles.Count != ids.Count) throw ApiException.BadRequest("unknown_role", "One of the roles does not exist.");
        return roles.Select(x => new UserRole { Role = x, RoleId = x.Id }).ToList();
    }

    static bool IsAdministrator(User user, ServerDb? db = null) =>
        user.Roles.Any(x => (x.Role ?? db?.Roles.Find(x.RoleId)) is { IsSystem: true, Name: Roles.Administrator });

    static async Task<bool> AnotherAdministratorExists(ServerDb db, Guid excluding) =>
        await db.Users.AnyAsync(x => x.Id != excluding && x.IsActive && x.Roles.Any(r => r.Role!.IsSystem && r.Role.Name == Roles.Administrator));

    static string ValidateRoleName(string? name)
    {
        name = (name ?? "").Trim();
        if (name.Length is 0 or > 60) throw ApiException.BadRequest("invalid_role", "A role name has 1 to 60 characters.");
        return name;
    }

    static string Join(IEnumerable<string>? permissions)
    {
        var list = (permissions ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
        var unknown = list.FirstOrDefault(x => !Permissions.IsKnown(x));
        if (unknown != null) throw ApiException.BadRequest("unknown_permission", "Unknown permission: " + unknown);
        return string.Join(',', list);
    }

    static UserView ToView(User user) => new(user.Id, user.Email, user.DisplayName, user.IsActive, user.PasswordChangePending, user.CreatedUtc, user.LastLoginUtc,
        user.Roles.Where(x => x.Role != null).Select(x => new RoleRef(x.Role!.Id, x.Role.Name)).OrderBy(x => x.Name).ToList());

    static RoleView ToView(Role role) => new(role.Id, role.Name, role.Description, role.IsSystem,
        role.IsSystem && role.Name == Roles.Administrator ? Permissions.All.Select(x => x.Key).ToList()
            : role.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(), role.Users.Count);
}
