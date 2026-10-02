# Name, logo and Fly themes

Under **Settings → Appearance**, open **Application name and logo**:

- Enter the displayed name, up to 80 characters. An empty field restores Monolith Harness.
- The previous default application name automatically becomes Monolith Harness; other custom names are preserved.
- Choose a PNG, JPEG, WebP, BMP or ICO logo (maximum 10 MB, 4096 × 4096 pixels). A preview lets you check the image before saving.
- **Original logo** restores only the logo. **Reset name and logo** resets both fields.
- Click **Save** to apply. Cancel keeps the previous customization.

The name appears in the sidebar and titles of the main, Settings and Scheduled tasks windows. The logo appears in the sidebar and also serves as the window icon on Windows. The EXE filename and application system identity remain unchanged.

The default Monolith Harness logo uses an illuminated M in cyan, violet and magenta. Matching small PNGs and a Windows ICO are bundled with the GUI and CLI; the terminal draws an M using its theme colors. Custom logos and the light/dark logo settings continue to take precedence. The [generation prompt](logo-monolith-prompt.md) records the image source and instructions.

## Portable storage

`ApplicationName` and `LogoPath` are stored in the JSON settings in `database.sqlite`, without a new migration. A logo already inside the executable directory or a subfolder is referenced by a relative path, such as `my-logo.png` or `images/logo.png`.

An external logo is copied into `workspace/branding/` when saved, using a content-derived filename. Its original file is retained. On Windows, `workspace/branding/window-icon.ico` is an adapted window-icon version. Copying **the complete folder**, including the database and images, preserves these references after relocation. Relative paths resolve from the actual portable directory, even for a self-extracting EXE.

If a logo is missing or unreadable, the interface uses the original logo; Settings lets you choose a new image. Resetting does not delete image files.

## Fly dark and Fly light

Both themes are available under **Appearance → Theme**, in French and English. **Fly dark** combines a near-black background with deep-blue surfaces; **Fly light** combines a white background with neutral surfaces. Airbus blue **#00205B** marks actions and selections in both themes, without a light-blue accent. Text on blue buttons stays white for readability.

The reference color is published in the [official Airbus Brand Centre](https://www.brand.airbus.com/en/asset-library/airbus-logo). These themes are inspired by that palette; the application does not include an Airbus logo.
