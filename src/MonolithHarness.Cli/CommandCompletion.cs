namespace MonolithHarness.Cli;

public sealed class CommandCompletion(IReadOnlyList<Choice> commands)
{
    public IReadOnlyList<Choice> Skills { get; set; } = [];
    string previous = "";
    bool dismissed;
    public int Selected { get; private set; }

    public void Dismiss(InputBuffer input)
    {
        previous = input.Text; Selected = 0; dismissed = true;
    }

    public List<Choice> Matches(InputBuffer input)
    {
        if (previous != input.Text) { previous = input.Text; Selected = 0; dismissed = false; }
        if (dismissed || input.HasSelection || !input.Text.StartsWith('/') || input.Text.Any(char.IsWhiteSpace) || input.Cursor != input.Text.Length) return [];
        return Skills.Concat(commands).DistinctBy(c => c.Value, StringComparer.OrdinalIgnoreCase).Where(c => c.Value.StartsWith(input.Text, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public bool Handle(ConsoleKeyInfo key, InputBuffer input)
    {
        if (key.Modifiers != 0) return false;
        var matches = Matches(input);
        if (matches.Count == 0) return false;
        Selected = Math.Clamp(Selected, 0, matches.Count - 1);
        switch (key.Key)
        {
            case ConsoleKey.UpArrow: Selected = (Selected + matches.Count - 1) % matches.Count; return true;
            case ConsoleKey.DownArrow: Selected = (Selected + 1) % matches.Count; return true;
            case ConsoleKey.Escape: dismissed = true; return true;
            case ConsoleKey.Tab:
            case ConsoleKey.Enter:
                input.Set(matches[Selected].Value + " ");
                return true;
            default: return false;
        }
    }
}
