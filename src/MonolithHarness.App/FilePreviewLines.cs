namespace MonolithHarness.App;

/// <summary>Maps file line numbers to UTF-16 offsets without normalizing its line endings.</summary>
internal sealed class FilePreviewLines
{
    readonly int[] starts;
    readonly int length;
    internal int Count => starts.Length;
    internal FilePreviewLines(string text)
    {
        length = text.Length;
        var offsets = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; offsets.Add(i + 1); }
            else if (text[i] == '\n') offsets.Add(i + 1);
        }
        starts = offsets.ToArray();
    }
    internal int LineAt(int offset)
    {
        var index = Array.BinarySearch(starts, Math.Clamp(offset, 0, length));
        return (index >= 0 ? index : ~index - 1) + 1;
    }
    internal (int Start, int Length) Range(int first, int last)
    {
        if (first < 1 || last < first || last > Count)
            throw new IOException($"Référence hors du fichier ({Count} lignes) / Reference outside the file ({Count} lines): L.{first}–{last}.");
        var start = starts[first - 1];
        return (start, (last < Count ? starts[last] : length) - start);
    }
}
