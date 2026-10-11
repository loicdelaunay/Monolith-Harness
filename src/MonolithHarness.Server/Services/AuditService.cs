using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

/// <summary>Append-only record of security-relevant operations. Secrets never belong in <c>detail</c>.</summary>
public sealed class AuditService(IDbContextFactory<ServerDb> factory, IHttpContextAccessor http, ILogger<AuditService> log)
{
    public async Task WriteAsync(string action, string target = "", string detail = "", bool success = true, ClaimsPrincipal? actor = null, string? email = null, string? address = null)
    {
        try
        {
            var context = http.HttpContext;
            actor ??= context?.User;
            await using var db = await factory.CreateDbContextAsync();
            db.Audit.Add(new AuditEntry
            {
                ActorId = actor?.UserId(),
                ActorEmail = email ?? actor?.Email() ?? "",
                Action = action,
                Target = Trim(target, 200),
                Detail = Trim(detail, 1000),
                Address = address ?? context?.Connection.RemoteIpAddress?.ToString() ?? "",
                Success = success
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { log.LogError(ex, "Audit write failed for {Action}", action); }
    }

    static string Trim(string value, int length) => value.Length <= length ? value : value[..length];
}
