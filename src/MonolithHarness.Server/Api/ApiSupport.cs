using System.Reflection;
using System.Security.Claims;

namespace MonolithHarness.Server.Api;

public static class ApiSupport
{
    public const string Name = "Monolith Harness Server";
    public const int ApiVersion = 1;

    public static string Version { get; } = typeof(ApiSupport).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public static string Address(this HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "";

    public static string Agent(this HttpContext context) => context.Request.Headers.UserAgent.ToString();

    public static void SetSessionCookie(this HttpContext context, string token, DateTime expiresUtc) =>
        context.Response.Cookies.Append(Security.SessionAuth.Cookie, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Expires = expiresUtc,
            Path = "/",
            IsEssential = true
        });

    public static void ClearSessionCookie(this HttpContext context) =>
        context.Response.Cookies.Delete(Security.SessionAuth.Cookie, new CookieOptions { Path = "/", SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps });

    /// <summary>Only local paths are accepted as return targets, which prevents open redirects after sign-in.</summary>
    public static string LocalPath(string? target) =>
        !string.IsNullOrEmpty(target) && target[0] == '/' && !target.StartsWith("//", StringComparison.Ordinal) && !target.StartsWith("/\\", StringComparison.Ordinal) ? target : "/";

    public static bool InRole(this ClaimsPrincipal user, string role) => user.IsInRole(role);
}
