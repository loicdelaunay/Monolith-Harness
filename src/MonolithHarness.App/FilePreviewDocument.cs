using MonolithHarness.Core;
using SkiaSharp;
using System.Text;

namespace MonolithHarness.App;

/// <summary>Bounded local reads and image decoding, always called on a worker thread.</summary>
internal sealed record FilePreviewDocument(string Name, string? Path, string Mime, byte[] Bytes,
    string? Text, string? EncodingName, byte[]? ImagePng, int Width, int Height, DateTime? ModifiedUtc)
{
    internal const int MaxImageBytes = 32 * 1024 * 1024, MaxTextBytes = 2 * 1024 * 1024;
    internal const int TextPageLength = 40_000, BinaryPageLength = 1024;
    internal static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".ico", ".txt", ".md", ".markdown"];
    internal bool IsImage => ImagePng != null;
    internal bool IsMarkdown => System.IO.Path.GetExtension(Name).Equals(".md", StringComparison.OrdinalIgnoreCase) ||
        System.IO.Path.GetExtension(Name).Equals(".markdown", StringComparison.OrdinalIgnoreCase) || Mime.StartsWith("text/markdown", StringComparison.OrdinalIgnoreCase);
    internal static bool Supports(string name, string? mime = null) => Extensions.Contains(System.IO.Path.GetExtension(name), StringComparer.OrdinalIgnoreCase) ||
        mime?.Split(';')[0].Trim().ToLowerInvariant() is "text/plain" or "text/markdown" or "image/png" or "image/jpeg" or "image/webp" or "image/gif" or "image/bmp" or "image/x-icon" or "image/vnd.microsoft.icon";

    internal static async Task<FilePreviewDocument> ReadAsync(string path, CancellationToken ct, int imageEdge = 2048)
    {
        path = LocalPreview.ValidatePath(path);
        if (!Supports(path)) throw new IOException("Format non compatible avec Preview / Unsupported Preview format.");
        var folder = System.IO.Path.GetDirectoryName(path)!;
        path = LocalPreview.ResolveResource(folder, Uri.EscapeDataString(System.IO.Path.GetFileName(path)));
        if (Directory.Exists(path)) throw new IOException("Choisissez un fichier / Select a file.");
        var text = System.IO.Path.GetExtension(path).ToLowerInvariant() is ".txt" or ".md" or ".markdown";
        var limit = text ? MaxTextBytes : MaxImageBytes;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > limit) throw TooLarge(text);
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > limit) throw TooLarge(text);
            output.Write(buffer, 0, count);
        }
        ct.ThrowIfCancellationRequested();
        var mime = System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        { ".md" or ".markdown" => "text/markdown", ".bmp" => "image/bmp", _ => LocalPreview.Mime(path).Split(';')[0] };
        return FromBytes(System.IO.Path.GetFileName(path), mime, output.ToArray(), ct, path, File.GetLastWriteTimeUtc(path), imageEdge);
    }

    internal static FilePreviewDocument FromBytes(string name, string mime, byte[] bytes, CancellationToken ct,
        string? path = null, DateTime? modifiedUtc = null, int imageEdge = 2048)
    {
        if (!Supports(name, mime)) throw new IOException("Format non compatible avec Preview / Unsupported Preview format.");
        ct.ThrowIfCancellationRequested();
        var text = System.IO.Path.GetExtension(name).ToLowerInvariant() is ".txt" or ".md" or ".markdown" || mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
        if (bytes.Length > (text ? MaxTextBytes : MaxImageBytes)) throw TooLarge(text);
        if (text)
        {
            var (content, encoding) = DecodeText(bytes);
            ct.ThrowIfCancellationRequested();
            return new(name, path, mime, bytes, content, encoding, null, 0, 0, modifiedUtc);
        }
        using var source = new SKMemoryStream(bytes);
        using var codec = SKCodec.Create(source);
        if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || codec.Info.Width > 16384 || codec.Info.Height > 16384 ||
            (long)codec.Info.Width * codec.Info.Height > 40_000_000)
            throw new IOException("Image invalide ou trop grande (40 mégapixels maximum) / Invalid or oversized image (40 megapixels maximum).");
        var width = codec.Info.Width; var height = codec.Info.Height;
        using var original = SKBitmap.Decode(bytes) ?? throw new IOException("Décodage de l’image impossible / Image decoding failed.");
        ct.ThrowIfCancellationRequested();
        var scale = Math.Min(1d, imageEdge / (double)Math.Max(width, height));
        using var resized = scale < 1 ? original.Resize(new SKImageInfo(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale))),
            new SKSamplingOptions(SKFilterMode.Linear)) : null;
        using var image = SKImage.FromBitmap(resized ?? original);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new IOException("Aperçu de l’image indisponible / Image preview unavailable.");
        ct.ThrowIfCancellationRequested();
        return new(name, path, mime, bytes, null, null, png.ToArray(), width, height, modifiedUtc);
    }

    static IOException TooLarge(bool text) => new(text ? "Texte trop volumineux (2 Mo maximum) / Text too large (2 MB maximum)." : "Image trop volumineuse (32 Mo maximum) / Image too large (32 MB maximum).");
    static (string Text, string Encoding) DecodeText(byte[] bytes)
    {
        Encoding encoding; int offset;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 })) { encoding = new UTF32Encoding(false, true, true); offset = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xFE, 0xFF })) { encoding = new UTF32Encoding(true, true, true); offset = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) { encoding = new UTF8Encoding(false, true); offset = 3; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) { encoding = new UnicodeEncoding(false, true, true); offset = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) { encoding = new UnicodeEncoding(true, true, true); offset = 2; }
        else { encoding = new UTF8Encoding(false, true); offset = 0; }
        string content;
        try { content = encoding.GetString(bytes, offset, bytes.Length - offset); }
        catch (DecoderFallbackException) when (offset == 0)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            encoding = Encoding.GetEncoding(1252); content = encoding.GetString(bytes);
        }
        if (content.Contains('\0')) throw new IOException("Le fichier contient des données binaires / The file contains binary data.");
        return (content, encoding.WebName.ToUpperInvariant() + (offset > 0 ? " · BOM" : ""));
    }

    // Keep surrogate pairs and CRLF together; prefer paragraph boundaries for Markdown.
    internal List<(int Start, int Length)> Pages()
    {
        if (Text == null) return Enumerable.Range(0, Math.Max(1, (Bytes.Length + BinaryPageLength - 1) / BinaryPageLength))
            .Select(i => (i * BinaryPageLength, Math.Min(BinaryPageLength, Bytes.Length - i * BinaryPageLength))).ToList();
        var pages = new List<(int, int)>();
        for (int start = 0; start < Text.Length;)
        {
            var end = Math.Min(Text.Length, start + TextPageLength);
            if (end < Text.Length)
            {
                var boundary = Text.LastIndexOf("\n\n", end - 1, Math.Min(end - start, TextPageLength / 4), StringComparison.Ordinal);
                if (boundary >= start) end = boundary + 2;
                else
                {
                    boundary = Text.LastIndexOf('\n', end - 1, Math.Min(end - start, 2000));
                    if (boundary >= start) end = boundary + 1;
                }
                if (char.IsHighSurrogate(Text[end - 1]) || Text[end - 1] == '\r' && Text[end] == '\n') end--;
            }
            pages.Add((start, end - start)); start = end;
        }
        if (pages.Count == 0) pages.Add((0, 0));
        return pages;
    }

    internal string RawPage((int Start, int Length) page)
    {
        if (Text != null) return Text.Substring(page.Start, page.Length);
        var b = new StringBuilder();
        b.AppendLine($"{Name}\nMIME: {Mime}\n{Width} × {Height} px\n{Bytes.Length:N0} octets / bytes\n");
        b.AppendLine("Offset     Hex                                               ASCII");
        for (int i = page.Start; i < page.Start + page.Length; i += 16)
        {
            var row = Bytes.AsSpan(i, Math.Min(16, page.Start + page.Length - i));
            b.Append(i.ToString("X8")).Append("   ");
            foreach (var value in row) b.Append(value.ToString("X2")).Append(' ');
            b.Append(' ', (16 - row.Length) * 3).Append("  ");
            foreach (var value in row) b.Append(value is >= 32 and < 127 ? (char)value : '.');
            b.AppendLine();
        }
        return b.ToString();
    }
}
