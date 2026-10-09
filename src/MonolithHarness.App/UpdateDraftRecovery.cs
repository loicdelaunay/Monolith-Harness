using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    // A one-time restart handoff, kept with portable user data and never sent to GitHub.
    sealed record UpdateDraft(string Text, List<Attachment> Images);
    sealed record UpdateDraftSnapshot(int? SelectedChatId, Dictionary<int, UpdateDraft> Drafts);
    static string UpdateDraftPath => Path.Combine(PortableStorage.Workspace, "gui-update-drafts.json");

    string CaptureUpdateDrafts()
    {
        SaveConversationDraft();
        return JsonSerializer.Serialize(new UpdateDraftSnapshot(chat?.Id,
            conversationDrafts.OrderBy(pair => pair.Key)
                .Where(pair => !string.IsNullOrEmpty(pair.Value.Text) || pair.Value.Images.Count > 0)
                .ToDictionary(pair => pair.Key, pair => new UpdateDraft(pair.Value.Text, pair.Value.Images))));
    }

    async Task SaveUpdateDraftsAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(PortableStorage.Workspace);
        var temporary = UpdateDraftPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            // The UI remains usable during I/O. If typing/navigation changes the snapshot,
            // save again before the caller performs its final checks and synchronous exit.
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var snapshot = CaptureUpdateDrafts();
                await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write,
                    FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(snapshot), ct);
                    await stream.FlushAsync(ct);
                    stream.Flush(flushToDisk: true);
                }
                ct.ThrowIfCancellationRequested();
                File.Move(temporary, UpdateDraftPath, overwrite: true);
                if (snapshot == CaptureUpdateDrafts()) return;
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    async Task RestoreUpdateDraftsAsync()
    {
        if (!File.Exists(UpdateDraftPath)) return;
        try
        {
            var snapshot = JsonSerializer.Deserialize<UpdateDraftSnapshot>(await File.ReadAllTextAsync(UpdateDraftPath))
                ?? throw new InvalidDataException("Invalid update draft snapshot.");
            var existing = await db.Chats.AsNoTracking().Select(item => new { item.Id, item.ProjectId })
                .ToDictionaryAsync(item => item.Id);
            foreach (var (id, draft) in snapshot.Drafts)
                if (existing.ContainsKey(id)) conversationDrafts[id] = (draft.Text, draft.Images);
            if (snapshot.SelectedChatId is { } selected && existing.TryGetValue(selected, out var restored))
            {
                state.ChatId = selected;
                state.ProjectId = restored.ProjectId;
            }
            // Normal conversation selection restores the composer and its image attachments.
            // Consume the handoff so sent drafts cannot reappear on a later normal startup.
            File.Delete(UpdateDraftPath);
        }
        catch (Exception ex)
        {
            AppLog.Write(AppLogLevel.Warning, "gui.update_draft_restore_failed", ex);
            ShowStatus(WorkflowText("Impossible de restaurer les brouillons de la mise à jour : ",
                "Could not restore update drafts: ") + ex.Message, StatusKind.Error);
        }
    }

    void ClearUpdateDrafts()
    {
        try { File.Delete(UpdateDraftPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AppLog.Write(AppLogLevel.Warning, "gui.update_draft_cleanup_failed", ex); }
    }
}
