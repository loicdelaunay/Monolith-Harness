namespace MonolithHarness.App;

/// <summary>Bounded application cache shared by the independently recycled Markdown views.</summary>
internal static class MermaidDiagramCache
{
    const int MaximumEntries = 64, MaximumCharacters = 4 * 1024 * 1024;
    sealed record Entry(string Svg, LinkedListNode<string> Node);
    static readonly Dictionary<string, Entry> entries = [];
    static readonly LinkedList<string> order = new();
    static readonly object gate = new();
    static int characters;
    static string Key(string source, bool dark, string palette) => (dark ? "dark\n" : "light\n") + palette + "\n" + source;
    public static string? Read(string source, bool dark, string palette = "")
    {
        lock (gate)
        {
            if (!entries.TryGetValue(Key(source, dark, palette), out var item)) return null;
            order.Remove(item.Node); order.AddLast(item.Node); return item.Svg;
        }
    }
    public static void Write(string source, bool dark, string svg, string palette = "")
    {
        if (source.Length > 50000 || svg.Length > MaximumCharacters) return;
        lock (gate)
        {
            var key = Key(source, dark, palette);
            if (entries.Remove(key, out var previous)) { characters -= previous.Svg.Length; order.Remove(previous.Node); }
            entries.Add(key, new(svg, order.AddLast(key))); characters += svg.Length;
            while (entries.Count > MaximumEntries || characters > MaximumCharacters)
            {
                var first = order.First!; characters -= entries[first.Value].Svg.Length;
                entries.Remove(first.Value); order.RemoveFirst();
            }
        }
    }
}
