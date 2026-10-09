using System.Text.Json;
using MonolithHarness.Core;

namespace MonolithHarness.Cli;

public sealed partial class TerminalUi
{
    async Task InformationPreferences()
    {
        while (true)
        {
            var snapshot = await client.Snapshot();
            var settings = FeatureSettings.Read(snapshot.State.FeaturesJson).Information;
            var enabled = Skills.Enabled(snapshot.State.EnabledSkills, InformationSkill.SkillId);
            var choices = new List<Choice> { new("enabled", (enabled ? "[x] " : "[ ] ") + L("Activer le skill Informations", "Enable Information skill")) };
            choices.AddRange(InformationSettings.Options.Select(option => new Choice(option.Id,
                (option.Read(settings) ? "[x] " : "[ ] ") + L(option.French, option.English), L(option.FrenchDescription, option.EnglishDescription))));
            choices.Add(new("clock", L("Horloge", "Clock") + " · " + (settings.Clock == "utc" ? "UTC" : L("Fuseau du PC", "Computer time zone"))));
            choices.Add(new("notes", L("Informations complémentaires", "Additional context")));
            choices.Add(new("preview", L("Aperçu local", "Local preview")));
            choices.Add(new("done", L("Terminer", "Done")));
            var action = await Prompt("Informations / Information", L("Catégories transmises au modèle. Chaque changement est enregistré ; aucun accès supplémentaire n’est accordé.",
                "Categories shared with the model. Each change is saved; no additional access is granted."), choices);
            if (action is null or "done") return;
            if (action == "enabled") await client.State(state =>
            {
                var ids = state.EnabledSkills.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                if (!ids.Remove(InformationSkill.SkillId)) ids.Add(InformationSkill.SkillId);
                state.EnabledSkills = string.Join(',', ids);
            });
            else if (action == "clock")
            {
                var clock = await Prompt(L("Horloge de référence", "Reference clock"), choices: [new("local", L("Fuseau horaire du PC", "Computer time zone")), new("utc", "UTC")]);
                if (clock != null) await SaveFeatures(features => features.Information.Clock = clock);
            }
            else if (action == "notes")
            {
                var notes = await Prompt(L("Informations complémentaires · 4000 caractères maximum", "Additional context · 4000 characters maximum"), initial: settings.AdditionalContext);
                if (notes != null) await SaveFeatures(features => features.Information.AdditionalContext = notes);
            }
            else if (action == "preview")
            {
                var context = new InformationSkill.Context(CurrentProject, CurrentChat, snapshot.State.Language);
                var info = await Task.Run(() => InformationSkill.Capture(settings, context));
                await Prompt(L("Aperçu local Informations", "Local Information preview"), info.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), [new("ok", "OK")]);
            }
            else if (InformationSettings.Options.FirstOrDefault(option => option.Id == action) is { } option)
                await SaveFeatures(features => option.Write(features.Information, !option.Read(features.Information)));
            await Refresh();
        }
    }
}
