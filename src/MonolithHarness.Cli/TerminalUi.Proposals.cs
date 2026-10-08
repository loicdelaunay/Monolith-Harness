using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;
namespace MonolithHarness.Cli;
public sealed partial class TerminalUi
{
    async Task ReviewProposals(int chatId)
    {
        if (Running(chatId)) throw new InvalidOperationException(L("Attendez la fin de la génération avant de revoir les fichiers.", "Wait for generation to finish before reviewing files."));
        var batch = await FileProposals.ReadAsync(client.Database, chatId, lifetime.Token);
        if (batch == null || batch.Files.Count == 0) { Post(() => notice = L("Aucune proposition en attente", "No pending proposal")); return; }
        var selected = new List<string>();
        foreach (var file in batch.Files)
        {
            var answer = await Prompt(L("Revue : ", "Review: ") + file.Path, FileProposals.Diff(file),
                [new("skip", L("Conserver en attente", "Keep pending")), new("apply", L("Sélectionner pour application", "Select for application"))], ct: lifetime.Token);
            if (answer == "apply") selected.Add(file.Path);
        }
        if (selected.Count == 0) return;
        if (await Prompt(L("Appliquer les fichiers sélectionnés ?", "Apply selected files?"), string.Join("\n", selected),
            [new("no", L("Annuler", "Cancel")), new("yes", L("Appliquer", "Apply"))], ct: lifetime.Token) != "yes") return;
        if (Running(chatId)) throw new InvalidOperationException(L("Une génération a commencé : application annulée.", "Generation started: application cancelled."));
        await using var db = client.OpenDb();
        var chat = await db.Chats.AsNoTracking().SingleAsync(c => c.Id == chatId, lifetime.Token);
        var project = ProjectResources.Effective(chat, await db.Projects.AsNoTracking().SingleAsync(p => p.Id == chat.ProjectId, lifetime.Token));
        await FileProposals.ApplyAsync(client.Database, chatId, batch.Revision, selected, new SourceAccess(project.GetSourceFolders()), lifetime.Token);
        Post(() => notice = L("Fichiers appliqués : ", "Files applied: ") + selected.Count);
    }
}
