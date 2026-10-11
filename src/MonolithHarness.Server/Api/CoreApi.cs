using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;
using MonolithHarness.Server.Services;

namespace MonolithHarness.Server.Api;

public sealed record LoginRequest(string? Email, string? Password, string? Label);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record RecoverRequest(string? Email, string? Token, string? NewPassword);
public sealed record CreateUserRequest(string? Email, string? Password, string? DisplayName, List<Guid>? RoleIds);
public sealed record UpdateUserRequest(string? DisplayName, bool? IsActive, List<Guid>? RoleIds);
public sealed record PasswordRequest(string? Password);
public sealed record RoleRequest(string? Name, string? Description, List<string>? Permissions);
public sealed record FeatureRequest(bool Enabled);
public sealed record SettingRequest(string? Value);

public static class CoreApi
{
    public static void MapCoreApi(this IEndpointRouteBuilder app)
    {
        MapServer(app);
        MapAuth(app.MapGroup("/api/auth").WithTags("Authentication"));
        MapAdmin(app.MapGroup("/api/admin").WithTags("Administration").RequireAuthorization());
    }

    static void MapServer(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/server", (SettingsService settings) => new
        {
            name = ApiSupport.Name,
            version = ApiSupport.Version,
            apiVersion = ApiSupport.ApiVersion,
            features = settings.FeatureStates(),
            webSocket = "/ws",
            openApi = "/openapi/v1.json"
        }).WithTags("Server").WithSummary("Server identity and enabled features (anonymous: used by clients to test a connection).");
    }

    static void MapAuth(RouteGroupBuilder auth)
    {
        auth.MapPost("/login", async (LoginRequest request, HttpContext http, AccountService accounts) =>
            await accounts.LoginAsync(request.Email, request.Password, SessionKinds.Client, request.Label ?? "", http.Address(), http.Agent()))
            .WithSummary("Signs in and returns a bearer token (use it as Authorization: Bearer ...).");

        auth.MapPost("/recover", async (RecoverRequest request, HttpContext http, AccountService accounts) =>
        {
            await accounts.RecoverAsync(request.Email, request.Token, request.NewPassword, http.Address());
            return Results.NoContent();
        }).WithSummary("Sets a new password with a one-time recovery code issued by an administrator.");

        auth.MapPost("/logout", async (ClaimsPrincipal user, SessionService sessions, AuditService audit) =>
        {
            if (user.SessionId() is { } id) await sessions.RevokeAsync(id);
            await audit.WriteAsync("auth.logout", user.Email());
            return Results.NoContent();
        }).RequireAuthorization().WithSummary("Revokes the current session.");

        auth.MapGet("/me", async (ClaimsPrincipal user, AccountService accounts) => await accounts.MeAsync(user.RequireUserId()))
            .RequireAuthorization().WithSummary("The signed-in account, its roles and effective permissions.");

        auth.MapPost("/password", async (ChangePasswordRequest request, ClaimsPrincipal user, HttpContext http, AccountService accounts) =>
        {
            await accounts.ChangePasswordAsync(user.RequireUserId(), user.SessionId() ?? Guid.Empty, request.CurrentPassword, request.NewPassword, http.Address());
            return Results.NoContent();
        }).RequireAuthorization().WithSummary("Changes the password and revokes every other session.");

        auth.MapGet("/sessions", async (ClaimsPrincipal user, SessionService sessions) =>
            (await sessions.ListAsync(user.RequireUserId())).Select(x => new
            {
                x.Id, x.Kind, x.Label, x.Address, x.CreatedUtc, x.LastSeenUtc, x.ExpiresUtc, current = x.Id == user.SessionId()
            })).RequireAuthorization().WithSummary("Active sessions of the signed-in account.");

        auth.MapDelete("/sessions/{id:guid}", async (Guid id, ClaimsPrincipal user, SessionService sessions, AuditService audit) =>
        {
            if (!await sessions.RevokeAsync(id, user.RequireUserId())) throw ApiException.NotFound("Session not found.");
            await audit.WriteAsync("auth.session_revoked", id.ToString());
            return Results.NoContent();
        }).RequireAuthorization().WithSummary("Revokes one of your sessions.");

        auth.MapPost("/sessions/revoke-others", async (ClaimsPrincipal user, SessionService sessions, AuditService audit) =>
        {
            var count = await sessions.RevokeAllAsync(user.RequireUserId(), user.SessionId());
            await audit.WriteAsync("auth.sessions_revoked", user.Email(), $"{count} session(s)");
            return new { revoked = count };
        }).RequireAuthorization().WithSummary("Revokes every session except the current one.");
    }

    static void MapAdmin(RouteGroupBuilder admin)
    {
        // Users
        admin.MapGet("/users", async (ClaimsPrincipal user, AccessService access, DirectoryService directory) =>
        {
            access.Require(user, Permissions.UsersManage);
            return await directory.ListUsersAsync();
        });
        admin.MapPost("/users", async (CreateUserRequest request, ClaimsPrincipal user, AccessService access, DirectoryService directory) =>
        {
            access.Require(user, Permissions.UsersManage);
            return Results.Created("/api/admin/users", await directory.CreateUserAsync(request.Email, request.Password, request.DisplayName, request.RoleIds));
        }).WithSummary("Creates an account from an e-mail address and an initial password.");
        admin.MapGet("/users/{id:guid}", async (Guid id, ClaimsPrincipal user, AccessService access, DirectoryService directory) =>
        {
            access.Require(user, Permissions.UsersManage);
            return await directory.GetUserAsync(id);
        });
        admin.MapPatch("/users/{id:guid}", async (Guid id, UpdateUserRequest request, ClaimsPrincipal user, AccessService access, DirectoryService directory) =>
        {
            access.Require(user, Permissions.UsersManage);
            return await directory.UpdateUserAsync(id, request.DisplayName, request.IsActive, request.RoleIds);
        });
        admin.MapDelete("/users/{id:guid}", async (Guid id, ClaimsPrincipal user, AccessService access, DirectoryService directory) =>
        {
            access.Require(user, Permissions.UsersManage);
            await directory.DeleteUserAsync(id, user.RequireUserId());
            return Results.NoContent();
        });
        admin.MapPost("/users/{id:guid}/password", async (Guid id, PasswordRequest request, ClaimsPrincipal user, AccessService access, DirectoryService directory) =>
        {
            access.Require(user, Permissions.UsersManage);
            await directory.ResetPasswordAsync(id, request.Password);
            return Results.NoContent();
        }).WithSummary("Issues a temporary password; the user is offered to change it at the next sign-in.");
        admin.MapPost("/users/{id:guid}/recovery", async (Guid id, ClaimsPrincipal user, AccessService access, AccountService accounts) =>
        {
            access.Require(user, Permissions.UsersManage);
            var (token, expires) = await accounts.IssueRecoveryAsync(id, user.Email());
            return new { token, expiresUtc = expires };
        }).WithSummary("Issues a one-time recovery code (valid one hour) to give to the account owner.");
        admin.MapPost("/users/{id:guid}/revoke-sessions", async (Guid id, ClaimsPrincipal user, AccessService access, DirectoryService directory) =>
        {
            access.Require(user, Permissions.UsersManage);
            return new { revoked = await directory.RevokeSessionsAsync(id) };
        });
        admin.MapPost("/users/{id:guid}/unlock", async (Guid id, ClaimsPrincipal user, AccessService access, DirectoryService directory, AccountService accounts) =>
        {
            access.Require(user, Permissions.UsersManage);
            accounts.Unlock((await directory.GetUserAsync(id)).Email);
            return Results.NoContent();
        }).WithSummary("Clears the login throttle of an account.");

        // Roles
        admin.MapGet("/permissions", (ClaimsPrincipal user, AccessService access) =>
        {
            access.Require(user, Permissions.RolesManage);
            return Permissions.All;
        }).WithSummary("Catalog of assignable permissions.");
        admin.MapGet("/roles", async (ClaimsPrincipal user, AccessService access, DirectoryService directory) =>
        {
            access.Require(user, Permissions.RolesManage);
            return await directory.ListRolesAsync();
        });
        admin.MapPost("/roles", async (RoleRequest request, ClaimsPrincipal user, AccessService access, DirectoryService directory) =>
        {
            access.Require(user, Permissions.RolesManage);
            return Results.Created("/api/admin/roles", await directory.CreateRoleAsync(request.Name, request.Description, request.Permissions));
        });
        admin.MapPut("/roles/{id:guid}", async (Guid id, RoleRequest request, ClaimsPrincipal user, AccessService access, DirectoryService directory) =>
        {
            access.Require(user, Permissions.RolesManage);
            return await directory.UpdateRoleAsync(id, request.Name, request.Description, request.Permissions);
        });
        admin.MapDelete("/roles/{id:guid}", async (Guid id, ClaimsPrincipal user, AccessService access, DirectoryService directory) =>
        {
            access.Require(user, Permissions.RolesManage);
            await directory.DeleteRoleAsync(id);
            return Results.NoContent();
        });

        // Features and settings
        admin.MapGet("/features", (ClaimsPrincipal user, AccessService access, SettingsService settings) =>
        {
            access.Require(user, Permissions.SettingsManage);
            return Features.All.Select(x => new { x.Key, x.Name, x.Description, enabled = settings.FeatureEnabled(x.Key) });
        });
        admin.MapPut("/features/{key}", async (string key, FeatureRequest request, ClaimsPrincipal user, AccessService access, SettingsService settings, AuditService audit) =>
        {
            access.Require(user, Permissions.SettingsManage);
            await settings.SetFeatureAsync(key, request.Enabled);
            await audit.WriteAsync(request.Enabled ? "feature.enabled" : "feature.disabled", key);
            return Results.NoContent();
        });
        admin.MapGet("/settings", (ClaimsPrincipal user, AccessService access, SettingsService settings) =>
        {
            access.Require(user, Permissions.SettingsManage);
            return SettingsService.Catalog.Select(x => new { x.Key, x.Group, x.Label, x.Type, x.Min, x.Max, x.Default, value = settings.Raw(x.Key) });
        });
        admin.MapPut("/settings/{key}", async (string key, SettingRequest request, ClaimsPrincipal user, AccessService access, SettingsService settings, AuditService audit) =>
        {
            access.Require(user, Permissions.SettingsManage);
            await settings.SetAsync(key, request.Value ?? "");
            await audit.WriteAsync("setting.changed", key, request.Value ?? "");
            return Results.NoContent();
        });

        // Audit
        admin.MapGet("/audit", async (ClaimsPrincipal user, AccessService access, IDbContextFactory<ServerDb> factory,
            [FromQuery] int? take, [FromQuery] int? skip, [FromQuery] string? action, [FromQuery] string? actor) =>
        {
            access.Require(user, Permissions.AuditRead);
            await using var db = await factory.CreateDbContextAsync();
            var query = db.Audit.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => x.Action.StartsWith(action));
            if (!string.IsNullOrWhiteSpace(actor)) query = query.Where(x => x.ActorEmail.Contains(actor));
            var page = Math.Clamp(take ?? 100, 1, 500);
            return await query.OrderByDescending(x => x.Id).Skip(Math.Max(skip ?? 0, 0)).Take(page).ToListAsync();
        }).WithSummary("Audit log, newest first.");
    }
}
