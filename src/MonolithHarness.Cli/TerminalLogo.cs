namespace MonolithHarness.Cli;

/// <summary>The monolith mark drawn with an orange CLI accent and the active foreground.</summary>
internal static class TerminalLogo
{
    internal static readonly string[] Outline =
    [
        "..##....",
        ".####...",
        ".#####..",
        ".###..C.",
        ".###.CC.",
        ".###.CC.",
        ".###.CC.",
        ".###.CC."
    ];

    public const int Width = 8;
    internal const int BrandAccent = 0xF97316;

    public static void Draw(TerminalCanvas canvas, int x, int y, Palette palette)
    {
        for (int row = 0; row < Outline.Length; row += 2)
            for (int column = 0; column < Width; column++)
            {
                int top = Color(row, column, palette), bottom = Color(row + 1, column, palette);
                if (top == 0 && bottom == 0) continue;
                var symbol = top == 0 ? "▄" : "▀";
                canvas.Write(x + column, y + row / 2, symbol,
                    new(top == 0 ? bottom : top, top == 0 || bottom == 0 ? palette.Background : bottom), 1);
            }
    }

    private static int Color(int row, int column, Palette palette)
    {
        return Outline[row][column] switch
        {
            '#' => palette.Foreground,
            'C' => BrandAccent,
            _ => 0
        };
    }
}
