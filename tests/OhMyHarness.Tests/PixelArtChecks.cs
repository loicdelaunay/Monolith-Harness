using OhMyHarness.Core;
using SkiaSharp;
using System.Text.Json.Nodes;

static class PixelArtChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        AssetDocument Canvas(int w = 32, int h = 24) => new() { Width = w, Height = h, Mode = "pixel_art" };
        JsonObject Op(string json) { var op = JsonNode.Parse(json)!.AsObject(); op["layer_id"] = "layer-1"; return op; }
        void Edit(AssetDocument doc, string json) { AssetTools.Edit(doc, new JsonArray(Op(json))); doc.Validate(); }
        bool Has(AssetDocument d, int x, int y, string color) => d.Layers[0].Pixels.Any(p => p.X == x && p.Y == y && AssetRenderer.Color(p.Color) == AssetRenderer.Color(color));
        var sprite = Canvas();
        Edit(sprite, """{"action":"pixel_stamp","x":1,"y":1,"rows":["A.","BB",".C"],"palette":{"A":"#FF0000","B":"#00FF00","C":"#0000FF"}}""");
        check(sprite.Layers[0].Pixels.Count == 4 && Has(sprite, 1, 1, "#FF0000") && Has(sprite, 2, 3, "#0000FF"), "Palette stamp paints exact logical cells with transparent gaps");
        Edit(sprite, """{"action":"pixel_transform","x":1,"y":1,"width":2,"height":3,"to_x":5,"to_y":5,"transform":"rotate_90","copy":true}""");
        check(Has(sprite, 7, 5, "#FF0000") && Has(sprite, 6, 5, "#00FF00") && Has(sprite, 6, 6, "#00FF00") && Has(sprite, 5, 6, "#0000FF") && Has(sprite, 1, 1, "#FF0000"), "Rectangular clockwise rotation swaps dimensions and copy preserves source");
        Edit(sprite, """{"action":"pixel_transform","x":1,"y":1,"width":2,"height":3,"to_x":2,"to_y":1}""");
        check(Has(sprite, 2, 1, "#FF0000") && Has(sprite, 3, 3, "#0000FF") && !sprite.Layers[0].Pixels.Any(p => p.X == 1 || p.X == 2 && p.Y == 3), "Overlapping move uses a snapshot and replaces transparent destination cells");
        Edit(sprite, """{"action":"pixel_transform","x":2,"y":1,"width":2,"height":3,"transform":"flip_x"}""");
        check(Has(sprite, 3, 1, "#FF0000") && Has(sprite, 2, 3, "#0000FF"), "Selection flip mirrors integer columns without smoothing");
        var region = JsonNode.Parse(AssetPixelEditor.Inspect(sprite, new() { ["layer_id"] = "layer-1", ["x"] = 2, ["y"] = 1, ["width"] = 2, ["height"] = 3 }))!;
        var replay = Canvas();
        var stamp = new JsonObject { ["action"] = "pixel_stamp", ["layer_id"] = "layer-1", ["x"] = 2, ["y"] = 1, ["rows"] = region["rows"]!.DeepClone(), ["palette"] = region["palette"]!.DeepClone() };
        AssetTools.Edit(replay, new JsonArray(stamp));
        check(Has(replay, 3, 1, "#FF0000") && Has(replay, 2, 3, "#0000FF") && replay.Layers[0].Pixels.Count == 4, "Compact region inspection can be reused directly as a palette stamp");

        var fill = Canvas(9, 9);
        Edit(fill, """{"action":"pixel_rect","x":2,"y":2,"width":5,"height":5,"filled":false,"color":"#FF0000"}""");
        Edit(fill, """{"action":"pixel_fill","x":3,"y":3,"color":"#0000FF"}""");
        check(fill.Layers[0].Pixels.Count == 25 && fill.Layers[0].Pixels.Count(p => AssetRenderer.Color(p.Color) == SKColors.Blue) == 9 && !fill.Layers[0].Pixels.Any(p => p.X < 2), "Four-connected fill stays inside an outlined region");
        Edit(fill, """{"action":"pixel_replace","x":3,"y":3,"width":1,"height":3,"from_color":"#0000ffFF","color":"#00FF0080"}""");
        check(fill.Layers[0].Pixels.Count(p => AssetRenderer.Color(p.Color).Alpha == 128) == 3, "Color replacement respects selection and RGBA equivalence");
        var ellipse = Canvas(10, 8);
        Edit(ellipse, """{"action":"pixel_ellipse","x":1,"y":1,"width":8,"height":6,"filled":false,"color":"#FFAA00"}""");
        check(ellipse.Layers[0].Pixels.Count > 0 && ellipse.Layers[0].Pixels.All(p => Has(ellipse, 9 - p.X, p.Y, p.Color) && Has(ellipse, p.X, 7 - p.Y, p.Color)) && !ellipse.Layers[0].Pixels.Any(p => p.X == 4 && p.Y == 3), "Pixel ellipse outline is symmetric on both axes and has an empty center");
        var brush = Canvas(8, 8);
        Edit(brush, """{"action":"pixel_brush","points":[[0,0],[3,3]],"brush_size":1,"color":"#FFFFFF","symmetry":"x"}""");
        check(Enumerable.Range(0, 4).All(i => Has(brush, i, i, "#FFFFFF") && Has(brush, 7 - i, i, "#FFFFFF")), "Connected brush interpolates a gap-free diagonal and mirrors across canvas center");
        Edit(brush, """{"action":"pixel_brush","points":[[0,0]],"brush_size":3,"brush_shape":"circle","color":"none"}""");
        check(!brush.Layers[0].Pixels.Any(p => p.X == 0 && p.Y == 0), "Brush clips at the canvas edge and supports erasing");

        var tiny = Canvas(16, 16);
        Edit(tiny, """{"action":"pixel","x":3,"y":4,"color":"#FF0000"}""");
        using (var preview = SKBitmap.Decode(AssetRenderer.Preview(tiny, 208, true)))
        {
            check(preview.Width == 208 && Enumerable.Range(39, 13).All(x => Enumerable.Range(52, 13).All(y => preview.GetPixel(x, y) == SKColors.Red)), "Every painted cell occupies exactly 13 by 13 unblended output pixels");
            check(preview.GetPixel(12, 5) != preview.GetPixel(13, 5) && preview.GetPixel(13, 5) == preview.GetPixel(25, 5) && preview.GetPixel(25, 5) != preview.GetPixel(26, 5), "Transparency checker boundaries align with logical pixel boundaries at odd zoom");
        }
        using (var plain = SKBitmap.Decode(AssetRenderer.Preview(tiny, 208, true)))
        using (var grid = SKBitmap.Decode(AssetRenderer.Preview(tiny, 208, true, pixelGrid: true)))
            check(grid.GetPixel(13, 5) != plain.GetPixel(13, 5) && grid.GetPixel(12, 5) == plain.GetPixel(12, 5) && grid.GetPixel(14, 5) == plain.GetPixel(14, 5), "Grid lines occupy a single output pixel at the exact cell boundary");
        foreach (var dpi in new[] { 1d, 1.25, 1.5, 2 })
        {
            var layout = AssetPixelPreview.Fit(tiny, 347, 291, dpi, originX: 0.3, originY: 0.7);
            bool Integer(double v) => Math.Abs(v - Math.Round(v)) < 0.00001;
            check(layout.PixelWidth % 16 == 0 && Integer(layout.Width * dpi) && Integer((layout.Left + 0.3) * dpi) && Integer((layout.Top + 0.7) * dpi), $"Preview uses whole device pixels and a snapped origin at {dpi * 100}% DPI");
        }
        var wide = AssetPixelPreview.Fit(Canvas(64, 16), 400, 100);
        check(wide.PixelWidth == 384 && wide.PixelHeight == 96, "Wide artwork fits using both viewport dimensions");
        var smallViewport = AssetPixelPreview.Fit(Canvas(128, 128), 64, 64);
        check(smallViewport.CellPixels == 1 && smallViewport.Width == 128, "Small viewport preserves native cells for scrolling instead of shrinking them");
        try { AssetRenderer.Export(tiny, "png", scale: 1.5f); throw new Exception("Fractional pixel scale accepted"); }
        catch (ArgumentException) { check(true, "Pixel-art exports reject fractional cell sizes"); }

        var root = Path.Combine(Path.GetTempPath(), "omh-pixel-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new AssetWorkspace(Path.Combine(root, "test.sqlite"), 1);
            var saved = await workspace.CreateAsync("Atomic pixels", 16, 16, "none", 1, "pixel_art", default);
            try
            {
                await workspace.UpdateAsync(saved.Id, saved.Revision, d => AssetTools.Edit(d, new JsonArray(
                    Op("""{"action":"pixel_stamp","x":0,"y":0,"rows":["AZ"],"palette":{"A":"#FF0000"}}"""))), default);
                throw new Exception("Missing palette entry accepted");
            }
            catch (ArgumentException) { }
            check((await workspace.ReadAsync(saved.Id, default)).Layers[0].Pixels.Count == 0, "Invalid bulk drawing leaves the persisted asset unchanged");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
