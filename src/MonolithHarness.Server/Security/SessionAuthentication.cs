using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MonolithHarness.Server.Security;

public static class SessionAuth
{
    public const string Scheme = "MonolithSession";
    public const string Cookie = "mh_session";
    public const string RelayPolicy = "Relay";
}

/// <summary>
/// Authenticates opaque session tokens from <c>Authorization: Bearer</c> (clients) or the HttpOnly cookie (web UI).
/// Permissions are read from the database on every request, so a role change or revocation applies immediately.
/// </summary>
public sealed class SessionAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, SessionService sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = null;
        var viaCookie = false;
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = header["Bearer ".Length..].Trim();
        else if (Request.Cookies.TryGetValue(SessionAuth.Cookie, out var cookie)) { token = cookie; viaCookie = true; }
        if (string.IsNullOrEmpty(token)) return AuthenticateResult.NoResult();

        // A cookie is sent by the browser on its own: reject cross-site writes (SameSite=Strict is the first line of defence).
        if (viaCookie && !HttpMethods.IsGet(Request.Method) && !HttpMethods.IsHead(Request.Method) && !HttpMethods.IsOptions(Request.Method) && !SameOrigin())
            return AuthenticateResult.Fail("Cross-site request refused.");

        var identity = await sessions.ValidateAsync(token);
        return identity == null
            ? AuthenticateResult.Fail("Invalid or expired session.")
            : AuthenticateResult.Success(new AuthenticationTicket(identity.ToPrincipal(Scheme.Name), Scheme.Name));
    }

    bool SameOrigin()
    {
        if (Request.Headers.TryGetValue("Sec-Fetch-Site", out var site) && site.ToString() is "cross-site" or "same-site") return false;
        if (!Request.Headers.TryGetValue("Origin", out var origin) || origin.Count == 0) return true;
        return Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var uri)
            && uri.Authority.Equals(Request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // Browser navigations to a protected page go to the sign-in form; API clients get a JSON 401.
        var path = Request.Path;
        if (HttpMethods.IsGet(Request.Method) && Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWithSegments("/api") && !path.StartsWithSegments("/openapi") && !path.StartsWithSegments("/ws"))
        {
            Response.Redirect("/login?returnUrl=" + Uri.EscapeDataString(Request.Path + Request.QueryString));
            return Task.CompletedTask;
        }
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(new { code = "unauthorized", message = "Authentication required." });
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Response.WriteAsJsonAsync(new { code = "forbidden", message = "You do not have the right to do this." });
    }
}
