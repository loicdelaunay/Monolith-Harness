using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MonolithHarness.Server.Services;

namespace MonolithHarness.Server.Api;

public static class UsageApi
{
    public static void MapUsageApi(this IEndpointRouteBuilder app)
    {
        var usage = app.MapGroup("/api/usage").WithTags("Usage statistics").RequireAuthorization();
        usage.MapPost("", async (UsageUpload upload, ClaimsPrincipal user, UsageService service) => await service.AcceptAsync(user, upload))
            .WithSummary("Shares measurements (tokens, duration, speed) of model calls. Idempotent: the same measurement id is counted once.");
        usage.MapGet("/status", async (ClaimsPrincipal user, UsageService service) => await service.StatusAsync(user))
            .WithSummary("How many measurements the account has shared.");
        usage.MapGet("/summary", async ([FromQuery] string? scope, [FromQuery] string? period, [FromQuery] Guid? userId, ClaimsPrincipal user, UsageService service) =>
            await service.SummaryAsync(user, scope, period, userId))
            .WithSummary("Totals, timeline, per-model and (common view) per-user figures. scope: me, all or user; period: 24h, 7d, 30d or 1y. The common and per-user views need the usage.viewAll permission.");
        usage.MapDelete("", async ([FromQuery] Guid? userId, ClaimsPrincipal user, UsageService service) => new { deleted = await service.DeleteAsync(user, userId) })
            .WithSummary("Erases the measurements you shared (an administrator can erase those of another account).");
    }
}
