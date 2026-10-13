using System.Security.Claims;
using MonolithHarness.Server.Services;

namespace MonolithHarness.Server.Api;

public static class MaintenanceApi
{
    public static void MapMaintenanceApi(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Maintenance").RequireAuthorization();
        admin.MapGet("/update", async (ClaimsPrincipal user, MaintenanceService service) => await service.UpdateInfoAsync(user))
            .WithSummary("Version, update mode and the newer release found by the last check, if any.");
        admin.MapPost("/update/check", async (ClaimsPrincipal user, MaintenanceService service) => await service.CheckAsync(user))
            .WithSummary("Looks for a newer release now.");
        admin.MapPost("/update/apply", async (ClaimsPrincipal user, MaintenanceService service) => Results.Accepted("/api/admin/update", await service.ApplyUpdateAsync(user)))
            .WithSummary("Downloads, verifies and installs the release found by the last check, then restarts the server. Running requests are interrupted.");
        admin.MapGet("/service", async (ClaimsPrincipal user, MaintenanceService service) => await service.ServiceStateAsync(user))
            .WithSummary("Whether the server is installed as a service (starts with the machine, restarts after a failure).");
        admin.MapPost("/service/install", async (ClaimsPrincipal user, MaintenanceService service) => Results.Accepted("/api/admin/service", await service.InstallServiceAsync(user)))
            .WithSummary("Installs the server as a service and switches to it: this process stops and the service takes over. Asks for administrator rights on this machine.");
        admin.MapDelete("/service", async (ClaimsPrincipal user, MaintenanceService service) => await service.UninstallServiceAsync(user))
            .WithSummary("Removes the service (the data is kept). If the server runs as that service it stops.");
    }
}
