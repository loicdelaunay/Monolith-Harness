using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using MonolithHarness.Server.Api;
using MonolithHarness.Server.Components;
using MudBlazor.Services;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Realtime;
using MonolithHarness.Server.Security;
using MonolithHarness.Server.Services;
using MonolithHarness.Server.Workers;

namespace MonolithHarness.Server;

/// <summary>Composition root: builds the web host (REST, WebSocket, Blazor UI) and prepares the database.</summary>
public static class ServerApp
{
    public static async Task<WebApplication> BuildAsync(ServerPaths paths, ServerOptions options, Action<WebApplicationBuilder>? configure = null)
    {
        paths.Ensure();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = ContentRoot(),
            ApplicationName = typeof(ServerApp).Assembly.GetName().Name,
            // Production unless asked otherwise: the portable executable is a deployment, not a development run.
            EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Production
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
        // A service has no console to read: its log goes to workspace/logs.
        if (ServerRuntime.IsService) builder.Logging.AddProvider(new FileLoggerProvider(paths.Logs));
        builder.WebHost.UseStaticWebAssets();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = 64L * 1024 * 1024;
            kestrel.Listen(options.Address, options.Port);
            if (options.HttpsPort is { } https)
                kestrel.Listen(options.Address, https, listen => listen.UseHttps(X509CertificateLoader.LoadPkcs12FromFile(options.CertificatePath!, options.CertificatePassword)));
        });

        var services = builder.Services;
        services.AddSingleton(paths);
        services.AddSingleton(options);
        services.AddHttpContextAccessor();
        services.AddPooledDbContextFactory<ServerDb>(o => o.UseSqlite($"Data Source={paths.Database}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));
        var protection = services.AddDataProtection().SetApplicationName("MonolithHarness.Server").PersistKeysToFileSystem(new DirectoryInfo(paths.Keys));
        // Machine scope: the same keys are readable by the console and by the service, which runs under another account.
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi(protectToLocalMachine: true);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(paths.Keys, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        services.AddSingleton<SettingsService>();
        services.AddSingleton<AnnouncementService>();
        services.AddSingleton<UsageService>();
        services.AddSingleton<ServerLifecycle>();
        services.AddSingleton<MaintenanceService>();
        services.TryAddSingleton(_ => new UpdateService(paths, options));
        services.AddHostedService<UpdateChecker>();
        if (OperatingSystem.IsWindows() && ServerRuntime.IsService) services.AddWindowsService(service => service.ServiceName = ServiceInstaller.WindowsName);
        services.AddSingleton<AuditService>();
        services.AddSingleton<LoginThrottle>();
        services.AddSingleton<SessionService>();
        services.AddSingleton<AccessService>();
        services.AddSingleton<AccountService>();
        services.AddSingleton<DirectoryService>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<SyncSignals>();
        services.AddSingleton<ProviderService>();
        services.AddSingleton<ProviderRelay>();
        services.AddHttpClient(ProviderRelay.ClientName).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(20)
        }).ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<ProjectStore>();
        services.AddSingleton<ProjectFiles>();
        services.AddSingleton<ProjectService>();
        services.AddSingleton<ApprovalService>();
        services.AddSingleton<RunService>();
        services.AddSingleton<ScheduleService>();
        services.AddSingleton<ShareService>();
        services.AddSingleton<RealtimeHub>();
        services.AddSingleton<RealtimeGateway>();
        services.AddSingleton<ServerEndpoints>();
        services.AddSingleton<ContainerProbe>();
        services.AddSingleton<WorkerLauncher>();
        services.AddSingleton<IRunExecutor, CloudRunExecutor>();
        services.AddHostedService<RunDispatcher>();
        services.AddHostedService<ScheduleRunner>();

        services.AddAuthentication(SessionAuth.Scheme).AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuth.Scheme, null);
        services.AddAuthorization(authorization =>
        {
            // Run tokens only open the provider relay; every other endpoint needs a real user session.
            authorization.DefaultPolicy = new AuthorizationPolicyBuilder(SessionAuth.Scheme).RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.SessionKind() != SessionKinds.Run).Build();
            authorization.AddPolicy(SessionAuth.RelayPolicy, policy => policy.AddAuthenticationSchemes(SessionAuth.Scheme).RequireAuthenticatedUser());
        });
        services.AddRazorComponents().AddInteractiveServerComponents();
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, SessionStateProvider>();
        services.AddScoped<UiContext>();
        services.AddMudServices(options =>
        {
            options.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomRight;
            options.SnackbarConfiguration.VisibleStateDuration = 4000;
        });
        services.AddOpenApi("v1", o => o.AddDocumentTransformer<OpenApiSetup>());
        services.AddEndpointsApiExplorer();
        configure?.Invoke(builder);

        var app = builder.Build();
        await PrepareAsync(app);
        Configure(app, options);
        return app;
    }

    /// <summary>
    /// A single-file executable unpacks itself into a temporary folder, but the web assets (MudBlazor, Blazor, wwwroot) are published
    /// beside the executable: use that folder when it carries them, the build output otherwise.
    /// </summary>
    static string ContentRoot()
    {
        var process = Environment.ProcessPath;
        if (process != null && !Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(process) is { } folder && Directory.Exists(Path.Combine(folder, "wwwroot"))) return folder;
        return AppContext.BaseDirectory;
    }

    static async Task PrepareAsync(WebApplication app)
    {
        var factory = app.Services.GetRequiredService<IDbContextFactory<ServerDb>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
            // WAL lets the UI read while workers and the scheduler write.
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }
        await app.Services.GetRequiredService<SettingsService>().LoadAsync();
        if (await app.Services.GetRequiredService<AccountService>().InitializeAsync())
            app.Logger.LogWarning("First start: administrator account {Email} created with its initial password. Change it after signing in.", AccountService.InitialAdminEmail);
        await app.Services.GetRequiredService<SessionService>().PurgeAsync();
        await app.Services.GetRequiredService<RunService>().InitializeAsync();
        WireEvents(app.Services);
        app.Lifetime.ApplicationStarted.Register(() => app.Services.GetRequiredService<ServerEndpoints>().Capture(app));
        app.Lifetime.ApplicationStopping.Register(() => app.Services.GetRequiredService<RealtimeHub>().CloseAllAsync().GetAwaiter().GetResult());
    }

    /// <summary>Cross-cutting reactions: revoked sessions drop their sockets, disabled features close what depended on them.</summary>
    static void WireEvents(IServiceProvider services)
    {
        var hub = services.GetRequiredService<RealtimeHub>();
        var sessions = services.GetRequiredService<SessionService>();
        sessions.SessionRevoked += hub.CloseSession;
        sessions.UserSessionsRevoked += hub.CloseUser;
        var settings = services.GetRequiredService<SettingsService>();
        var shares = services.GetRequiredService<ShareService>();
        var runs = services.GetRequiredService<RunService>();
        settings.FeatureChanged += (feature, enabled) =>
        {
            hub.Publish(RealtimeHub.ServerTopic, "feature.changed", new { feature, enabled });
            if (enabled) return;
            if (feature == Features.Sharing) shares.CloseAll("feature_disabled");
            if (feature == Features.Cloud) _ = Task.Run(() => runs.CancelQueuedAsync("Cloud execution was disabled by an administrator."));
        };
    }

    static void Configure(WebApplication app, ServerOptions options)
    {
        if (options.BehindProxy)
        {
            var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost };
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
            app.UseForwardedHeaders(forwarded);
        }
        app.Use(ErrorHandling);
        if (options.RedirectToHttps) app.UseHttpsRedirection();
        if (options.HttpsPort != null) app.UseHsts();
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "same-origin";
            if (context.Request.Path.StartsWithSegments("/api")) headers.CacheControl = "no-store";
            await next();
        });
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
        app.UseAuthentication();
        app.Use(PasswordChangeGate);
        app.UseAuthorization();
        app.UseAntiforgery();
        // Published folders carry a static-assets manifest; plain builds and the in-process tests fall back to the web root.
        var manifest = Path.Combine(app.Environment.ContentRootPath, app.Environment.ApplicationName + ".staticwebassets.endpoints.json");
        if (File.Exists(manifest)) app.MapStaticAssets(manifest);
        else app.UseStaticFiles();
        app.MapOpenApi();
        app.UseSwaggerUI(ui =>
        {
            ui.DocumentTitle = ApiSupport.Name + " API";
            ui.SwaggerEndpoint("/openapi/v1.json", ApiSupport.Name + " API v1");
            ui.RoutePrefix = "swagger";
        });
        app.MapCoreApi();
        app.MapFeatureApi();
        app.MapUsageApi();
        app.MapMaintenanceApi();
        app.MapRealtime();
        app.MapWebAuth();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
    }

    static async Task ErrorHandling(HttpContext context, RequestDelegate next)
    {
        try { await next(context); }
        catch (ApiException ex) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = ex.Status;
            if (ex.Status == StatusCodes.Status429TooManyRequests) context.Response.Headers.RetryAfter = "30";
            await context.Response.WriteAsJsonAsync(new { code = ex.Code, message = ex.Message });
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !context.Response.HasStarted && context.Request.Path.StartsWithSegments("/api"))
        {
            context.RequestServices.GetRequiredService<ILogger<ServerOptions>>().LogError(ex, "Unhandled error on {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { code = "server_error", message = "Unexpected server error." });
        }
    }

    /// <summary>When the policy imposes a password change, only the authentication endpoints stay reachable until it is done.</summary>
    static async Task PasswordChangeGate(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path;
        if (context.User.PasswordChangePending() && path.StartsWithSegments("/api") && !path.StartsWithSegments("/api/auth") && !path.StartsWithSegments("/api/server")
            && context.RequestServices.GetRequiredService<SettingsService>().Bool(SettingKeys.EnforcePasswordChange))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { code = "password_change_required", message = "Change your password before continuing." });
            return;
        }
        await next(context);
    }
}
