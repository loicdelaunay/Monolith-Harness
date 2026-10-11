using Microsoft.AspNetCore.Mvc;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;
using MonolithHarness.Server.Services;

namespace MonolithHarness.Server.Api;

/// <summary>Form endpoints of the web interface: they set or clear the HttpOnly session cookie, then redirect.</summary>
public static class WebAuth
{
    public static void MapWebAuth(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", async ([FromForm] string? email, [FromForm] string? password, [FromForm] string? returnUrl, HttpContext http, AccountService accounts) =>
        {
            try
            {
                var result = await accounts.LoginAsync(email, password, SessionKinds.Web, "Navigateur", http.Address(), http.Agent());
                http.SetSessionCookie(result.Token, result.ExpiresUtc);
                return Results.Redirect(result.PasswordChangeRequired ? "/account" : ApiSupport.LocalPath(returnUrl));
            }
            catch (ApiException ex)
            {
                return Results.Redirect($"/login?error={ex.Code}&wait={ex.RetryAfterSeconds}&returnUrl={Uri.EscapeDataString(ApiSupport.LocalPath(returnUrl))}");
            }
        }).ExcludeFromDescription();

        app.MapPost("/auth/recover", async ([FromForm] string? email, [FromForm] string? code, [FromForm] string? password, HttpContext http, AccountService accounts) =>
        {
            try
            {
                await accounts.RecoverAsync(email, code, password, http.Address());
                return Results.Redirect("/login?recovered=1");
            }
            catch (ApiException ex) { return Results.Redirect($"/recover?error={ex.Code}&wait={ex.RetryAfterSeconds}"); }
        }).ExcludeFromDescription();

        app.MapGet("/auth/signout", async (HttpContext http, SessionService sessions) =>
        {
            if (http.Request.Cookies.TryGetValue(SessionAuth.Cookie, out var token) && await sessions.ValidateAsync(token) is { } identity)
                await sessions.RevokeAsync(identity.SessionId);
            http.ClearSessionCookie();
            http.Response.Headers.CacheControl = "no-store";
            return Results.Redirect("/login");
        }).ExcludeFromDescription();
    }
}
