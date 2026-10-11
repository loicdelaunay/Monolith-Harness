namespace MonolithHarness.Server.Security;

public sealed record FeatureInfo(string Key, string Name, string Description, bool DefaultEnabled);

public sealed record PermissionInfo(string Key, string Group, string Name);

/// <summary>The five switchable feature sets of the server. Disabling one blocks it for everybody, whatever their role.</summary>
public static class Features
{
    public const string Cloud = "cloud";
    public const string Sharing = "sharing";
    public const string Schedules = "schedules";
    public const string Queue = "queue";
    public const string Providers = "providers";

    // Code-executing features start disabled: the administrator opts in once isolation is configured.
    public static readonly IReadOnlyList<FeatureInfo> All =
    [
        new(Cloud, "Exécution cloud", "Projets et requêtes exécutés sur le serveur, dans un workspace isolé par projet.", false),
        new(Sharing, "Partage en temps réel", "Suivi en direct d’une conversation locale par des personnes autorisées, via un bridge.", true),
        new(Schedules, "Tâches planifiées cloud", "Tâches exécutées par le serveur aux horaires définis, sans client connecté.", false),
        new(Queue, "File d’attente", "Limite le nombre d’utilisateurs et d’exécutions simultanés. Désactivée : aucune limite.", true),
        new(Providers, "Providers partagés", "Catalogue de providers dont les clés restent sur le serveur, qui relaie les appels.", true),
    ];

    public static bool IsKnown(string key) => All.Any(x => x.Key == key);
}

/// <summary>Permissions are granted by roles. An operation also requires its feature to be enabled and the right on the resource.</summary>
public static class Permissions
{
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string SettingsManage = "settings.manage";
    public const string AuditRead = "audit.read";
    public const string BackupManage = "backup.manage";
    public const string CloudUse = "cloud.use";
    public const string SharingHost = "sharing.host";
    public const string SharingView = "sharing.view";
    public const string SharingComment = "sharing.comment";
    public const string SchedulesManage = "schedules.manage";
    public const string QueueManage = "queue.manage";
    public const string ProvidersManage = "providers.manage";
    public const string ProvidersUse = "providers.use";

    public static readonly IReadOnlyList<PermissionInfo> All =
    [
        new(UsersManage, "admin", "Gérer les comptes et les connexions"),
        new(RolesManage, "admin", "Gérer les rôles et leurs permissions"),
        new(SettingsManage, "admin", "Activer les fonctionnalités et régler le serveur"),
        new(AuditRead, "admin", "Consulter le journal d’audit"),
        new(BackupManage, "admin", "Sauvegarder les données"),
        new(CloudUse, Features.Cloud, "Créer des projets cloud et lancer des requêtes"),
        new(SharingHost, Features.Sharing, "Partager une conversation locale"),
        new(SharingView, Features.Sharing, "Suivre une conversation partagée"),
        new(SharingComment, Features.Sharing, "Commenter une conversation partagée"),
        new(SchedulesManage, Features.Schedules, "Créer des tâches planifiées cloud"),
        new(QueueManage, Features.Queue, "Voir et annuler toutes les requêtes de la file"),
        new(ProvidersManage, Features.Providers, "Configurer le catalogue de providers"),
        new(ProvidersUse, Features.Providers, "Utiliser les providers partagés autorisés"),
    ];

    public static bool IsKnown(string key) => All.Any(x => x.Key == key);

    /// <summary>The feature that must be enabled for a permission to have any effect, or null for administration.</summary>
    public static string? FeatureOf(string permission) => All.FirstOrDefault(x => x.Key == permission) is { Group: not "admin" } info ? info.Group : null;
}

public static class Roles
{
    public const string Administrator = "Administrator";
    public const string User = "User";
    public const string Guest = "Guest";

    public static readonly string[] UserPermissions =
        [Permissions.CloudUse, Permissions.SharingHost, Permissions.SharingView, Permissions.SharingComment, Permissions.SchedulesManage, Permissions.ProvidersUse];

    public static readonly string[] GuestPermissions = [Permissions.SharingView, Permissions.SharingComment];
}

/// <summary>A failure with a stable code that every client (and the web UI) can translate.</summary>
public sealed class ApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    /// <summary>Set for throttled requests so that the sign-in page can say how long to wait.</summary>
    public int? RetryAfterSeconds { get; private init; }
    public static ApiException BadRequest(string code, string message) => new(400, code, message);
    public static ApiException Unauthorized(string code = "unauthorized", string message = "Authentication required.") => new(401, code, message);
    public static ApiException Forbidden(string code = "forbidden", string message = "You do not have the right to do this.") => new(403, code, message);
    public static ApiException NotFound(string message = "Not found.") => new(404, "not_found", message);
    public static ApiException Conflict(string code, string message) => new(409, code, message);
    public static ApiException Throttled(int seconds) => new(429, "too_many_attempts", $"Too many attempts. Try again in {seconds} seconds.") { RetryAfterSeconds = seconds };
}
