using System.Security.Claims;
using MonolithHarness.Server.Services;

namespace MonolithHarness.Server.Security;

/// <summary>
/// One decision point for "may this user do this?": the feature must be enabled, the role must grant the permission,
/// and resource-level rights (ownership, membership, shares) are checked by the caller on top of this.
/// </summary>
public sealed class AccessService(SettingsService settings)
{
    public void RequireFeature(string feature)
    {
        if (!settings.FeatureEnabled(feature))
            throw new ApiException(403, "feature_disabled", $"The '{feature}' feature is disabled on this server.");
    }

    public void Require(ClaimsPrincipal? user, string permission)
    {
        if (!user.IsAuthenticated()) throw ApiException.Unauthorized();
        if (user.SessionKind() == Data.SessionKinds.Run) throw ApiException.Forbidden();
        if (Permissions.FeatureOf(permission) is { } feature) RequireFeature(feature);
        if (!user.HasPermission(permission)) throw ApiException.Forbidden("missing_permission", $"The permission '{permission}' is required.");
    }

    public bool Can(ClaimsPrincipal? user, string permission) =>
        user.IsAuthenticated() && user.SessionKind() != Data.SessionKinds.Run
        && (Permissions.FeatureOf(permission) is not { } feature || settings.FeatureEnabled(feature))
        && user.HasPermission(permission);
}
