using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

/// <summary>Integer-cell drawing operations shared by the GUI and CLI asset tools.</summary>
public static class AssetPixelEditor
{
    public static bool Handles(string action) => action is "pixel" or "erase_pixel" or "pixel_rect" or "pixel_line"
        or "pixel_ellipse" or "pixel_brush" or "pixel_fill" or "pixel_replace" or "pixel_stamp" or "pixel_transform";

    public static void Apply(AssetDocument doc, AssetLayer layer, List<AssetLayer> layers, JsonObject op)
    {
        int columns = doc.Width / doc.PixelSize, rows = doc.Height / doc.PixelSize;
        int I(string name, int fallback = 0) => op[name]?.GetValue<int>() ?? fallback;
        string S(string name, string fallback = "") => op[name]?.GetValue<string>() ?? fallback;
        bool B(string name, bool fallback = false) => op[name]?.GetValue<bool>() ?? fallback;
        var action = S("action");
        if (action is not ("pixel_brush" or "pixel_replace") && (op["x"] == null || op["y"] == null)) throw new ArgumentException("Pixel x and y required.");
        var pixels = layer.Pixels.ToDictionary(p => (p.X, p.Y), p => p.Color);
        string Normalize(string color)
        {
            var value = AssetRenderer.Color(color);
            return value.Alpha == 0 ? "none" : $"#{value.Red:X2}{value.Green:X2}{value.Blue:X2}{value.Alpha:X2}";
        }
        void Point(int x, int y)
        { if (x < 0 || y < 0 || x >= columns || y >= rows) throw new ArgumentException("Pixel outside canvas; use logical cell coordinates."); }
        void Region(int x, int y, int w, int h)
        {
            Point(x, y);
            if (w < 1 || h < 1 || w > columns - x || h > rows - y || (long)w * h > 262144)
                throw new ArgumentException("Pixel region must fit inside the grid (maximum 262144 cells).");
        }
        var symmetry = S("symmetry", "none");
        if (symmetry is not ("none" or "x" or "y" or "xy")) throw new ArgumentException("Symmetry: none, x, y or xy.");
        void SetOne(int x, int y, string color)
        {
            Point(x, y);
            if (color == "none") pixels.Remove((x, y)); else pixels[(x, y)] = color;
            if (pixels.Count > 20000) throw new ArgumentException("Maximum 20000 painted cells; reduce the region or canvas size.");
        }
        void Set(int x, int y, string color)
        {
            SetOne(x, y, color);
            if (symmetry is "x" or "xy") SetOne(columns - 1 - x, y, color);
            if (symmetry is "y" or "xy") SetOne(x, rows - 1 - y, color);
            if (symmetry == "xy") SetOne(columns - 1 - x, rows - 1 - y, color);
        }
        void Line(int x, int y, int x2, int y2, Action<int, int> paint)
        {
            Point(x, y); Point(x2, y2);
            int dx = Math.Abs(x2 - x), sx = x < x2 ? 1 : -1, dy = -Math.Abs(y2 - y), sy = y < y2 ? 1 : -1, error = dx + dy;
            while (true)
            {
                paint(x, y); if (x == x2 && y == y2) break;
                int twice = 2 * error;
                if (twice >= dy) { error += dy; x += sx; }
                if (twice <= dx) { error += dx; y += sy; }
            }
        }

        int x = I("x"), y = I("y"), w = I("width", 1), h = I("height", 1);
        if (action == "pixel_transform")
        {
            if (symmetry != "none") throw new ArgumentException("Use symmetry on drawing operations, not pixel_transform.");
            Region(x, y, w, h);
            int tx = I("to_x", x), ty = I("to_y", y);
            string transform = S("transform", "identity"), targetId = S("target_layer_id", layer.Id);
            if (transform is not ("identity" or "flip_x" or "flip_y" or "rotate_90" or "rotate_180" or "rotate_270")) throw new ArgumentException("Unknown pixel transform.");
            bool rotate = transform is "rotate_90" or "rotate_270";
            int tw = rotate ? h : w, th = rotate ? w : h;
            Region(tx, ty, tw, th);
            var target = layers.SingleOrDefault(l => l.Id == targetId) ?? throw new ArgumentException("Unknown target layer.");
            var selection = pixels.Where(p => p.Key.X >= x && p.Key.X < x + w && p.Key.Y >= y && p.Key.Y < y + h).ToArray();
            if (!B("copy")) foreach (var p in selection) pixels.Remove(p.Key);
            var destination = target == layer ? pixels : target.Pixels.ToDictionary(p => (p.X, p.Y), p => p.Color);
            // Snapshot first so overlapping moves and rotations never overwrite their own input.
            for (int yy = ty; yy < ty + th; yy++) for (int xx = tx; xx < tx + tw; xx++) destination.Remove((xx, yy));
            foreach (var p in selection)
            {
                int px = p.Key.X - x, py = p.Key.Y - y;
                var mapped = transform switch
                {
                    "flip_x" => (w - 1 - px, py), "flip_y" => (px, h - 1 - py),
                    "rotate_90" => (h - 1 - py, px), "rotate_180" => (w - 1 - px, h - 1 - py),
                    "rotate_270" => (py, w - 1 - px), _ => (px, py)
                };
                destination[(tx + mapped.Item1, ty + mapped.Item2)] = p.Value;
            }
            if (destination.Count > 20000) throw new ArgumentException("Maximum 20000 painted cells per layer.");
            if (target != layer) target.Pixels = ToPixels(destination);
        }
        else if (action == "pixel_stamp")
        {
            var pattern = op["rows"]?.AsArray().Select(n => n?.GetValue<string>() ?? "").ToArray() ?? throw new ArgumentException("Stamp rows required.");
            var palette = op["palette"]?.AsObject() ?? throw new ArgumentException("Stamp palette required.");
            if (pattern.Length == 0 || pattern[0].Length == 0 || pattern.Any(row => row.Length != pattern[0].Length)) throw new ArgumentException("Stamp rows must form a nonempty rectangle.");
            Region(x, y, pattern[0].Length, pattern.Length);
            var colors = new Dictionary<char, string>();
            foreach (var entry in palette)
            {
                if (entry.Key.Length != 1 || entry.Key[0] is < '!' or > '~' or '.') throw new ArgumentException("Palette keys must be single printable ASCII characters except '.' (transparent).");
                colors[entry.Key[0]] = Normalize(entry.Value?.GetValue<string>() ?? throw new ArgumentException("Palette color required."));
            }
            for (int yy = 0; yy < pattern.Length; yy++) for (int xx = 0; xx < pattern[yy].Length; xx++)
            {
                char key = pattern[yy][xx];
                if (key == '.') { if (B("erase_transparent")) Set(x + xx, y + yy, "none"); }
                else Set(x + xx, y + yy, colors.TryGetValue(key, out var color) ? color : throw new ArgumentException("Missing palette color: " + key));
            }
        }
        else
        {
            string color = Normalize(action == "erase_pixel" ? "none" : S("color", "none"));
            if (action != "erase_pixel" && op["color"] == null) throw new ArgumentException("Pixel color required.");
            if (action == "pixel_brush")
            {
                int size = I("brush_size", 1);
                string brush = S("brush_shape", "square");
                if (size is < 1 or > 32 || brush is not ("square" or "circle")) throw new ArgumentException("Brush: size 1–32, shape square or circle.");
                var points = op["points"]?.AsArray() ?? throw new ArgumentException("Brush points required.");
                if (points.Count is < 1 or > 1024) throw new ArgumentException("Brush requires 1–1024 points.");
                int work = 0;
                void Brush(int px, int py)
                {
                    if ((work += size * size) > 1_000_000) throw new ArgumentException("Brush stroke too large; split it into shorter strokes.");
                    int startX = px - (size - 1) / 2, startY = py - (size - 1) / 2;
                    for (int yy = 0; yy < size; yy++) for (int xx = 0; xx < size; xx++)
                    {
                        int bx = startX + xx, by = startY + yy;
                        if (bx < 0 || by < 0 || bx >= columns || by >= rows) continue;
                        if (brush == "square" || (2 * xx + 1 - size) * (2 * xx + 1 - size) + (2 * yy + 1 - size) * (2 * yy + 1 - size) <= size * size)
                            Set(bx, by, color);
                    }
                }
                (int X, int Y)? previous = null;
                foreach (var point in points)
                {
                    var pair = point?.AsArray();
                    if (pair?.Count != 2) throw new ArgumentException("Each brush point is [x,y].");
                    var current = (X: pair[0]!.GetValue<int>(), Y: pair[1]!.GetValue<int>()); Point(current.X, current.Y);
                    if (previous is { } p) Line(p.X, p.Y, current.X, current.Y, Brush); else Brush(current.X, current.Y);
                    previous = current;
                }
            }
            else if (action is "pixel_rect" or "pixel_ellipse")
            {
                Region(x, y, w, h); bool filled = B("filled", true);
                bool Inside(int px, int py) => px >= 0 && py >= 0 && px < w && py < h && (action == "pixel_rect" ||
                    (long)(2 * px + 1 - w) * (2 * px + 1 - w) * h * h + (long)(2 * py + 1 - h) * (2 * py + 1 - h) * w * w <= (long)w * w * h * h);
                for (int yy = 0; yy < h; yy++) for (int xx = 0; xx < w; xx++)
                    if (Inside(xx, yy) && (filled || !Inside(xx - 1, yy) || !Inside(xx + 1, yy) || !Inside(xx, yy - 1) || !Inside(xx, yy + 1))) Set(x + xx, y + yy, color);
            }
            else if (action == "pixel_line") Line(x, y, I("x2", x), I("y2", y), (px, py) => Set(px, py, color));
            else if (action == "pixel_fill")
            {
                Point(x, y);
                // Test against the original cells: symmetric painting must not change flood boundaries.
                var source = new Dictionary<(int X, int Y), string>(pixels);
                string At(int px, int py) => source.TryGetValue((px, py), out var value) ? Normalize(value) : "none";
                string from = At(x, y);
                if (from != color)
                {
                    var visited = new HashSet<(int X, int Y)>(); var queue = new Queue<(int X, int Y)>(); queue.Enqueue((x, y));
                    int count = 0;
                    while (queue.TryDequeue(out var p))
                    {
                        if (p.X < 0 || p.Y < 0 || p.X >= columns || p.Y >= rows || !visited.Add(p) || At(p.X, p.Y) != from) continue;
                        if (++count > 262144) throw new ArgumentException("Flood fill exceeds 262144 cells.");
                        Set(p.X, p.Y, color);
                        queue.Enqueue((p.X - 1, p.Y)); queue.Enqueue((p.X + 1, p.Y)); queue.Enqueue((p.X, p.Y - 1)); queue.Enqueue((p.X, p.Y + 1));
                    }
                }
            }
            else if (action == "pixel_replace")
            {
                w = I("width", columns - x); h = I("height", rows - y); Region(x, y, w, h);
                string from = Normalize(op["from_color"]?.GetValue<string>() ?? throw new ArgumentException("from_color required."));
                var source = new Dictionary<(int X, int Y), string>(pixels);
                for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++)
                    if ((source.TryGetValue((xx, yy), out var value) ? Normalize(value) : "none") == from) Set(xx, yy, color);
            }
            else if (action is "pixel" or "erase_pixel") Set(x, y, color);
            else throw new ArgumentException("Unknown pixel operation.");
        }
        layer.Pixels = ToPixels(pixels);
    }

    static List<AssetPixel> ToPixels(Dictionary<(int X, int Y), string> pixels) => pixels
        .OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X).Select(p => new AssetPixel { X = p.Key.X, Y = p.Key.Y, Color = p.Value }).ToList();

    public static string Inspect(AssetDocument doc, JsonObject args)
    {
        var frameId = args["frame_id"]?.GetValue<string>();
        var layers = frameId == null ? doc.Layers : doc.Frames.Single(f => f.Id == frameId).Layers;
        var layer = layers.Single(l => l.Id == args["layer_id"]!.GetValue<string>());
        int x = args["x"]?.GetValue<int>() ?? 0, y = args["y"]?.GetValue<int>() ?? 0;
        int w = args["width"]?.GetValue<int>() ?? Math.Min(64, doc.Width / doc.PixelSize - x), h = args["height"]?.GetValue<int>() ?? Math.Min(64, doc.Height / doc.PixelSize - y);
        if (x < 0 || y < 0 || w < 1 || h < 1 || w > 64 || h > 64 || x > doc.Width / doc.PixelSize - w || y > doc.Height / doc.PixelSize - h)
            throw new ArgumentException("Inspect a region of 1–64 cells per side inside the grid.");
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz!#$%&()*+,-/:;<=>?@[]^_{|}~";
        var pixels = layer.Pixels.Where(p => p.X >= x && p.X < x + w && p.Y >= y && p.Y < y + h).ToDictionary(p => (p.X, p.Y), p => p.Color);
        var colors = pixels.Values.Distinct().ToArray();
        if (colors.Length > alphabet.Length) return JsonSerializer.Serialize(new { asset_id = doc.Id, doc.Revision, layer_id = layer.Id, frame_id = frameId, x, y, width = w, height = h, pixels = ToPixels(pixels) }, AssetDocument.Json);
        var palette = colors.Select((color, i) => (color, key: alphabet[i])).ToDictionary(p => p.color, p => p.key);
        var rows = Enumerable.Range(y, h).Select(yy => new string(Enumerable.Range(x, w).Select(xx => pixels.TryGetValue((xx, yy), out var color) ? palette[color] : '.').ToArray())).ToArray();
        return JsonSerializer.Serialize(new { asset_id = doc.Id, doc.Revision, layer_id = layer.Id, frame_id = frameId, x, y, width = w, height = h, rows, palette = palette.ToDictionary(p => p.Value.ToString(), p => p.Key), transparent = ".", coordinates = "logical cells, top-left origin" }, AssetDocument.Json);
    }
}
