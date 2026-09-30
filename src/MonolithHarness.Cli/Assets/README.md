# CLI executable icon

The GUI keeps its original neon M icon. The CLI uses `app.ico`, a separate variant showing the M inside a terminal window with a `>_` prompt. `logo.png` is the source image; `logo-256.png` and `logo-32.png` are resized previews. The ICO includes PNG frames at 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixels with the original alpha channel preserved.

Created with the built-in image generation tool using the GUI's `src/MonolithHarness.App/Assets/logo-256.png` as the edit target. The generated bitmap was copied into this folder, then resized and encoded as ICO without further design edits.

## Generation prompt

Use case: precise-object-edit. Asset type: Windows executable icon for Monolith Harness CLI. Input image is the edit target: the existing neon cyan-magenta M app icon. Preserve the recognizable luminous ribbon M, its cyan and magenta palette and dark glossy material. Change its rounded-square backdrop into a clean, immediately recognizable terminal window icon inspired by a Windows command terminal: charcoal rounded rectangular window, thin silver-gray frame, slim title bar, simple small window controls, and a restrained >_ command prompt motif visible behind or beside the M. Keep the M large and readable, slightly smaller only as needed for a clearly visible terminal frame and >_ symbol. Single centered square icon, front-on, crisp silhouettes readable at 32 pixels; generous coverage, no surrounding white square, no app name or extra text, no watermark. Actual transparent exterior outside the terminal silhouette, preserve translucent glow edges.
