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
        CheckStreamingBoundaries(check);
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

    static void CheckStreamingBoundaries(Action<bool, string> check)
    {
        string[] samples =
        [
            "# Titre\n\nParagraphe **gras**.\n\n- un\n- deux\n\n- trois après une ligne vide\n\nSuite du texte.\n\n1. premier\n\n2. second\n\nFin.",
            "Avant\n\n```csharp\nvar x = 1;\n\n\nConsole.WriteLine(x);\n```\n\nAprès le code.\n\n~~~\nbloc\n\ntilde\n~~~\n\nTexte final.",
            "Formule :\n\n$$\na = b\n\nc = d\n$$\n\nTexte après la formule.\n\n> citation\n\n> autre citation\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\nFin du tableau.",
            "- élément\n\n  suite indentée de l'élément\n\nParagraphe.\n\n    code indenté\n\n    encore du code\n\nDernier paragraphe avec `code` et [lien](https://example.com).",
            "## Plan\n\n- un\n- deux\n\n- trois\n\nTexte **gras**.\n\n```csharp\nvar x = 1;\n\nvar y = 2;\n```\n\n1. premier\n\n2. second\n\n10) dixième\n\n2024 est une année.\n\nConclusion.",
        ];
        var pipeline = MarkdownPipelineHelper.Pipeline;
        foreach (var sample in samples)
        {
            // Simulate a reply growing a few characters at a time, as the renderer sees it.
            int stable = 0; var chunks = new List<string>(); var exact = true;
            for (int length = 1; length <= sample.Length; length++)
            {
                var text = sample[..length];
                var next = MarkdownStreaming.StableBoundary(text, stable);
                if (next < stable || next > text.Length) { exact = false; break; }
                if (next > stable) { chunks.Add(text[stable..next]); stable = next; }
                var html = string.Concat(chunks.Select(x => Markdig.Markdown.ToHtml(x, pipeline))) + Markdig.Markdown.ToHtml(text[stable..], pipeline);
                if (html != Markdig.Markdown.ToHtml(text, pipeline)) { exact = false; break; }
            }
            check(exact && stable > 0, "Streaming Markdown boundaries keep completed blocks identical: " + sample[..12]);
        }
    }
}
