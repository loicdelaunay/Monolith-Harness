using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;
using MonolithHarness.Server.Services;

namespace MonolithHarness.Server.Api;

public sealed record ProviderRequest(string? Name, string? BaseUrl, string? Secret, List<ProviderModel>? Models, bool? IsEnabled, bool? AllowLocalUse);
public sealed record AccessRequest(List<AccessEntry>? Entries);
public sealed record ProjectRequest(string? Name);
public sealed record MembersRequest(List<MemberRequest>? Members);
public sealed record ChatRequest(string? Title);
public sealed record SendRequest(string? Text, Guid ProviderId, string? Model);
public sealed record DecisionRequest(string? Choice);
public sealed record AnswerRequest(bool Cancelled, List<List<string>>? Answers);
public sealed record ShareRequest(string? Title, string? ClientConversationId, bool? IsPublic);
public sealed record JoinRequest(string? Code);
public sealed record CommentRequest(string? Text);
public sealed record ShareSettingsRequest(bool? IsPublic);
public sealed record ProbeRequest(string? BaseUrl, string? Secret, Guid? ProviderId);
public sealed record InviteRequest(string? Email);

public static class FeatureApi
{
    public static void MapFeatureApi(this IEndpointRouteBuilder app)
    {
        MapProviders(app);
        MapDirectory(app);
        MapProjects(app.MapGroup("/api/projects").WithTags("Cloud projects").RequireAuthorization());
        MapRuns(app);
        MapSchedules(app.MapGroup("/api/schedules").WithTags("Scheduled tasks").RequireAuthorization());
        MapShares(app.MapGroup("/api/shares").WithTags("Shared conversations").RequireAuthorization());
        MapBackups(app.MapGroup("/api/admin/backups").WithTags("Administration").RequireAuthorization());
    }

    // ---- Providers ----

    static void MapProviders(IEndpointRouteBuilder app)
    {
        var providers = app.MapGroup("/api/providers").WithTags("Providers");
        providers.MapGet("", async (ClaimsPrincipal user, ProviderService service) => await service.CatalogAsync(user)).RequireAuthorization()
            .WithSummary("Providers and models available to you. Keys and upstream addresses are never returned.");
        providers.MapMethods("/{id:guid}/relay/{**path}", ["GET", "POST"], async (Guid id, string path, HttpContext http, ProviderRelay relay) => await relay.HandleAsync(id, path, http))
            .RequireAuthorization(SessionAuth.RelayPolicy)
            .WithSummary("OpenAI-compatible relay (chat/completions, embeddings, models). The server adds the provider key.");

        var admin = app.MapGroup("/api/admin/providers").WithTags("Administration").RequireAuthorization();
        admin.MapGet("", async (ClaimsPrincipal user, AccessService access, ProviderService service) =>
        {
            access.Require(user, Permissions.ProvidersManage);
            return await service.ListAsync();
        });
        admin.MapPost("", async (ProviderRequest request, ClaimsPrincipal user, AccessService access, ProviderService service) =>
        {
            access.Require(user, Permissions.ProvidersManage);
            return Results.Created("/api/admin/providers", await service.CreateAsync(request.Name, request.BaseUrl, request.Secret, request.Models, request.IsEnabled ?? true, request.AllowLocalUse ?? false));
        });
        admin.MapPut("/{id:guid}", async (Guid id, ProviderRequest request, ClaimsPrincipal user, AccessService access, ProviderService service) =>
        {
            access.Require(user, Permissions.ProvidersManage);
            return await service.UpdateAsync(id, request.Name, request.BaseUrl, request.Secret, request.Models, request.IsEnabled, request.AllowLocalUse);
        }).WithSummary("Omit 'secret' to keep the stored key; send an empty string to remove it.");
        admin.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, AccessService access, ProviderService service) =>
        {
            access.Require(user, Permissions.ProvidersManage);
            await service.DeleteAsync(id);
            return Results.NoContent();
        });
        admin.MapPost("/probe", async (ProbeRequest request, ClaimsPrincipal user, AccessService access, ProviderService service, HttpContext http) =>
        {
            access.Require(user, Permissions.ProvidersManage);
            return await service.ProbeAsync(request.BaseUrl, request.Secret, request.ProviderId, http.RequestAborted);
        }).WithSummary("Tests a provider address and key (GET /models) and returns the models it publishes. Omit 'secret' to use the stored key of 'providerId'.");
        admin.MapPut("/{id:guid}/access", async (Guid id, AccessRequest request, ClaimsPrincipal user, AccessService access, ProviderService service) =>
        {
            access.Require(user, Permissions.ProvidersManage);
            await service.SetAccessAsync(id, request.Entries ?? []);
            return Results.NoContent();
        }).WithSummary("Users and roles allowed to use the provider (kind 'user' or 'role').");
    }

    // ---- Directory ----

    /// <summary>Lets anybody who can invite (project owners, share hosts) look up active accounts by e-mail or name.</summary>
    static void MapDirectory(IEndpointRouteBuilder app) =>
        app.MapGet("/api/users/search", async ([FromQuery] string? query, [FromQuery] int? take, ClaimsPrincipal user, AccessService access, DirectoryService directory) =>
        {
            if (!access.Can(user, Permissions.CloudUse) && !access.Can(user, Permissions.SharingHost)) throw ApiException.Forbidden("missing_permission", "Searching accounts needs the cloud or sharing permission.");
            return await directory.SearchActiveAsync(user.RequireUserId(), query, take ?? 20);
        }).WithTags("Directory").RequireAuthorization().WithSummary("Active accounts matching a text (e-mail or display name), to pick members or viewers.");

    // ---- Cloud projects ----

    static void MapProjects(RouteGroupBuilder projects)
    {
        projects.MapGet("", async (ClaimsPrincipal user, ProjectService service) => await service.ListAsync(user));
        projects.MapPost("", async (ProjectRequest request, ClaimsPrincipal user, ProjectService service) => Results.Created("/api/projects", await service.CreateAsync(user, request.Name)));
        projects.MapPatch("/{id:guid}", async (Guid id, ProjectRequest request, ClaimsPrincipal user, ProjectService service) =>
        {
            await service.RenameAsync(user, id, request.Name);
            return Results.NoContent();
        });
        projects.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, ProjectService service) =>
        {
            await service.DeleteAsync(user, id);
            return Results.NoContent();
        });
        projects.MapGet("/{id:guid}/members", async (Guid id, ClaimsPrincipal user, ProjectService service) => await service.MembersAsync(user, id));
        projects.MapPut("/{id:guid}/members", async (Guid id, MembersRequest request, ClaimsPrincipal user, ProjectService service) =>
        {
            await service.SetMembersAsync(user, id, request.Members ?? []);
            return Results.NoContent();
        }).WithSummary("Replaces the member list. Access is 'read' or 'run'.");

        // Files
        projects.MapGet("/{id:guid}/files", async (Guid id, [FromQuery] string? path, ClaimsPrincipal user, ProjectService service, ProjectFiles files) =>
        {
            await service.RequireAsync(user, id, ProjectAccess.Read);
            return files.List(id, path);
        });
        projects.MapGet("/{id:guid}/files/content", async (Guid id, [FromQuery] string path, ClaimsPrincipal user, ProjectService service, ProjectFiles files) =>
        {
            await service.RequireAsync(user, id, ProjectAccess.Read);
            var stream = files.OpenRead(id, path);
            return Results.File(stream, "application/octet-stream", Path.GetFileName(path.TrimEnd('/', '\\')));
        }).WithSummary("Downloads a file (always as an attachment).");
        projects.MapPut("/{id:guid}/files/content", async (Guid id, [FromQuery] string path, HttpRequest request, ClaimsPrincipal user, ProjectService service, ProjectFiles files, AuditService audit) =>
        {
            await service.RequireAsync(user, id, ProjectAccess.Run);
            await files.WriteAsync(id, path, request.Body, request.HttpContext.RequestAborted);
            await audit.WriteAsync("project.file_written", id.ToString(), path);
            return Results.NoContent();
        }).WithSummary("Creates or replaces a file with the raw request body.");
        projects.MapPost("/{id:guid}/files/directory", async (Guid id, [FromQuery] string path, ClaimsPrincipal user, ProjectService service, ProjectFiles files) =>
        {
            await service.RequireAsync(user, id, ProjectAccess.Run);
            files.CreateDirectory(id, path);
            return Results.NoContent();
        });
        projects.MapDelete("/{id:guid}/files", async (Guid id, [FromQuery] string path, ClaimsPrincipal user, ProjectService service, ProjectFiles files, AuditService audit) =>
        {
            await service.RequireAsync(user, id, ProjectAccess.Run);
            files.Delete(id, path);
            await audit.WriteAsync("project.file_deleted", id.ToString(), path);
            return Results.NoContent();
        });

        // Conversations and requests
        projects.MapGet("/{id:guid}/chats", async (Guid id, ClaimsPrincipal user, ProjectService service, ProjectStore store) =>
        {
            await service.RequireAsync(user, id, ProjectAccess.Read);
            return await store.ChatsAsync(id);
        });
        projects.MapPost("/{id:guid}/chats", async (Guid id, ChatRequest request, ClaimsPrincipal user, ProjectService service, ProjectStore store) =>
        {
            await service.RequireAsync(user, id, ProjectAccess.Run);
            return Results.Created($"/api/projects/{id}/chats", await store.CreateChatAsync(id, request.Title));
        });
        projects.MapPatch("/{id:guid}/chats/{chatId:int}", async (Guid id, int chatId, ChatRequest request, ClaimsPrincipal user, ProjectService service, ProjectStore store) =>
        {
            await service.RequireAsync(user, id, ProjectAccess.Run);
            await store.RenameChatAsync(id, chatId, request.Title);
            return Results.NoContent();
        }).WithSummary("Renames a conversation: every member of the project sees the new title.");
        projects.MapDelete("/{id:guid}/chats/{chatId:int}", async (Guid id, int chatId, ClaimsPrincipal user, ProjectService service, ProjectStore store, RunService runs) =>
        {
            await service.RequireAsync(user, id, ProjectAccess.Run);
            if ((await runs.ListAsync(user, false)).Any(x => x.ProjectId == id && x.ChatId == chatId && !RunStates.IsFinal(x.State)))
                throw ApiException.Conflict("conversation_busy", "Cancel the request in progress first.");
            await store.DeleteChatAsync(id, chatId);
            return Results.NoContent();
        });
        projects.MapGet("/{id:guid}/chats/{chatId:int}/messages", async (Guid id, int chatId, [FromQuery] int? after, ClaimsPrincipal user, ProjectService service, ProjectStore store) =>
        {
            await service.RequireAsync(user, id, ProjectAccess.Read);
            if (!await store.ChatExistsAsync(id, chatId)) throw ApiException.NotFound("Conversation not found.");
            return await store.MessagesAsync(id, chatId, after ?? 0);
        });
        projects.MapPost("/{id:guid}/chats/{chatId:int}/requests", async (Guid id, int chatId, SendRequest request, ClaimsPrincipal user, RunService runs) =>
            Results.Accepted($"/api/runs", await runs.EnqueueAsync(user, id, chatId, request.Text, request.ProviderId, request.Model)))
            .WithSummary("Queues a request. Follow it with GET /api/runs/{id} or the WebSocket topic run:{id}.");
    }

    // ---- Runs, queue, approvals ----

    static void MapRuns(IEndpointRouteBuilder app)
    {
        var runs = app.MapGroup("/api/runs").WithTags("Requests and queue").RequireAuthorization();
        runs.MapGet("", async ([FromQuery] string? scope, ClaimsPrincipal user, RunService service) => await service.ListAsync(user, scope == "all"));
        runs.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, RunService service) => await service.GetAsync(user, id));
        runs.MapPost("/{id:guid}/cancel", async (Guid id, ClaimsPrincipal user, RunService service) => await service.CancelAsync(user, id));
        app.MapGet("/api/queue", async (ClaimsPrincipal user, RunService service) => await service.OverviewAsync(user)).RequireAuthorization().WithTags("Requests and queue")
            .WithSummary("Whole queue (requires the queue.manage permission).");

        var approvals = app.MapGroup("/api/approvals").WithTags("Requests and queue").RequireAuthorization();
        approvals.MapGet("", async ([FromQuery] bool? pending, ClaimsPrincipal user, ApprovalService service) => await service.ListAsync(user.RequireUserId(), pending ?? true))
            .WithSummary("Tool approvals and agent questions waiting for you.");
        approvals.MapPost("/{id:guid}/decision", async (Guid id, DecisionRequest request, ClaimsPrincipal user, ApprovalService service) =>
            await service.DecideAsync(user.RequireUserId(), id, request.Choice)).WithSummary("choice: allow, deny or always.");
        approvals.MapPost("/{id:guid}/answer", async (Guid id, AnswerRequest request, ClaimsPrincipal user, ApprovalService service) =>
            await service.AnswerAsync(user.RequireUserId(), id, request.Cancelled, request.Answers?.Select(x => (IReadOnlyList<string>)x).ToList()));
    }

    // ---- Scheduled tasks ----

    static void MapSchedules(RouteGroupBuilder schedules)
    {
        schedules.MapGet("", async (ClaimsPrincipal user, ScheduleService service) => await service.ListAsync(user));
        schedules.MapPost("", async (ScheduleRequest request, ClaimsPrincipal user, ScheduleService service) => Results.Created("/api/schedules", await service.CreateAsync(user, request)));
        schedules.MapPut("/{id:guid}", async (Guid id, ScheduleRequest request, ClaimsPrincipal user, ScheduleService service) => await service.UpdateAsync(user, id, request));
        schedules.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, ScheduleService service) =>
        {
            await service.DeleteAsync(user, id);
            return Results.NoContent();
        });
        schedules.MapPost("/{id:guid}/run", async (Guid id, ClaimsPrincipal user, ScheduleService service) => Results.Accepted("/api/runs", await service.RunNowAsync(user, id)));
        schedules.MapGet("/time-zones", () => TimeZoneInfo.GetSystemTimeZones().Select(x => new { id = x.Id, name = x.DisplayName }));
    }

    // ---- Shared conversations ----

    static void MapShares(RouteGroupBuilder shares)
    {
        shares.MapGet("", async (ClaimsPrincipal user, ShareService service) => await service.ListAsync(user));
        shares.MapPost("", async (ShareRequest request, ClaimsPrincipal user, ShareService service) => Results.Created("/api/shares", await service.CreateAsync(user, request.Title, request.ClientConversationId, request.IsPublic ?? false)))
            .WithSummary("Declares a local conversation as shared. The device then attaches its bridge over the WebSocket.");
        shares.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, ShareService service) => await service.GetAsync(user, id));
        shares.MapGet("/public", async (ClaimsPrincipal user, ShareService service) => await service.ListPublicAsync(user))
            .WithSummary("Shared conversations their host made public.");
        shares.MapPost("/join", async (JoinRequest request, ClaimsPrincipal user, ShareService service) => await service.JoinAsync(user, request.Code))
            .WithSummary("Follows a shared conversation from its sharing code (attempts are limited).");
        shares.MapPatch("/{id:guid}", async (Guid id, ShareSettingsRequest request, ClaimsPrincipal user, ShareService service) =>
            request.IsPublic is { } isPublic ? await service.SetPublicAsync(user, id, isPublic) : await service.GetAsync(user, id))
            .WithSummary("Host only: makes the conversation public or private.");
        shares.MapPost("/{id:guid}/comments", async (Guid id, CommentRequest request, ClaimsPrincipal user, ShareService service) =>
        {
            await service.CommentAsync(user, id, request.Text);
            return Results.NoContent();
        }).WithSummary("Adds a comment to a followed conversation (shown to everybody and sent to the host device).");
        shares.MapPost("/{id:guid}/code", async (Guid id, ClaimsPrincipal user, ShareService service) => await service.RegenerateCodeAsync(user, id))
            .WithSummary("Host only: generates a new sharing code; the previous one stops working.");
        shares.MapDelete("/{id:guid}/code", async (Guid id, ClaimsPrincipal user, ShareService service) => await service.DisableCodeAsync(user, id))
            .WithSummary("Host only: turns the sharing code off.");
        shares.MapPost("/{id:guid}/participants", async (Guid id, InviteRequest request, ClaimsPrincipal user, ShareService service) => await service.InviteAsync(user, id, request.Email));
        shares.MapDelete("/{id:guid}/participants/{userId:guid}", async (Guid id, Guid userId, ClaimsPrincipal user, ShareService service) =>
        {
            await service.RemoveParticipantAsync(user, id, userId);
            return Results.NoContent();
        });
        shares.MapPost("/{id:guid}/leave", async (Guid id, ClaimsPrincipal user, ShareService service) =>
        {
            await service.LeaveAsync(user, id);
            return Results.NoContent();
        });
        shares.MapPost("/{id:guid}/revoke", async (Guid id, ClaimsPrincipal user, ShareService service) =>
        {
            await service.RevokeAsync(user, id);
            return Results.NoContent();
        });
    }

    // ---- Backups ----

    static void MapBackups(RouteGroupBuilder backups)
    {
        backups.MapGet("", (ClaimsPrincipal user, AccessService access, BackupService service) =>
        {
            access.Require(user, Permissions.BackupManage);
            return service.List();
        });
        backups.MapPost("", async (ClaimsPrincipal user, AccessService access, BackupService service) =>
        {
            access.Require(user, Permissions.BackupManage);
            return Results.Created("/api/admin/backups", await service.CreateAsync());
        }).WithSummary("Creates a consistent snapshot of the server database. Restore it offline with the 'restore' command.");
        backups.MapGet("/{name}", (string name, ClaimsPrincipal user, AccessService access, BackupService service) =>
        {
            access.Require(user, Permissions.BackupManage);
            return Results.File(new FileStream(service.Resolve(name), FileMode.Open, FileAccess.Read, FileShare.Read), "application/octet-stream", name);
        });
        backups.MapDelete("/{name}", async (string name, ClaimsPrincipal user, AccessService access, BackupService service) =>
        {
            access.Require(user, Permissions.BackupManage);
            await service.DeleteAsync(name);
            return Results.NoContent();
        });
    }
}
