using System.Security.Claims;

namespace MonolithHarness.Server.Security;

public static class ClaimNames
{
    public const string Session = "mh:session";
    public const string Kind = "mh:kind";
    public const string Permission = "mh:permission";
    public const string PasswordPending = "mh:pwdpending";
    public const string Run = "mh:run";
}

public static class PrincipalExtensions
{
    public static bool IsAuthenticated(this ClaimsPrincipal? user) => user?.Identity?.IsAuthenticated == true;

    public static Guid? UserId(this ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static Guid RequireUserId(this ClaimsPrincipal? user) => user.UserId() ?? throw ApiException.Unauthorized();

    public static string Email(this ClaimsPrincipal? user) => user?.FindFirstValue(ClaimTypes.Email) ?? "";

    public static Guid? SessionId(this ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirstValue(ClaimNames.Session), out var id) ? id : null;

    public static string SessionKind(this ClaimsPrincipal? user) => user?.FindFirstValue(ClaimNames.Kind) ?? "";

    public static bool HasPermission(this ClaimsPrincipal? user, string permission) =>
        user?.HasClaim(ClaimNames.Permission, permission) == true;

    public static bool PasswordChangePending(this ClaimsPrincipal? user) => user?.HasClaim(ClaimNames.PasswordPending, "1") == true;
}
