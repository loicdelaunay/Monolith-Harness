using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;

static class MessageDisplayChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        check(MarkdownDisplay.HasVisuals(MarkdownPipelineHelper.Parse("Une formule $x^2$ dans le texte.")), "Math inline detected");
        check(MarkdownDisplay.HasVisuals(MarkdownPipelineHelper.Parse("$$\n\\frac{1}{2}\n$$")), "Display math detected");
        check(MarkdownDisplay.HasVisuals(MarkdownPipelineHelper.Parse("```mermaid\nflowchart LR\n A-->B\n```")), "Mermaid block detected");
        check(!MarkdownDisplay.HasVisuals(MarkdownPipelineHelper.Parse("```text\n$x$\n```")), "Code stays literal");
        var html = MarkdownDisplay.Html("# Titre\n\nParagraphe.\n\n- Un\n- Deux\n\n$x^2$\n\n```mermaid\nflowchart LR\n A-->B\n```\n\n<script>alert(1)</script>");
        check(html.Contains("class=\"math") && html.Contains("class=\"mermaid\"") && !html.Contains("<script>"), "HTML keeps display formats and escapes raw scripts");
        var folder = Path.Combine(Path.GetTempPath(), "mh-display-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var database = Path.Combine(folder, "database.sqlite");
        try
        {
            await using var db = new HarnessDb(database); await db.InitializeAsync();
            var chat = await db.Chats.FirstAsync(); var provider = await db.Providers.FirstAsync();
            var a = await ConversationInbox.AddAsync(database, chat.Id, provider.Id, "A", [], "queued");
            var b = await ConversationInbox.AddAsync(database, chat.Id, provider.Id, "B", [new() { Name = "keep.png", Mime = "image/png", Data = [1, 2] }], "queued");
            var c = await ConversationInbox.AddAsync(database, chat.Id, provider.Id, "C", [], "queued");
            await ConversationInbox.MoveAsync(database, chat.Id, b.Id, -1);
            await ConversationInbox.MoveAsync(database, chat.Id, c.Id, -1);
            check((await ConversationInbox.Ordered(db.PendingInputs.AsNoTracking()).Select(x => x.Text).ToListAsync()).SequenceEqual(["B", "C", "A"]), "Queue moves persist in requested order");
            var d = await ConversationInbox.AddAsync(database, chat.Id, provider.Id, "D", [], "queued");
            var steering = await ConversationInbox.AddAsync(database, chat.Id, provider.Id, "Steer", [], "steering");
            await ConversationInbox.MoveAsync(database, chat.Id, b.Id, -1);
            await using var reopened = new HarnessDb(database);
            var items = await ConversationInbox.Ordered(reopened.PendingInputs.AsNoTracking()).ToListAsync();
            check(items.Select(x => x.Text).SequenceEqual(["Steer", "B", "C", "A", "D"]), "New messages append; steering retains priority after reopen");
            check(items.Single(x => x.Id == b.Id).Images()[0].Data.SequenceEqual(new byte[] { 1, 2 }) && items.Single(x => x.Id == b.Id).ProviderId == provider.Id, "Reorder preserves attachments and routing");
            bool refused = false;
            try { await ConversationInbox.MoveAsync(database, chat.Id + 999, b.Id, 1); } catch (InvalidOperationException) { refused = true; }
            check(refused, "Cross-conversation reorder refused");
            await db.PendingInputs.Where(x => x.Id == d.Id).ExecuteDeleteAsync(); refused = false;
            try { await ConversationInbox.MoveAsync(database, chat.Id, d.Id, -1); } catch (InvalidOperationException) { refused = true; }
            check(refused, "Consumed or deleted messages are not recreated");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
    }
}
