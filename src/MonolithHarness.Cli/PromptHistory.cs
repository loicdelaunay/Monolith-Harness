namespace MonolithHarness.Cli;

/// <summary>Per-chat recall of saved messages and inputs not yet reflected in saved history.</summary>
internal sealed class PromptHistory
{
    const int Capacity = 500;
    sealed record Entry(int Id, string Text);
    readonly List<Entry> entries = [];
    int position = -1;
    string draft = "", recalled = "";

    public void Saved(int id, string text, bool historical = false)
    {
        if (string.IsNullOrWhiteSpace(text) || entries.Any(entry => entry.Id == id)) return;
        int pending = historical ? -1 : entries.FindIndex(entry => entry.Id == 0 && entry.Text == text);
        if (pending >= 0) entries[pending] = new(id, text);
        else
        {
            int index = historical ? entries.FindIndex(entry => entry.Id == 0 || entry.Id > id) : -1;
            if (index < 0) entries.Add(new(id, text));
            else { entries.Insert(index, new(id, text)); if (position >= index) position++; }
        }
        Trim();
    }

    public void Submitted(string text)
    {
        if (!string.IsNullOrWhiteSpace(text)) entries.Add(new(0, text));
        position = -1;
        Trim();
    }

    void Trim()
    {
        if (entries.Count <= Capacity) return;
        entries.RemoveAt(0);
        if (position >= 0) position = Math.Max(0, position - 1);
    }

    public void RestoreDraft(InputBuffer input)
    {
        if (position >= 0 && input.Text == recalled) input.Set(draft);
        position = -1;
    }

    public bool Recall(InputBuffer input, bool older)
    {
        if (input.HasSelection) return false;
        if (position >= 0 && input.Text != recalled) position = -1;
        if (position < 0)
        {
            // Keep vertical editing within a multiline draft; recall only at its edges.
            if (older && input.Text.AsSpan(0, input.Cursor).Contains('\n') ||
                !older && input.Text.AsSpan(input.Cursor).Contains('\n')) return false;
            if (!older) return false;
        }
        if (entries.Count == 0) return false;
        if (position < 0) { draft = input.Text; position = entries.Count; }
        position = Math.Clamp(position + (older ? -1 : 1), 0, entries.Count);
        if (position == entries.Count) { input.Set(draft); position = -1; recalled = ""; }
        else { recalled = entries[position].Text; input.Set(recalled); }
        return true;
    }
}
