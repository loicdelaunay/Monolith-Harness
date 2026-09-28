# Asset generator

Enable **Asset generator** in Skills, then ask the AI to draw a logo, icon, diagram, game asset or illustration. In the GUI, open **Tools > Assets** to watch each completed drawing operation. The native preview does not need a browser. The CLI provides the same tools and saved exports.

For example:

> Create a 512 × 512 space badge with a transparent background. Use separate layers for the planet, orbit and title. Capture the canvas, inspect the result and refine the spacing. Export SVG and transparent PNG.

## Canvas and layers

- Draw rectangles (including rounded corners), ellipses, circles, lines, polygons, polylines, arbitrary SVG paths and text.
- Use the full RGB palette, alpha, fill, outline, outline width, opacity and rotation. Colors accept `#RRGGBB`, `#RRGGBBAA`, `none` and `transparent`.
- Name, reorder, hide or delete layers and shapes. Layers have independent opacity. The GUI lists the frontmost layer first and provides visibility and ordering controls.
- Coordinates start at the canvas's top-left. Rectangles and ellipses use their top-left corner; circles use their center; lines use their start; text uses its baseline. Paths and polygon points use absolute canvas coordinates. Rotation is around the shape's `x`/`y` point.
- Use the GUI palette to inspect colors and set or remove the canvas background. Export options are available under **Export**.

The AI updates a structured drawing scene, rather than executing SVG scripts or loading remote SVG resources. Changes become visible when each tool call succeeds. This is an AI drawing workspace; it is not a freehand mouse editor or a general SVG importer.

## Classic and pixel-art modes

Choose **Classic** for smooth SVG shapes, paths and text. Choose **Pixel art** for cell-by-cell drawing with no antialiasing. The GUI canvas dialog offers 16 × 16 (small icons), 32 × 32 (sprites), 64 × 64 (detailed sprites), 128 × 128 and a custom width and height. The AI receives the same recommendations through the skill instructions and `asset_create` tool. For a 32 × 32 sprite, use `{"mode":"pixel_art","width":32,"height":32,"pixel_size":1}`: every canvas pixel is one addressable cell. Custom pixel-art grids can be up to 512 × 512 cells.

In pixel-art mode, `asset_edit` accepts `pixel`, `erase_pixel`, `pixel_rect` and `pixel_line`; their `x`/`y` coordinates refer to individual cells. Vector shapes are rejected in this mode so the artwork cannot acquire smoothed edges accidentally. `color` accepts the full RGB palette with optional alpha. Captures and the GUI preview enlarge pixel art with nearest-neighbor sampling; PNG, WebP, GIF and numbered PNG exports retain hard edges. SVG output requests crisp edges. Use `grid: true` in `asset_capture` to inspect each cell. The grid is an overlay and never appears in exports.

The older `pixel_size` option remains available for block-sized cells: a 64 × 64 canvas with `pixel_size: 8` has an 8 × 8 logical grid. Width and height must be divisible by the cell size. Existing drawings without an explicit mode continue to load; drawings that already use `pixel_size > 1` retain their pixel-art preview.

## Guides and animation

### Fast pixel composition

The AI can use editor-style operations in `asset_edit` instead of specifying every pixel separately. All coordinates and sizes below are integer logical cells. These operations paint cells directly, with no antialiasing or fractional placement.

Set `compact_response: true` on edits to return the new revision and layer/frame counts without repeating the entire pixel scene. The live GUI still receives the full updated drawing. Inspect only the regions needed for the next step.

| Operation | Parameters and behavior |
| --- | --- |
| `pixel_stamp` | Place equal-length character `rows` at `x,y`, using a `palette` of characters to RGBA colors. `.` skips a transparent cell; `erase_transparent: true` clears it instead. |
| `pixel_brush` | Connect `points: [[x,y], ...]` with a continuous stroke. `brush_size` is 1–32; `brush_shape` is `square` or `circle`. `color: "none"` erases. Brush edges clip to the canvas. Even sizes extend one extra cell right/down. |
| `pixel_rect`, `pixel_ellipse` | Draw a filled region of `width,height`, or a one-cell outline with `filled: false`. |
| `pixel_fill` | Fill the four-connected region at `x,y` that matches the original cell's exact RGBA color. Boundaries use the selected layer only. |
| `pixel_replace` | Replace `from_color` with `color` inside a region, or the whole layer if no region is supplied. `none` matches transparent cells. |
| `pixel_transform` | Move a selected rectangle to `to_x,to_y`, or duplicate it with `copy: true`. `transform` supports `identity`, `flip_x`, `flip_y`, and clockwise `rotate_90`, `rotate_180`, `rotate_270`. An optional `target_layer_id` selects another existing layer. |

Drawing operations accept `symmetry: "x"`, `"y"` or `"xy"` to mirror across the whole canvas's center axes (`x` reverses columns, `y` reverses rows). Selection transforms snapshot the source first, so overlapping moves work correctly. Rotations by 90° or 270° swap the region's width and height. Transparent source cells replace destination cells too; selections must fit entirely inside the canvas. All operations respect `frame_id` and atomic revision checking.

For example, the model can draw and duplicate a small sprite in two operations:

```json
{
  "asset_id": "<asset ID>", "expected_revision": 1,
  "operations": [
    {
      "action": "pixel_stamp", "layer_id": "layer-1", "x": 2, "y": 2,
      "rows": [".AAA.", "ABBBA", "AB.BA", ".AAA."],
      "palette": { "A": "#172038", "B": "#4CC9F0" }
    },
    {
      "action": "pixel_transform", "layer_id": "layer-1",
      "x": 2, "y": 2, "width": 5, "height": 4,
      "to_x": 10, "to_y": 2, "transform": "flip_x", "copy": true
    }
  ]
}
```

`asset_inspect` with `layer_id`, optional `frame_id`, and a region up to 64 × 64 returns compact reusable `rows`/`palette` instead of the whole scene. Regions containing too many distinct colors return explicit pixels. `asset_guides` also returns logical grid dimensions, `cell_*` bounds and integer centering shifts. A missing `frame_id` edits or inspects the base scene.

### Pixel-perfect viewing

The transparency checker follows the pixel cells, and the optional grid uses one-pixel lines on their exact boundaries. Choose **Fit / Adapter** or an integer zoom beside the canvas selector; scroll to inspect enlarged artwork. The GUI calculates the preview in physical screen pixels at 100%, 125%, 150% or other display scaling; oversized native artwork scrolls instead of being reduced below one screen pixel per cell. The grid appears from five screen pixels per cell, so it does not obscure smaller previews. Captures report `cell_pixels`, `grid_visible`, logical grid dimensions and output dimensions. Set `max_size` to at least the longest logical grid side. Pixel-art raster exports require a whole number of output pixels per cell; normal vector exports retain fractional scaling.

### Alignment and frames

`asset_guides` returns the exact canvas center, thirds, and the bounds and center offsets of visible shapes and painted pixel regions. Text, SVG paths and rotated shapes have approximate bounds; capture the canvas to verify them visually. Set `guides: true` or `grid: true` in `asset_capture`, or use the **Repères / Grille** controls in the GUI, to overlay visual guides. These overlays never appear in exported files.

Use `frame_add` to copy the base scene or another frame, then target edits with `frame_id`. `frame_duration` sets each frame's duration from 20 to 10,000 ms; `frame_delete` removes one. The GUI includes frame selection, playback and timing controls. Animated exports use the frames in order and loop. With no frames, animation exports contain one still frame.

Export `svg-animated` for a self-contained SVG with discrete timed frames, `gif` for a looping GIF, or `frames` for a ZIP of numbered PNG files and `frames.json` timing metadata. The existing `svg`, `png`, `webp`, `jpeg` and `pdf` formats export the first animation frame when frames exist. GIF is limited to 1024 pixels per side, 4 million pixels across all frames and scale 1; PNG sequences are limited to 64 million pixels total. Use animated SVG for larger artwork.

## Tools

| Tool | Purpose |
| --- | --- |
| `asset_create` | Create a named canvas with dimensions and an optional background. Returns `asset_id`, `revision`, and the initial `layer-1`. |
| `asset_inspect` | Read the scene and current revision, or list this conversation's drawings when no ID is supplied. |
| `asset_guides` | Read exact center and alignment coordinates, with visible shape and pixel-region bounds. |
| `asset_edit` | Apply an atomic batch of layer, shape, ordering or canvas operations. Requires `expected_revision` to protect edits made by the user or another operation. |
| `asset_capture` | Return a PNG image of the artwork, its original dimensions and capture scale. No desktop screenshot is taken. |
| `asset_export` | Save SVG, PNG, WebP, JPEG, PDF, animated SVG, GIF or a PNG-frame ZIP and return the absolute output path. |

Example edit after creating a canvas at revision 1:

```json
{
  "asset_id": "<ID returned by asset_create>",
  "expected_revision": 1,
  "operations": [
    { "action": "layer", "layer_id": "planet", "name": "Planet" },
    {
      "action": "shape",
      "layer_id": "planet",
      "shape": {
        "id": "disc", "type": "circle",
        "x": 256, "y": 256, "radius": 140,
        "fill": "#7C3AED", "stroke": "#4CC9F0", "strokeWidth": 8
      }
    }
  ]
}
```

Supported operations are `canvas`, `layer`, `delete_layer`, `move_layer`, `shape`, `delete_shape`, `move_shape`, `pixel`, `erase_pixel`, `pixel_rect`, `pixel_line`, `pixel_ellipse`, `pixel_brush`, `pixel_fill`, `pixel_replace`, `pixel_stamp`, `pixel_transform`, `frame_add`, `frame_delete` and `frame_duration`. A `shape` upsert replaces the complete shape; omitted fields return to defaults. Move indices are zero-based, from back to front. Re-inspect the scene if an edit reports a revision conflict.

## Capture and export

Canvas captures are sent through the existing image pipeline. A vision-capable model or the configured image bypass skill is needed to interpret them. Captures contain the drawing only, without application chrome or a checkerboard background. The GUI displays a checkerboard to make transparency visible.

SVG preserves vector shapes and named layers. PNG and WebP preserve transparency; JPEG requires an opaque background such as `#FFFFFF`. PDF retains vectors. The `transparent` option removes the canvas background, but does not delete background shapes drawn into layers. Raster exports support scales up to 4×, subject to the pixel limit. The GUI also provides a Save As dialog.

Drawings are persisted beside the database under `assets/chat-<id>`, with exports in that conversation's `exports` subfolder. Reopening the conversation restores its drawings. Asset export creates a new file instead of overwriting previous exports.

## Availability and limits

The skill is opt-in and follows application permissions. Plan mode allows inspection, guides and capture only. It is unavailable in sandbox mode, in subagents, and through the OpenCode-native adapter. The live canvas is implemented in the Uno GUI.

Canvases are limited to 4096 × 4096, with up to 64 layers per frame, 32 frames, 5,000 shapes, 20,000 painted cells and a 2 MB serialized scene. Each edit accepts up to 100 operations. Raster exports are limited to 16 megapixels and 8192 pixels per side. Captures are limited to a 2048-pixel longest side and 8 MB. These limits bound memory use while the UI remains responsive.
