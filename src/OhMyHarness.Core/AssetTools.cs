using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

public static class AssetTools
{
    public const string PixelInstructions = """
        PIXEL-ART WORKFLOW: Plan a compact palette, silhouette, details and animation on separate named layers. Start at 16x16/32x32; prefer a few bulk operations over hundreds of individual pixels. Set compact_response=true on asset_edit to receive revision/counts instead of repeating every pixel in the response; inspect selected regions when needed.
        Use pixel_stamp with equal-length ASCII rows and a palette mapping each character to a color; '.' skips transparent cells (erase_transparent=true clears them). Example: rows=['.AA.','ABBA','.AA.'], palette={'A':'#172038','B':'#4CC9F0'}. Supply x,y,layer_id. Colors retain full RGB/alpha, and every symbol still represents one exact cell.
        pixel_rect and pixel_ellipse accept filled=false for a 1-cell outline. pixel_brush joins points=[[x,y],...] with an integer path; brush_size=1..32, brush_shape=square/circle, color=none erases. Brush centers must be in bounds; brush edges clip to the canvas. Even brush sizes extend one extra cell right/down. symmetry=x mirrors x across the vertical canvas center, y mirrors across the horizontal center, xy both. Use symmetry on drawing tools, never transforms.
        pixel_fill recolors only the 4-connected region containing x,y on the selected layer, matching exact RGBA. pixel_replace replaces from_color inside x,y,width,height (defaults to whole layer); none matches transparent cells. Layer opacity and other layers do not affect color matching.
        pixel_transform selects x,y,width,height, writes at to_x,to_y (default same origin), and accepts identity,flip_x,flip_y,rotate_90/180/270 clockwise. copy=true duplicates; default moves. 90/270 swap width/height. Transparent cells replace destination cells too. target_layer_id can name another existing layer in the same frame. Out-of-bounds regions fail; operations never silently crop selections. frame_id is optional; omitted edits the base scene, not the first animation frame.
        asset_inspect with layer_id and a region up to 64x64 returns compact reusable rows/palette. asset_guides reports logical grid size and painted bounds for alignment. Inspect before edits, submit expected_revision, then capture at a zoom where cells are visible. The transparency checker is a background aid, not painted content; grid=true shows exact cell boundaries. Captures must be at least the logical grid size. GUI uses integer device pixels at any Windows DPI. Keep every edit inside bounds; 20000 painted cells total across all layers/frames and 2 MB scene limit still apply.
        """;
    public const string SkillId = "asset_generator";
    public const string Instructions = PixelInstructions + " Choose mode=classic for smooth vector shapes or mode=pixel_art for exact hard-edged pixel drawing. For pixel art, use width/height as logical pixel dimensions with pixel_size=1: 16x16 for tiny icons, 32x32 for sprites, 64x64 for detailed sprites, or a custom resolution up to 512x512. Every pixel operation addresses one logical cell; never add vector shapes in pixel_art mode. pixel_size>1 is an optional legacy block size that divides the canvas dimensions. Pixel previews are enlarged with nearest-neighbor scaling and exports have no antialiasing; capture with grid=true to inspect each cell. Inspect with asset_inspect, align with asset_guides (exact center and bounding boxes), edit with asset_edit, visually check with asset_capture, and export with asset_export. GUI shows edits live in Tools > Assets. Drawings persist per conversation and remain editable. Coordinates originate at top-left; pixel operations use logical cells, while shapes in classic mode use canvas pixels. Colors support full RGB/alpha (#RRGGBB or #RRGGBBAA); none erases a pixel. Use frame_add to create animation frames, frame_id to edit one, and frame_duration to set its timing. Add all frames before exporting animated SVG, GIF or numbered PNG frame ZIP. SVG animation uses discrete visibility frames and loops. Capture a specific frame with frame_id and set guides=true or grid=true to reveal non-exported alignment overlays. Use asset_guides for numeric centering, capture after meaningful changes, and expected_revision to avoid overwriting edits. A shape upsert replaces that shape completely. Exports stay in the conversation's managed assets/exports folder. JPEG requires an opaque background; GIF uses scale 1 and has a 4-million-pixel total cap. Never claim to have seen a capture if the model cannot interpret images. Respect tool permissions; unavailable in sandbox and to subagents; Plan permits inspection, guides and capture only.";
    public sealed record Result(string Text, Attachment? Image = null, AssetDocument? Document = null);
    public static bool Handles(string name) => name is "asset_create" or "asset_inspect" or "asset_edit" or "asset_capture" or "asset_export" or "asset_guides";
    public static AssetWorkspace Workspace(ConversationSession run) => new(run.Db.Database.GetDbConnection().DataSource, run.Chat.Id);
    public static void AddDefinitions(JsonArray tools, string skills)
    {
        if (!Skills.Enabled(skills, SkillId)) return;
        JsonObject Text(string description = "") => new() { ["type"] = "string", ["description"] = description };
        JsonObject Number() => new() { ["type"] = "number" };
        JsonObject Integer() => new() { ["type"] = "integer" };
        JsonObject Bool() => new() { ["type"] = "boolean" };
        JsonObject Choice(params string[] values) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()) };
        JsonObject Object(JsonObject properties, params string[] required) => new() { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false,
            ["required"] = new JsonArray(required.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()) };
        void Add(string name, string description, JsonObject properties, params string[] required) => tools.Add(new JsonObject { ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = name, ["description"] = description, ["parameters"] = Object(properties, required) } });
        var shape = Object(new() { ["id"] = Text(), ["type"] = Choice("rect","ellipse","circle","line","polygon","polyline","path","text"),
            ["x"] = Number(), ["y"] = Number(), ["width"] = Number(), ["height"] = Number(), ["radius"] = Number(), ["x2"] = Number(), ["y2"] = Number(),
            ["fill"] = Text("#RRGGBB, #RRGGBBAA, none"), ["stroke"] = Text(), ["strokeWidth"] = Number(), ["opacity"] = Number(), ["rotation"] = Number(),
            ["path"] = Text("SVG d path data only"), ["text"] = Text(), ["fontSize"] = Number(), ["fontFamily"] = Text(), ["bold"] = Bool(),
            ["points"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "array", ["items"] = Number(), ["minItems"] = 2, ["maxItems"] = 2 } } }, "id", "type");
        Add("asset_create", "Create a persistent canvas after approval. mode=classic (default) for vector art; mode=pixel_art for exact cells without antialiasing. Pixel-art presets: 16x16, 32x32, 64x64; custom width/height up to 512x512. Use pixel_size=1 for one canvas pixel per cell; larger cell sizes must divide width/height. Returns asset_id/revision and layer-1.", new() { ["name"] = Text(), ["mode"] = Choice("classic","pixel_art"), ["width"] = Integer(), ["height"] = Integer(), ["background"] = Text(), ["pixel_size"] = Integer() }, "name");
        Add("asset_inspect", "Read the editable scene/revision or list assets. For compact pixel inspection, supply asset_id and layer_id plus optional frame_id,x,y,width,height (region up to 64x64). Returns reusable stamp rows/palette, or explicit pixels for regions with many colors. No network.", new() { ["asset_id"] = Text(), ["layer_id"] = Text(), ["frame_id"] = Text(), ["x"] = Integer(), ["y"] = Integer(), ["width"] = Integer(), ["height"] = Integer() });
        Add("asset_guides", "Read canvas center, thirds and each visible shape/pixel bounding box in exact canvas coordinates, including offsets to center. Use these guides to align artwork before editing. Read-only.", new() { ["asset_id"] = Text(), ["frame_id"] = Text("Optional animation frame ID") }, "asset_id");
        Add("asset_edit", "Apply 1–100 operations atomically; expected_revision must match. Pixel tools work in integer logical cells: pixel/erase_pixel, pixel_line, pixel_rect/pixel_ellipse (filled=false for outline), pixel_brush (connected points), pixel_fill (4-connected flood), pixel_replace (from_color in region), pixel_stamp (rows and palette), pixel_transform (move/copy/flip/rotate region). All produce exact cells, never smooth vector shapes. frame_id targets a frame; omitted means base scene. Limits: 32 frames, 64 layers/frame, 20000 painted cells total, 2 MB scene.", new() {
            ["asset_id"] = Text(), ["expected_revision"] = Integer(), ["compact_response"] = Bool(), ["operations"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 100,
                ["items"] = Object(new() { ["action"] = Choice("layer","delete_layer","move_layer","shape","delete_shape","move_shape","canvas","pixel","pixel_rect","pixel_line","erase_pixel","pixel_ellipse","pixel_brush","pixel_fill","pixel_replace","pixel_stamp","pixel_transform","frame_add","frame_delete","frame_duration"),
                    ["layer_id"] = Text(), ["shape_id"] = Text(), ["frame_id"] = Text(), ["source_frame_id"] = Text(), ["name"] = Text(), ["visible"] = Bool(), ["opacity"] = Number(), ["index"] = Integer(),
                    ["width"] = Integer(), ["height"] = Integer(), ["mode"] = Choice("classic","pixel_art"), ["pixel_size"] = Integer(), ["x"] = Integer(), ["y"] = Integer(), ["x2"] = Integer(), ["y2"] = Integer(), ["color"] = Text(), ["duration_ms"] = Integer(),
                    ["filled"] = Bool(), ["symmetry"] = Choice("none","x","y","xy"), ["brush_size"] = Integer(), ["brush_shape"] = Choice("square","circle"),
                    ["points"] = new JsonObject { ["type"] = "array", ["maxItems"] = 1024, ["items"] = new JsonObject { ["type"] = "array", ["items"] = Integer(), ["minItems"] = 2, ["maxItems"] = 2 } },
                    ["from_color"] = Text("Exact color to replace; none selects transparent cells."),
                    ["rows"] = new JsonObject { ["type"] = "array", ["items"] = Text("One row of palette characters. All rows have equal length; '.' is transparent.") },
                    ["palette"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = Text("Color for a single printable ASCII character except '.'. #RRGGBB/#RRGGBBAA/none.") },
                    ["erase_transparent"] = Bool(), ["to_x"] = Integer(), ["to_y"] = Integer(), ["target_layer_id"] = Text("Existing destination layer, default source layer."),
                    ["transform"] = Choice("identity","flip_x","flip_y","rotate_90","rotate_180","rotate_270"), ["copy"] = Bool(),
                    ["background"] = Text(), ["shape"] = shape }, "action") } }, "asset_id","expected_revision","operations");
        Add("asset_capture", "Capture the asset or a chosen animation frame as PNG for visual inspection. guides=true overlays center crosshair and grid=true overlays pixel cells on the capture only. max_size 64–2048; reports scale and coordinates.", new() { ["asset_id"] = Text(), ["frame_id"] = Text(), ["max_size"] = Integer(), ["guides"] = Bool(), ["grid"] = Bool() }, "asset_id");
        Add("asset_export", "Export SVG, PNG, WebP, JPEG, PDF, animated SVG (svg-animated), GIF, or numbered PNG frames ZIP (frames). Animated formats use frame durations and repeat; transparent=true omits canvas background. GIF scale must be 1 and is limited to 4 million pixels across frames.", new() { ["asset_id"] = Text(), ["format"] = Choice("svg","png","webp","jpeg","pdf","svg-animated","gif","frames"), ["transparent"] = Bool(), ["background"] = Text(), ["scale"] = Number() }, "asset_id","format");
    }
    public static void Edit(AssetDocument doc, JsonArray operations)
    {
        if (operations.Count is < 1 or > 100) throw new ArgumentException("1–100 operations required.");
        foreach (var node in operations)
        {
            var op = node?.AsObject() ?? throw new ArgumentException("Invalid operation.");
            string S(string key) => op[key]?.GetValue<string>() ?? "";
            string action = S("action"), layerId = S("layer_id");
            if (action == "canvas")
            { doc.Name = op["name"]?.GetValue<string>() ?? doc.Name; doc.Width = op["width"]?.GetValue<int>() ?? doc.Width; doc.Height = op["height"]?.GetValue<int>() ?? doc.Height; doc.Mode = op["mode"]?.GetValue<string>() ?? doc.Mode; doc.PixelSize = op["pixel_size"]?.GetValue<int>() ?? doc.PixelSize; doc.Background = op["background"]?.GetValue<string>() ?? doc.Background; continue; }
            if (action == "frame_add")
            {
                var id = S("frame_id"); AssetWorkspace.ValidId(id);
                if (doc.Frames.Any(f => f.Id == id)) throw new ArgumentException("Duplicate frame ID.");
                var source = S("source_frame_id");
                var sourceLayers = source.Length == 0 ? doc.Layers : doc.Frames.Single(f => f.Id == source).Layers;
                doc.Frames.Add(new AssetFrame { Id = id, DurationMs = op["duration_ms"]?.GetValue<int>() ?? 120,
                    Layers = JsonSerializer.Deserialize<List<AssetLayer>>(JsonSerializer.Serialize(sourceLayers, AssetDocument.Json), AssetDocument.Json)! });
                continue;
            }
            if (action == "frame_delete" || action == "frame_duration")
            {
                var frame = doc.Frames.Single(f => f.Id == S("frame_id"));
                if (action == "frame_delete") doc.Frames.Remove(frame); else frame.DurationMs = op["duration_ms"]?.GetValue<int>() ?? frame.DurationMs;
                continue;
            }
            var frameId = S("frame_id");
            var layers = frameId.Length == 0 ? doc.Layers : doc.Frames.Single(f => f.Id == frameId).Layers;
            AssetWorkspace.ValidId(layerId);
            var layer = layers.SingleOrDefault(l => l.Id == layerId);
            if (action == "layer")
            {
                if (layer == null) { layer = new() { Id = layerId, Name = layerId }; layers.Add(layer); }
                layer.Name = op["name"]?.GetValue<string>() ?? layer.Name; layer.Visible = op["visible"]?.GetValue<bool>() ?? layer.Visible; layer.Opacity = op["opacity"]?.GetValue<double>() ?? layer.Opacity; continue;
            }
            if (layer == null) throw new ArgumentException("Unknown layer: " + layerId);
            if (action == "delete_layer") { layers.Remove(layer); continue; }
            if (action == "move_layer") { Move(layers, layer, op["index"]?.GetValue<int>() ?? -1); continue; }
            if (AssetPixelEditor.Handles(action))
            {
                AssetPixelEditor.Apply(doc, layer, layers, op);
                continue;
            }
            if (action == "shape")
            {
                var shape = op["shape"]?.Deserialize<AssetShape>(AssetDocument.Json) ?? throw new ArgumentException("shape required."); shape.Validate();
                var index = layer.Shapes.FindIndex(s => s.Id == shape.Id); if (index < 0) layer.Shapes.Add(shape); else layer.Shapes[index] = shape; continue;
            }
            var item = layer.Shapes.SingleOrDefault(s => s.Id == S("shape_id")) ?? throw new ArgumentException("Unknown shape.");
            if (action == "delete_shape") layer.Shapes.Remove(item);
            else if (action == "move_shape") Move(layer.Shapes, item, op["index"]?.GetValue<int>() ?? -1);
            else throw new ArgumentException("Unknown asset operation.");
        }
    }
    static void Move<T>(List<T> list, T item, int index)
    { if (index < 0 || index >= list.Count) throw new ArgumentException("Invalid layer/shape index."); list.Remove(item); list.Insert(index, item); }
    public static async Task<Result> CallAsync(ConversationSession run, string name, JsonObject args, Func<CancellationToken, Task<string>> skills,
        Func<string,string,string,CancellationToken,Task<bool>> approve, CancellationToken ct)
    {
        async Task Demand()
        { AgentPolicy.Demand(run.Chat.ExecutionMode, name); SandboxWorkspace.Demand(run.Chat.SandboxEnabled,name); if (!Skills.Enabled(await skills(ct),SkillId)) throw new UnauthorizedAccessException("Générateur d’assets désactivé."); ct.ThrowIfCancellationRequested(); }
        await Demand(); var workspace = Workspace(run);
        string id = args["asset_id"]?.GetValue<string>() ?? "";
        if (name == "asset_inspect") return new(id.Length == 0
            ? JsonSerializer.Serialize((await workspace.ListAsync(ct)).Select(d => new { asset_id = d.Id, d.Name, mode = d.IsPixelArt ? "pixel_art" : "classic", d.Width, d.Height, d.PixelSize, d.Revision }),AssetDocument.Json)
            : args["layer_id"] != null ? AssetPixelEditor.Inspect(await workspace.ReadAsync(id,ct), args)
            : JsonSerializer.Serialize(await workspace.ReadAsync(id,ct),AssetDocument.Json));
        if (name == "asset_guides") return new(AssetGuides.Describe(await workspace.ReadAsync(id, ct), args["frame_id"]?.GetValue<string>()));
        if (!Handles(name)) throw new ArgumentException("Unknown asset tool.");
        if (!await approve("assets|" + run.Chat.Id + "|" + name, "Générateur d’assets / Asset generator", args.ToJsonString(), ct)) return new("Access denied; asset unchanged.");
        await Demand();
        AssetDocument doc;
        if (name == "asset_create")
        {
            var pixelSize = args["pixel_size"]?.GetValue<int>() ?? 1;
            var mode = args["mode"]?.GetValue<string>() ?? (pixelSize > 1 ? "pixel_art" : "classic");
            var defaultSize = mode == "pixel_art" ? 32 : 512;
            doc = await workspace.CreateAsync(args["name"]?.GetValue<string>() ?? "Asset", args["width"]?.GetValue<int>() ?? defaultSize, args["height"]?.GetValue<int>() ?? defaultSize, args["background"]?.GetValue<string>() ?? "none", pixelSize, mode, ct);
        }
        else if (name == "asset_edit") doc = await Task.Run(() => workspace.UpdateAsync(id, args["expected_revision"]?.GetValue<int>() ?? -1, d => Edit(d, args["operations"]?.AsArray() ?? []), ct), ct);
        else
        {
            doc = await workspace.ReadAsync(id,ct);
            if (name == "asset_capture")
            {
                int size = args["max_size"]?.GetValue<int>() ?? 1400;
                if (size is < 64 or > 2048) throw new ArgumentException("max_size: 64–2048.");
                if (doc.IsPixelArt && size < Math.Max(doc.Width, doc.Height) / doc.PixelSize)
                    throw new ArgumentException("For pixel-perfect capture, max_size must be at least the longest logical grid side.");
                var frameId = args["frame_id"]?.GetValue<string>();
                int frameIndex = frameId == null ? 0 : doc.Frames.FindIndex(f => f.Id == frameId);
                if (frameIndex < 0) throw new ArgumentException("Unknown frame ID.");
                var bytes = await Task.Run(() => AssetRenderer.Preview(doc,size,false,frameIndex,args["guides"]?.GetValue<bool>() ?? false,args["grid"]?.GetValue<bool>() ?? false),ct);
                if (bytes.Length > 8*1024*1024) throw new IOException("Capture exceeds 8 MB; lower max_size.");
                var scale=AssetRenderer.PreviewScale(doc,size);
                var cellPixels=doc.IsPixelArt?(int)Math.Round(doc.PixelSize*scale):0;
                return new(JsonSerializer.Serialize(new { asset_id=doc.Id, doc.Revision, frame_id=frameId, guides=args["guides"]?.GetValue<bool>()??false, grid=args["grid"]?.GetValue<bool>()??false,
                    doc.Width, doc.Height, scale, cell_pixels=cellPixels, grid_visible=(args["grid"]?.GetValue<bool>()??false)&&doc.IsPixelArt&&cellPixels>=5,
                    grid_columns=doc.Width/doc.PixelSize, grid_rows=doc.Height/doc.PixelSize,
                    capture_width=doc.IsPixelArt?doc.Width/doc.PixelSize*cellPixels:(int)Math.Ceiling(doc.Width*scale),
                    capture_height=doc.IsPixelArt?doc.Height/doc.PixelSize*cellPixels:(int)Math.Ceiling(doc.Height*scale),origin="top-left of artwork, no desktop coordinates" },AssetDocument.Json), new Attachment { Name=doc.Name+".png", Mime="image/png", Data=bytes },doc);
            }
            var path = await workspace.ExportAsync(doc,args["format"]?.GetValue<string>()??"svg",args["transparent"]?.GetValue<bool>()??false,args["background"]?.GetValue<string>(),args["scale"]?.GetValue<float>()??1,ct);
            return new(JsonSerializer.Serialize(new { path, asset_id=doc.Id, doc.Revision },AssetDocument.Json),Document:doc);
        }
        if (name == "asset_edit" && args["compact_response"]?.GetValue<bool>() == true)
            return new(JsonSerializer.Serialize(new { asset_id=doc.Id,doc.Revision,grid_columns=doc.Width/doc.PixelSize,grid_rows=doc.Height/doc.PixelSize,
                layers=doc.Layers.Select(l=>new { l.Id,l.Name,pixels=l.Pixels.Count,shapes=l.Shapes.Count }),
                frames=doc.Frames.Select(f=>new { f.Id,f.DurationMs,pixels=f.Layers.Sum(l=>l.Pixels.Count),shapes=f.Layers.Sum(l=>l.Shapes.Count) }) },AssetDocument.Json),Document:doc);
        return new(JsonSerializer.Serialize(new { asset_id=doc.Id,doc.Revision,document=doc },AssetDocument.Json),Document:doc);
    }
}
