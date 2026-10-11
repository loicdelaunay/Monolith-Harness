using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

public sealed record UserSummary(Guid Id, string Email, string DisplayName, bool PasswordChangePending, bool PasswordChangeRequired, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public sealed record LoginResult(string Token, DateTime ExpiresUtc, Guid SessionId, UserSummary User, bool PasswordChangeSuggested, bool PasswordChangeRequired);

/// <summary>Sign-in, password change, account recovery and first-start initialization.</summary>
public sealed partial class AccountService(IDbContextFactory<ServerDb> factory, SettingsService settings, SessionService sessions, LoginThrottle throttle, AuditService audit)
{
    /// <summary>Credentials created once, at the very first initialization (see the server specification).</summary>
    public const string InitialAdminEmail = "admin@admin.com";
    public const string InitialAdminPassword = "admin1234";
    const string InitializedFlag = "server.initialized";

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    public static string NormalizeEmail(string? email)
    {
        var value = (email ?? "").Trim().ToLowerInvariant();
        if (value.Length is 0 or > 254 || !EmailPattern().IsMatch(value)) throw ApiException.BadRequest("invalid_email", "A valid e-mail address is required.");
        return value;
    }

    public void ValidatePassword(string? password, string? email = null)
    {
        var minimum = settings.Int(SettingKeys.MinPasswordLength);
        if (password == null || password.Length < minimum) throw ApiException.BadRequest("weak_password", $"The password must contain at least {minimum} characters.");
        if (password.Length > 128) throw ApiException.BadRequest("weak_password", "The password is limited to 128 characters.");
        if (email != null && password.Equals(email, StringComparison.OrdinalIgnoreCase)) throw ApiException.BadRequest("weak_password", "The password cannot be the e-mail address.");
    }

    /// <summary>
    /// Creates the system roles and the administrator account only the first time. Later starts never touch existing credentials,
    /// even if the account was renamed or deleted.
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        if (await db.Settings.AnyAsync(x => x.Key == InitializedFlag)) return false;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var admin = new Role { Name = Roles.Administrator, Description = "Accès complet au serveur", IsSystem = true, Permissions = string.Join(',', Permissions.All.Select(x => x.Key)) };
        var user = new Role { Name = Roles.User, Description = "Utilisation courante des fonctionnalités activées", Permissions = string.Join(',', Roles.UserPermissions) };
        var guest = new Role { Name = Roles.Guest, Description = "Suivre et commenter les conversations partagées", Permissions = string.Join(',', Roles.GuestPermissions) };
        db.Roles.AddRange(admin, user, guest);
        var account = new User
        {
            Email = InitialAdminEmail,
            DisplayName = "Administrateur",
            PasswordHash = PasswordHasher.Hash(InitialAdminPassword),
            PasswordChangePending = true,
            Roles = [new UserRole { Role = admin }]
        };
        db.Users.Add(account);
        db.Settings.Add(new SettingEntry { Key = InitializedFlag, Value = DateTime.UtcNow.ToString("O") });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        await audit.WriteAsync("server.initialized", InitialAdminEmail, "Initial administrator account created.", email: "system", address: "local");
        return true;
    }

    public async Task<LoginResult> LoginAsync(string? emailInput, string? password, string kind, string label, string address, string userAgent)
    {
        var email = (emailInput ?? "").Trim().ToLowerInvariant();
        if (email.Length == 0 || string.IsNullOrEmpty(password)) throw ApiException.BadRequest("credentials_required", "E-mail and password are required.");
        if (kind is not (SessionKinds.Web or SessionKinds.Client)) kind = SessionKinds.Client;
        var wait = throttle.RetryAfterSeconds(email, address);
        if (wait > 0)
        {
            await audit.WriteAsync("auth.login_blocked", email, $"Throttled for {wait}s.", false, email: email, address: address);
            throw ApiException.Throttled(wait);
        }
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.Include(x => x.Roles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Email == email);
        var valid = PasswordHasher.Verify(password, user?.PasswordHash, out var rehash);
        if (user == null || !valid || !user.IsActive)
        {
            throttle.Failure(email, address);
            await audit.WriteAsync("auth.login_failed", email, user is { IsActive: false } ? "Account disabled." : "", false, email: email, address: address);
            throw ApiException.Unauthorized("invalid_credentials", "Invalid e-mail or password.");
        }
        throttle.Success(email);
        if (rehash) user.PasswordHash = PasswordHasher.Hash(password);
        user.LastLoginUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var (token, session) = await sessions.CreateAsync(user.Id, kind, label, address, userAgent);
        await audit.WriteAsync("auth.login", email, $"{kind} session {session.Id}", email: email, address: address);
        var enforced = user.PasswordChangePending && settings.Bool(SettingKeys.EnforcePasswordChange);
        return new LoginResult(token, session.ExpiresUtc, session.Id, Summarize(user), user.PasswordChangePending, enforced);
    }

    public UserSummary Summarize(User user)
    {
        var roles = user.Roles.Where(x => x.Role != null).Select(x => x.Role!).ToList();
        return new UserSummary(user.Id, user.Email, user.DisplayName, user.PasswordChangePending,
            user.PasswordChangePending && settings.Bool(SettingKeys.EnforcePasswordChange),
            roles.Select(x => x.Name).ToList(), SessionService.EffectivePermissions(roles).Order().ToList());
    }

    public async Task<UserSummary> MeAsync(Guid userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.AsNoTracking().Include(x => x.Roles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Id == userId)
            ?? throw ApiException.Unauthorized();
        return Summarize(user);
    }

    /// <summary>Changing the password signs the account out everywhere else.</summary>
    public async Task ChangePasswordAsync(Guid userId, Guid currentSession, string? current, string? next, string address)
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId) ?? throw ApiException.Unauthorized();
        var wait = throttle.RetryAfterSeconds(user.Email, address);
        if (wait > 0) throw ApiException.Throttled(wait);
        if (!PasswordHasher.Verify(current ?? "", user.PasswordHash, out _))
        {
            throttle.Failure(user.Email, address);
            await audit.WriteAsync("auth.password_change_failed", user.Email, "Wrong current password.", false, email: user.Email, address: address);
            throw ApiException.Unauthorized("invalid_credentials", "The current password is incorrect.");
        }
        ValidatePassword(next, user.Email);
        if (current == next) throw ApiException.BadRequest("same_password", "The new password must differ from the current one.");
        user.PasswordHash = PasswordHasher.Hash(next!);
        user.PasswordChangePending = false;
        user.PasswordChangedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        throttle.Success(user.Email);
        var revoked = await sessions.RevokeAllAsync(userId, currentSession);
        await audit.WriteAsync("auth.password_changed", user.Email, $"{revoked} other session(s) revoked.", email: user.Email, address: address);
    }

    /// <summary>Returns a one-time token valid one hour. Delivery is the caller's job: this server has no mail transport.</summary>
    public async Task<(string Token, DateTime ExpiresUtc)> IssueRecoveryAsync(Guid userId, string issuedBy)
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId) ?? throw ApiException.NotFound("Account not found.");
        var token = PasswordHasher.NewToken("mhr_");
        var expires = DateTime.UtcNow.AddHours(1);
        // One live token per account.
        await db.RecoveryTokens.Where(x => x.UserId == userId && x.UsedUtc == null).ExecuteDeleteAsync();
        db.RecoveryTokens.Add(new RecoveryToken { UserId = userId, TokenHash = PasswordHasher.HashToken(token), ExpiresUtc = expires, IssuedBy = issuedBy });
        await db.SaveChangesAsync();
        await audit.WriteAsync("auth.recovery_issued", user.Email, "Valid one hour.", email: issuedBy);
        return (token, expires);
    }

    public async Task RecoverAsync(string? emailInput, string? token, string? newPassword, string address)
    {
        var email = (emailInput ?? "").Trim().ToLowerInvariant();
        var key = "recover:" + email;
        var wait = throttle.RetryAfterSeconds(key, address);
        if (wait > 0) throw ApiException.Throttled(wait);
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email);
        var hash = PasswordHasher.HashToken(token ?? "");
        var recovery = user == null ? null : await db.RecoveryTokens.SingleOrDefaultAsync(x => x.UserId == user.Id && x.TokenHash == hash);
        if (user == null || recovery == null || recovery.UsedUtc != null || recovery.ExpiresUtc <= DateTime.UtcNow || !user.IsActive)
        {
            throttle.Failure(key, address);
            await audit.WriteAsync("auth.recovery_failed", email, "", false, email: email, address: address);
            throw ApiException.BadRequest("invalid_recovery", "The recovery code is invalid or has expired.");
        }
        ValidatePassword(newPassword, user.Email);
        user.PasswordHash = PasswordHasher.Hash(newPassword!);
        user.PasswordChangePending = false;
        user.PasswordChangedUtc = DateTime.UtcNow;
        recovery.UsedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        throttle.Success(key);
        throttle.Success(email);
        await sessions.RevokeAllAsync(user.Id);
        await audit.WriteAsync("auth.recovered", user.Email, "All sessions revoked.", email: user.Email, address: address);
    }

    /// <summary>Effective permissions of an account, or null if it does not exist or is disabled (scheduler, workers).</summary>
    public async Task<IReadOnlySet<string>?> PermissionsOfAsync(Guid userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.AsNoTracking().Include(x => x.Roles).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Id == userId);
        return user is not { IsActive: true } ? null : SessionService.EffectivePermissions(user.Roles.Where(x => x.Role != null).Select(x => x.Role!));
    }

    public void Unlock(string email) => throttle.Unlock(email);
}
