# Monolith logo source

Selected by the user on 2026-10-10 from five proposals generated with the built-in `image_gen` tool: **01 — Monolithe**. The selected attached PNG is copied byte-for-byte into `src/MonolithHarness.App/Assets/logo.png`, `src/MonolithHarness.Cli/Assets/logo.png` and `desktop/ui/logo.png`. Its dark square background is part of the selected artwork.

The desktop source remains the selected image. From 1.84.0 the CLI and Android have dedicated color variants; only icon sizes and native containers are exported from each source. `build/update-branding-assets.ps1` exports the small PNGs, multi-size Windows ICOs and macOS ICNS. Android references its own green source through Uno.Resizetizer. The terminal provides a small character approximation because it cannot display a raster app icon in its normal text layout.

## Generation prompt

Use case: logo-brand. Preview-only logo concept for Monolith, a cross-platform AI conversation and agent app for Android and desktop. Create ONE premium professional square logo exploration, centered on a plain very dark charcoal background (#16181d), not a mockup photograph. Vector-friendly flat geometry, confident silhouette, generous negative space and strong readability at app-icon size. Current branding uses an M with cyan, blue, violet and magenta; retain a selective cyan/violet connection with this identity but modernize it with restraint. No glossy bevels, no neon glow, no metallic textures, no robots, no brain, no sparkle clichés, no circuitry, no fine detail, no decorative text. Present one large standalone symbol occupying about 58% of the frame. No caption, no numbers, no wordmark.

Primary concept: Design an original upright architectural monolith symbol. A bold tall rounded slab with a single clean recessed diagonal notch and a slight stepped second plane suggesting an intelligent doorway. Mainly cool off-white with one narrow cyan accent plane. Front-facing graphic construction, no photorealistic 3D. A memorable solid vertical monument with negative-space precision. Avoid an M letterform here; the name's monolith idea is the identity.

## Color variant prompts (1.84.0, 2026-10-10)

Both variants were edited with the built-in image generator using the desktop PNG as the target. Shared constraints: change only the colored right-hand segment; keep the silhouette, white shading, rounded corners, negative space, position, charcoal background, lighting and square composition unchanged; no text or added elements.

- **CLI:** replace cyan/blue with a vivid warm orange gradient, light amber-orange at the top and saturated orange at the bottom (approximately #FFAD42 → #F97316).
- **Android:** replace cyan/blue with a vivid green gradient, mint-lime at the top and emerald at the bottom (approximately #6DEA70 → #22C55E).

Final sources live in each host's `Assets/logo.png`. Small PNGs and native icon containers are deterministic exports of those images, with no color processing in the exporter.
