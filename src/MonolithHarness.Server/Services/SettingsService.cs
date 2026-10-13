using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

public sealed record SettingInfo(string Key, string Group, string Label, string Type, string Default, int Min = 0, int Max = int.MaxValue);

public static class SettingKeys
{
    public const string MaxConcurrentUsers = "queue.maxConcurrentUsers";
    public const string MaxConcurrentRuns = "queue.maxConcurrentRuns";
    public const string MaxRunsPerUser = "queue.maxRunsPerUser";
    public const string MaxRunsPerProvider = "queue.maxRunsPerProvider";
    public const string MaxQueuedPerUser = "queue.maxQueuedPerUser";
    public const string MaxQueueLength = "queue.maxQueueLength";
    public const string RelayWaitSeconds = "queue.relayWaitSeconds";
    public const string TimeoutSeconds = "execution.timeoutSeconds";
    public const string MemoryMb = "execution.memoryMb";
    public const string CpuSeconds = "execution.cpuSeconds";
    public const string ProjectDiskMb = "execution.projectDiskMb";
    public const string AllowNetworkTools = "execution.allowNetworkTools";
    public const string RequireContainer = "execution.requireContainerSandbox";
    public const string AllowElevatedWorkers = "execution.allowElevatedWorkers";
    public const string ApprovalTimeoutMinutes = "execution.approvalTimeoutMinutes";
    public const string AutoApplySandbox = "execution.autoApplySandbox";
    public const string EnforcePasswordChange = "security.enforcePasswordChange";
    public const string MaxFailedLogins = "security.maxFailedLogins";
    public const string SessionHoursWeb = "security.sessionHoursWeb";
    public const string SessionDaysClient = "security.sessionDaysClient";
    public const string MinPasswordLength = "security.minPasswordLength";
    public const string MaxViewers = "shares.maxViewers";
    public const string BufferEvents = "shares.bufferEvents";
    public const string MaxSchedulesPerUser = "schedules.maxPerUser";
    public const string MinScheduleMinutes = "schedules.minIntervalMinutes";
    public const string RelayMaxRequestMb = "providers.relayMaxRequestMb";
    public const string UsageRetentionDays = "usage.retentionDays";
}

/// <summary>Feature switches and tunable limits. Values live in SQLite and are cached because they are read on every request.</summary>
public sealed class SettingsService(IDbContextFactory<ServerDb> factory)
{
    public static readonly IReadOnlyList<SettingInfo> Catalog =
    [
        new(SettingKeys.MaxConcurrentUsers, "Files d’attente", "Utilisateurs dont les requêtes s’exécutent en même temps", "int", "2", 1, 1000),
        new(SettingKeys.MaxConcurrentRuns, "Files d’attente", "Exécutions simultanées sur le serveur (capacité)", "int", "4", 1, 1000),
        new(SettingKeys.MaxRunsPerUser, "Files d’attente", "Exécutions simultanées par utilisateur", "int", "1", 1, 100),
        new(SettingKeys.MaxRunsPerProvider, "Files d’attente", "Exécutions simultanées par provider (0 = illimité)", "int", "0", 0, 1000),
        new(SettingKeys.MaxQueuedPerUser, "Files d’attente", "Requêtes en attente par utilisateur", "int", "10", 1, 10000),
        new(SettingKeys.MaxQueueLength, "Files d’attente", "Longueur maximale de la file", "int", "100", 1, 100000),
        new(SettingKeys.RelayWaitSeconds, "Files d’attente", "Attente maximale d’un appel d’une conversation locale (relais), en secondes", "int", "300", 5, 3600),
        new(SettingKeys.TimeoutSeconds, "Isolation et limites", "Durée maximale d’une exécution (secondes)", "int", "1800", 30, 86400),
        new(SettingKeys.MemoryMb, "Isolation et limites", "Mémoire maximale d’un worker (Mo)", "int", "2048", 128, 262144),
        new(SettingKeys.CpuSeconds, "Isolation et limites", "Temps CPU maximal d’un worker (secondes)", "int", "1800", 10, 86400),
        new(SettingKeys.ProjectDiskMb, "Isolation et limites", "Espace disque maximal d’un projet (Mo)", "int", "2048", 16, 1048576),
        new(SettingKeys.AllowNetworkTools, "Isolation et limites", "Autoriser les outils d’accès réseau (web) dans les exécutions", "bool", "false"),
        new(SettingKeys.RequireContainer, "Isolation et limites", "Exiger l’isolation par conteneur pour les outils qui exécutent du code", "bool", "true"),
        new(SettingKeys.AllowElevatedWorkers, "Isolation et limites", "Autoriser les workers si le serveur tourne avec des privilèges administrateur", "bool", "false"),
        new(SettingKeys.ApprovalTimeoutMinutes, "Isolation et limites", "Délai avant expiration d’une demande d’approbation (minutes)", "int", "1440", 1, 43200),
        new(SettingKeys.AutoApplySandbox, "Isolation et limites", "Appliquer au projet les changements validés de la sandbox à la fin d’une exécution", "bool", "true"),
        new(SettingKeys.EnforcePasswordChange, "Sécurité", "Imposer le changement du mot de passe initial (sinon seulement proposé)", "bool", "false"),
        new(SettingKeys.MaxFailedLogins, "Sécurité", "Échecs de connexion avant blocage temporaire", "int", "5", 3, 100),
        new(SettingKeys.SessionHoursWeb, "Sécurité", "Durée d’une session web (heures)", "int", "12", 1, 720),
        new(SettingKeys.SessionDaysClient, "Sécurité", "Durée d’une connexion d’application cliente (jours)", "int", "30", 1, 365),
        new(SettingKeys.MinPasswordLength, "Sécurité", "Longueur minimale des mots de passe", "int", "8", 8, 128),
        new(SettingKeys.MaxViewers, "Partage", "Spectateurs maximum par conversation partagée", "int", "20", 1, 1000),
        new(SettingKeys.BufferEvents, "Partage", "Événements gardés en mémoire pour les nouveaux spectateurs", "int", "500", 10, 10000),
        new(SettingKeys.MaxSchedulesPerUser, "Tâches planifiées", "Tâches planifiées par utilisateur", "int", "20", 1, 1000),
        new(SettingKeys.MinScheduleMinutes, "Tâches planifiées", "Intervalle minimal entre deux occurrences (minutes)", "int", "5", 1, 10080),
        new(SettingKeys.RelayMaxRequestMb, "Providers", "Taille maximale d’une requête relayée (Mo)", "int", "20", 1, 512),
        new(SettingKeys.UsageRetentionDays, "Statistiques", "Durée de conservation des statistiques partagées (jours)", "int", "365", 7, 3650),
    ];

    readonly ConcurrentDictionary<string, string> values = new();

    /// <summary>Raised after a feature switch changed (feature key, new state).</summary>
    public event Action<string, bool>? FeatureChanged;

    public async Task LoadAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        values.Clear();
        foreach (var entry in await db.Settings.AsNoTracking().ToListAsync()) values[entry.Key] = entry.Value;
    }

    public bool FeatureEnabled(string feature) => values.TryGetValue("feature." + feature, out var value)
        ? value == "true"
        : Features.All.FirstOrDefault(x => x.Key == feature)?.DefaultEnabled ?? false;

    public IReadOnlyDictionary<string, bool> FeatureStates() => Features.All.ToDictionary(x => x.Key, x => FeatureEnabled(x.Key));

    public string Raw(string key) => values.TryGetValue(key, out var value) ? value : Catalog.First(x => x.Key == key).Default;
    public int Int(string key) => int.Parse(Raw(key), CultureInfo.InvariantCulture);
    public bool Bool(string key) => Raw(key) == "true";

    public Task SetFeatureAsync(string feature, bool enabled)
    {
        if (!Features.IsKnown(feature)) throw ApiException.BadRequest("unknown_feature", "Unknown feature: " + feature);
        return ChangeFeatureAsync(feature, enabled);
    }

    async Task ChangeFeatureAsync(string feature, bool enabled)
    {
        var before = FeatureEnabled(feature);
        await StoreAsync("feature." + feature, enabled ? "true" : "false");
        if (before != enabled) FeatureChanged?.Invoke(feature, enabled);
    }

    /// <summary>Free-text values that are not tunable limits (the announcement, the update state): stored like the settings, outside the catalog.</summary>
    public string Text(string key, string fallback = "") => values.TryGetValue(key, out var value) ? value : fallback;

    public Task SetTextAsync(string key, string value) => StoreAsync(key, value);

    public async Task RemoveTextAsync(params string[] keys)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Settings.Where(x => keys.Contains(x.Key)).ExecuteDeleteAsync();
        foreach (var key in keys) values.TryRemove(key, out _);
    }

    public Task SetAsync(string key, string value)
    {
        var info = Catalog.FirstOrDefault(x => x.Key == key) ?? throw ApiException.BadRequest("unknown_setting", "Unknown setting: " + key);
        value = value.Trim();
        if (info.Type == "bool")
        {
            if (!bool.TryParse(value, out var flag)) throw ApiException.BadRequest("invalid_setting", $"{key} must be true or false.");
            value = flag ? "true" : "false";
        }
        else
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < info.Min || number > info.Max)
                throw ApiException.BadRequest("invalid_setting", $"{key} must be an integer between {info.Min} and {info.Max}.");
            value = number.ToString(CultureInfo.InvariantCulture);
        }
        return StoreAsync(key, value);
    }

    async Task StoreAsync(string key, string value)
    {
        await using var db = await factory.CreateDbContextAsync();
        var entry = await db.Settings.FindAsync(key);
        if (entry == null) db.Settings.Add(new SettingEntry { Key = key, Value = value }); else entry.Value = value;
        await db.SaveChangesAsync();
        values[key] = value;
    }
}
