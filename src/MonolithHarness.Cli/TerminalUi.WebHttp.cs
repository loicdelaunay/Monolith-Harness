using MonolithHarness.Core;

namespace MonolithHarness.Cli;

public sealed partial class TerminalUi
{
    async Task WebHttpPreferences(WorkspaceSnapshot snapshot)
    {
        var current = FeatureSettings.Read(snapshot.State.FeaturesJson).WebHttpResponseMode;
        var value = await Prompt(L("Recherche web · HTTP", "Web research · HTTP"),
            L("Smart économise le contexte : le modèle recherche et lit les passages utiles dans les pages conservées. Legacy charge le contenu et les en-têtes directement dans le contexte. Appliqué aux prochains envois.",
                "Smart saves context: the model searches and reads relevant passages in stored pages. Legacy loads the body and headers directly into context. Applies to subsequent messages."),
            [new("smart", L("Smart (recommandé)", "Smart (recommended)"), current == "smart" ? "✓" : ""),
                new("legacy", "Legacy", current == "legacy" ? "✓" : "")]);
        if (value != null) await SaveFeatures(settings => settings.WebHttpResponseMode = value);
    }
    async Task ArtifactPreferences(WorkspaceSnapshot snapshot)
    {
        var config = FeatureSettings.Read(snapshot.State.FeaturesJson);
        var action = await Prompt(L("Entretien · Artefacts", "Maintenance · Artifacts"),
            L("Le cache HTTP inutilisé est nettoyé au démarrage puis toutes les heures. Lire ou rechercher une page prolonge sa conservation.",
                "Unused HTTP cache is cleaned at startup and hourly. Reading or searching a page extends retention."),
            [new("toggle", (config.AutoCleanArtifacts ? "[x] " : "[ ] ") + L("Nettoyer les artefacts", "Clean tool artifacts")),
                new("days", L("Durée sans utilisation : ", "Time without use: ") + config.ArtifactRetentionDays + L(" jours", " days"))]);
        if (action == "toggle") await SaveFeatures(settings => settings.AutoCleanArtifacts = !config.AutoCleanArtifacts);
        if (action == "days")
        {
            var value = await Prompt(L("Jours sans utilisation avant nettoyage", "Unused days before cleanup"), "1–3650", initial: config.ArtifactRetentionDays.ToString());
            if (value == null) return;
            if (!int.TryParse(value, out var days) || days is < 1 or > 3650) throw new ArgumentException("1–3650 jours / days.");
            await SaveFeatures(settings => settings.ArtifactRetentionDays = days);
        }
    }
}
