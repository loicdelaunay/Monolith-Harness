namespace OhMyHarness.Core;

public sealed record AssetPixelPreview(int PixelWidth, int PixelHeight, int CellPixels, float Scale,
    double Width, double Height, double Left, double Top)
{
    // Fit whole device pixels, not DIPs: a 125%/150% display must not resample a cell.
    public static AssetPixelPreview Fit(AssetDocument doc, double width, double height, double density = 1,
        int zoom = 0, double originX = 0, double originY = 0)
    {
        if (!double.IsFinite(density) || density <= 0 || !double.IsFinite(width) || !double.IsFinite(height)) throw new ArgumentException("Invalid preview dimensions.");
        int columns = doc.Width / doc.PixelSize, rows = doc.Height / doc.PixelSize;
        int maxCell = Math.Max(1, Math.Min(32, 4096 / Math.Max(columns, rows)));
        int cell = zoom > 0 ? Math.Clamp(zoom, 1, maxCell) : Math.Clamp((int)Math.Floor(Math.Min(width * density / columns, height * density / rows)), 1, maxCell);
        int pixelWidth = columns * cell, pixelHeight = rows * cell;
        double left = Math.Max(0, (width * density - pixelWidth) / 2), top = Math.Max(0, (height * density - pixelHeight) / 2);
        left = (Math.Ceiling(originX * density + left) - originX * density) / density;
        top = (Math.Ceiling(originY * density + top) - originY * density) / density;
        return new(pixelWidth, pixelHeight, cell, cell / (float)doc.PixelSize, pixelWidth / density, pixelHeight / density, left, top);
    }
}
