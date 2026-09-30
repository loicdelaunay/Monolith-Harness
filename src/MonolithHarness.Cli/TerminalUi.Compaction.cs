using MonolithHarness.Core;

namespace MonolithHarness.Cli;

public sealed partial class TerminalUi
{
    async Task CompactionPreferences(WorkspaceSnapshot snapshot)
    {
        var config = FeatureSettings.Read(snapshot.State.FeaturesJson).Compaction;
        while (true)
        {
            var key = await Prompt(L("Compactage", "Compaction"),
                L("Les modifications s’appliquent aux prochains envois. Les messages originaux restent dans la base.", "Changes apply to subsequent requests. Original messages remain in the database."),
                [new("automatic", L("Automatique", "Automatic") + " · " + config.AutoEnabled),
                 new("threshold", L("Seuil → cible", "Trigger → target") + $" · {config.TriggerPercent}% → {config.TargetPercent}%"),
                 new("strength", L("Force", "Strength") + " · " + config.Strength), new("strategy", L("Stratégie", "Strategy") + " · " + config.Strategy),
                 new("recent", L("Échanges récents prioritaires", "Prioritized recent exchanges") + " · " + config.RecentTurns),
                 new("custom", L("Taille et budget personnalisés", "Custom size and budget") + $" · {config.CustomRetentionPercent}% / {config.CustomMaxSummaryTokens} tokens"),
                 new("instructions", L("Consignes personnalisées", "Custom instructions")), new("defaults", L("Rétablir les réglages", "Restore defaults")),
                 new("save", L("Enregistrer", "Save")), new("cancel", L("Annuler", "Cancel"))]);
            if (key == null || key == "cancel") return;
            if (key == "automatic") config.AutoEnabled = !config.AutoEnabled;
            if (key == "defaults") config = new();
            if (key == "strength")
            {
                var selected = await Prompt(L("Force du compactage", "Compaction strength"), choices:
                    [new("gentle", L("Léger", "Gentle"), "40% · ≤ 4000 tokens"), new("balanced", L("Équilibré", "Balanced"), "20% · ≤ 2000 tokens"),
                     new("strong", L("Fort", "Strong"), "10% · ≤ 1000 tokens"), new("custom", L("Personnalisé", "Custom"))]);
                if (selected != null) config.Strength = selected;
            }
            if (key == "strategy")
            {
                var selected = await Prompt(L("Stratégie de compactage", "Compaction strategy"), choices:
                    [new("recent", L("Conserver les échanges récents", "Keep recent exchanges")), new("full", L("Résumer tout l’historique", "Summarize the entire history")),
                     new("tools", L("Alléger d’abord les échanges avec les outils", "Reduce tool exchanges first"))]);
                if (selected != null) config.Strategy = selected;
            }
            if (key is "threshold" or "custom")
            {
                var value = await Prompt(key == "threshold" ? L("Seuil et cible (%)", "Trigger and target (%)") : L("Taille (%) et budget (tokens)", "Size (%) and budget (tokens)"),
                    key == "threshold" ? L("Deux nombres séparés par un espace. Exemple : 90 60. Cible < seuil.", "Two numbers separated by a space. Example: 90 60. Target < trigger.")
                        : L("Exemple : 20 2000. Taille : 5–60 % ; budget : 128–32000. Activez Personnalisé pour appliquer ces valeurs.", "Example: 20 2000. Size: 5–60%; budget: 128–32000. Select Custom to apply these values."));
                if (value == null) continue;
                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !int.TryParse(parts[0], out int a) || !int.TryParse(parts[1], out int b) ||
                    (key == "threshold" ? a is < 10 or > 99 || b is < 5 or > 90 || b >= a : a is < 5 or > 60 || b is < 128 or > 32000))
                { Post(() => notice = L("Valeurs invalides.", "Invalid values.")); continue; }
                if (key == "threshold") { config.TriggerPercent = a; config.TargetPercent = b; }
                else { config.CustomRetentionPercent = a; config.CustomMaxSummaryTokens = b; }
            }
            if (key == "recent")
            {
                var value = await Prompt(L("Échanges récents prioritaires (0–10)", "Prioritized recent exchanges (0–10)"));
                if (value != null && int.TryParse(value, out var n) && n is >= 0 and <= 10) config.RecentTurns = n;
                else if (value != null) Post(() => notice = L("Valeurs invalides.", "Invalid values."));
            }
            if (key == "instructions")
            {
                var value = await Prompt(L("Consignes personnalisées", "Custom instructions"), config.CustomInstruction);
                if (value != null && value.Length <= 8000) config.CustomInstruction = value;
                else if (value != null) Post(() => notice = L("8 000 caractères maximum.", "8,000 characters maximum."));
            }
            if (key == "save")
            {
                config.Validate();
                await client.State(s => { var settings = FeatureSettings.Read(s.FeaturesJson); settings.Compaction = config; s.FeaturesJson = settings.Json(); });
                return;
            }
        }
    }
}
