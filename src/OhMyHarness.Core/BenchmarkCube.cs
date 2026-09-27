namespace OhMyHarness.Core;

/// <summary>Independent facelet permutation model, shared with the visual grader only.</summary>
public static class BenchmarkCube
{
    public const string Solved = "UUUUUUUUURRRRRRRRRFFFFFFFFFDDDDDDDDDLLLLLLLLLBBBBBBBBB";
    record Sticker(int X, int Y, int Z, int Nx, int Ny, int Nz);
    static readonly Sticker[] stickers = Build();
    static readonly Dictionary<Sticker, int> index = stickers.Select((s, i) => (s, i)).ToDictionary(p => p.s, p => p.i);
    static Sticker[] Build()
    {
        var list = new List<Sticker>();
        foreach (var face in "URFDLB") for (var r = 0; r < 3; r++) for (var c = 0; c < 3; c++)
            list.Add(face switch
            {
                'U' => new(c - 1, 1, r - 1, 0, 1, 0), 'R' => new(1, 1 - r, 1 - c, 1, 0, 0),
                'F' => new(c - 1, 1 - r, 1, 0, 0, 1), 'D' => new(c - 1, -1, 1 - r, 0, -1, 0),
                'L' => new(-1, 1 - r, c - 1, -1, 0, 0), _ => new(1 - c, 1 - r, -1, 0, 0, -1)
            });
        return list.ToArray();
    }
    public static string Apply(string state, string move)
    {
        if (state.Length != 54 || string.IsNullOrEmpty(move) || !"URFDLB".Contains(move[0]) || move.Length > 2 || (move.Length == 2 && move[1] is not ('\'' or '2')))
            throw new FormatException("Invalid cube state or move.");
        var face = move[0]; var axis = "RL".Contains(face) ? 0 : "UD".Contains(face) ? 1 : 2;
        var side = "RUF".Contains(face) ? 1 : -1; var turns = move.Length == 1 ? 1 : move[1] == '2' ? 2 : 3;
        (int, int, int) Rotate(int x, int y, int z) => axis switch { 0 => (x, side * z, -side * y), 1 => (-side * z, y, side * x), _ => (side * y, -side * x, z) };
        for (var turn = 0; turn < turns; turn++)
        {
            var next = state.ToCharArray();
            for (var i = 0; i < 54; i++)
            {
                var s = stickers[i]; if ((axis == 0 ? s.X : axis == 1 ? s.Y : s.Z) != side) continue;
                var (x, y, z) = Rotate(s.X, s.Y, s.Z); var (nx, ny, nz) = Rotate(s.Nx, s.Ny, s.Nz);
                next[index[new(x, y, z, nx, ny, nz)]] = state[i];
            }
            state = new(next);
        }
        return state;
    }
    public static string Apply(string state, IEnumerable<string> moves) { foreach (var move in moves) state = Apply(state, move); return state; }
    public static string[] Scramble(Random random, int length)
    {
        var moves = new List<string>();
        while (moves.Count < length)
        {
            var face = "URFDLB"[random.Next(6)]; if (moves.Count > 0 && moves[^1][0] == face) continue;
            moves.Add(face + new[] { "", "'", "2" }[random.Next(3)]);
        }
        return moves.ToArray();
    }
}
