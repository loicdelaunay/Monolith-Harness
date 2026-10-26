using MonolithHarness.Core;

namespace MonolithHarness.Cli;

public sealed partial class TerminalUi
{
    async Task MemoryPreferences()
    {
        while (true)
        {
            var snapshot = await client.Snapshot();
            var settings = FeatureSettings.Read(snapshot.State.FeaturesJson);
            string Mark(bool value) => value ? "[x] " : "[ ] ";
            var choices = new List<Choice>
            {
                new(MemoryTools.ConversationSkill, Mark(Skills.Enabled(snapshot.State.EnabledSkills, MemoryTools.ConversationSkill)) + L("Niveau 1 · Mémoire de conversation", "Level 1 · Conversation memory")),
                new(MemoryTools.SharedSkill, Mark(Skills.Enabled(snapshot.State.EnabledSkills, MemoryTools.SharedSkill)) + L("Niveau 2 · Mémoire partagée", "Level 2 · Shared memory")),
                new("read", Mark(settings.MemoryForceRead) + L("Forcer la lecture en début de discussion", "Force read at conversation start"),
                    L("Les 20 mémoires accessibles les plus récentes sont transmises au modèle avant la première réponse.", "The 20 most recent accessible memories are given to the model before the first response.")),
                new("write", Mark(settings.MemoryForceWrite) + L("Forcer l’écriture en fin de discussion", "Force write at conversation end"),
                    L("Requête supplémentaire après chaque réponse pour enregistrer les faits durables, selon vos autorisations.", "Extra request after each response to save durable facts, subject to your permissions.")),
                new("done", L("Terminer", "Done"))
            };
            var action = await Prompt(L("Mémoire", "Memory"), L("Les options forcées nécessitent un niveau de mémoire activé et un fournisseur direct. Chaque changement est enregistré.",
                "Forced options require an enabled memory level and a direct provider. Each change is saved."), choices);
            if (action is null or "done") return;
            if (action is MemoryTools.ConversationSkill or MemoryTools.SharedSkill) await client.State(state =>
            {
                var ids = state.EnabledSkills.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                if (!ids.Remove(action)) ids.Add(action);
                state.EnabledSkills = string.Join(',', ids);
            });
            else if (action == "read") await SaveFeatures(features => features.MemoryForceRead = !features.MemoryForceRead);
            else if (action == "write") await SaveFeatures(features => features.MemoryForceWrite = !features.MemoryForceWrite);
            await Refresh();
        }
    }
}
