using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using MonolithHarness.Server.Realtime;
using MonolithHarness.Server.Security;
using MonolithHarness.Server.Services;
using MudBlazor;

namespace MonolithHarness.Server.Components;

/// <summary>Keeps the circuit's user in step with the database: revoked sessions sign out, role changes apply within seconds.</summary>
public sealed class SessionStateProvider(ILoggerFactory loggers, SessionService sessions) : RevalidatingServerAuthenticationStateProvider(loggers)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken cancellationToken)
    {
        if (state.User.SessionId() is not { } id) return false;
        var identity = await sessions.RefreshAsync(id);
        if (identity == null) return false;
        SetAuthenticationState(Task.FromResult(new AuthenticationState(identity.ToPrincipal(SessionAuth.Scheme))));
        return true;
    }
}

/// <summary>Per-circuit helper for pages: a fresh principal for every action and uniform error reporting.</summary>
public sealed class UiContext(AuthenticationStateProvider auth, SessionService sessions, ISnackbar snackbar, NavigationManager navigation, AccessService access,
    SettingsService settings, RealtimeHub hub, ILogger<UiContext> log)
{
    public RealtimeHub Hub => hub;
    public SettingsService Settings => settings;

    /// <summary>The signed-in user, reloaded from the database. Signs out when the session is gone.</summary>
    public async Task<ClaimsPrincipal> UserAsync()
    {
        var state = await auth.GetAuthenticationStateAsync();
        if (state.User.SessionId() is { } id && await sessions.RefreshAsync(id) is { } identity) return identity.ToPrincipal(SessionAuth.Scheme);
        navigation.NavigateTo("/login", forceLoad: true);
        throw ApiException.Unauthorized();
    }

    public bool Can(ClaimsPrincipal? user, string permission) => access.Can(user, permission);

    public bool Enabled(string feature) => settings.FeatureEnabled(feature);

    /// <summary>Runs an action; business errors become a message, anything else is logged and reported generically.</summary>
    public async Task<bool> TryAsync(Func<ClaimsPrincipal, Task> action, string? success = null)
    {
        try
        {
            await action(await UserAsync());
            if (success != null) snackbar.Add(success, Severity.Success);
            return true;
        }
        catch (ApiException ex) when (ex.Status != 401) { snackbar.Add(Explain(ex), Severity.Warning); return false; }
        catch (ApiException) { return false; }
        catch (Exception ex)
        {
            log.LogError(ex, "UI action failed");
            snackbar.Add("Une erreur inattendue s’est produite.", Severity.Error);
            return false;
        }
    }

    /// <summary>Like <see cref="TryAsync(Func{ClaimsPrincipal, Task}, string?)"/> but returns the value (default after an error).</summary>
    public async Task<T?> Try<T>(Func<ClaimsPrincipal, Task<T>> action, string? success = null)
    {
        T? result = default;
        await TryAsync(async user => result = await action(user), success);
        return result;
    }

    public void Info(string message) => snackbar.Add(message, Severity.Info);

    /// <summary>User-facing text for the stable error codes (the REST API keeps the English message).</summary>
    public static string Explain(ApiException ex) => ex.Code switch
    {
        "invalid_credentials" => "Adresse e-mail ou mot de passe incorrect.",
        "too_many_attempts" => $"Trop de tentatives. Réessayez dans {ex.RetryAfterSeconds ?? 30} s.",
        "weak_password" => "Mot de passe trop faible: respectez la longueur minimale (8 caractères ou plus).",
        "email_taken" => "Un compte utilise déjà cette adresse e-mail.",
        "invalid_email" => "Adresse e-mail invalide.",
        "last_administrator" => "Au moins un administrateur actif doit rester.",
        "feature_disabled" => "Cette fonctionnalité est désactivée sur le serveur.",
        "missing_permission" or "forbidden" => "Vous n’avez pas ce droit.",
        "project_busy" => "Annulez d’abord les requêtes du projet en cours.",
        "conversation_busy" => "Cette conversation a déjà une requête en cours.",
        "queue_full" => "La file d’attente est pleine. Réessayez plus tard.",
        "too_many_queued" => "Vous avez trop de requêtes en attente.",
        "quota_exceeded" => "Le quota disque du projet est atteint.",
        "provider_not_allowed" => "Ce provider n’est pas disponible pour vous.",
        "invalid_recovery" => "Le code de récupération est invalide ou a expiré.",
        _ => ex.Message
    };
}
