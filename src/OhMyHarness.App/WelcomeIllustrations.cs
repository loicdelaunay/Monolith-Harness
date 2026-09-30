using OhMyHarness.Core;
using SkiaSharp;

namespace OhMyHarness.App;

/// <summary>Small, language-neutral illustrations drawn in the selected theme's palette.</summary>
static class WelcomeIllustrations
{
    public static byte[] Draw(int step, AppearanceTheme theme)
    {
        using var bitmap = new SKBitmap(640, 464);
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale(2);
        canvas.Clear(SKColors.Transparent);
        var background = SKColor.Parse(theme.Background);
        var surface = SKColor.Parse(theme.Surface);
        var accent = SKColor.Parse(theme.Accent);
        var muted = SKColor.Parse(theme.Muted);
        SKColor Fade(SKColor color, byte alpha) => color.WithAlpha(alpha);
        void Box(float x, float y, float w, float h, SKColor color, float radius = 8)
        {
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(x, y, x + w, y + h), radius, radius, paint);
        }
        void Circle(float x, float y, float r, SKColor color)
        {
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawCircle(x, y, r, paint);
        }
        void Line(float x, float y, float x2, float y2, SKColor color, float width = 3)
        {
            using var paint = new SKPaint { Color = color, StrokeWidth = width, StrokeCap = SKStrokeCap.Round, IsAntialias = true };
            canvas.DrawLine(x, y, x2, y2, paint);
        }
        void Check(float x, float y)
        {
            Circle(x, y, 10, accent);
            var check = SKColor.Parse(ThemeContrast.On(theme.Accent));
            Line(x - 4, y, x - 1, y + 3, check, 2); Line(x - 1, y + 3, x + 5, y - 4, check, 2);
        }
        void Copy(float x, float y, float width)
        { Box(x, y, width, 4, Fade(muted, 125), 2); Box(x, y + 10, width * .65f, 4, Fade(muted, 70), 2); }
        Box(0, 0, 320, 232, background, 18);
        Circle(282, 32, 48, Fade(accent, 16)); Circle(35, 206, 62, Fade(accent, 12));
        if (step == 0)
        {
            Box(18, 25, 284, 185, surface, 12);
            for (int i = 0; i < 3; i++) Circle(32 + i * 10, 38, 2.5f, Fade(muted, 130));
            Box(27, 52, 68, 148, background, 6);
            Box(35, 62, 15, 15, accent, 4); Copy(56, 64, 29);
            for (int i = 0; i < 4; i++)
            { Box(33, 91 + i * 21, 56, 16, i == 0 ? Fade(accent, 55) : Fade(muted, 16), 4); }
            Box(112, 64, 123, 34, Fade(accent, 50)); Copy(122, 75, 90);
            Box(134, 110, 151, 40, Fade(muted, 28)); Copy(145, 122, 125);
            Box(112, 161, 173, 33, background); Box(122, 173, 103, 4, Fade(muted, 75), 2);
            Circle(270, 178, 9, accent); Line(270, 182, 270, 174, SKColor.Parse(ThemeContrast.On(theme.Accent)), 2);
        }
        else if (step == 1)
        {
            Line(95, 110, 234, 110, Fade(accent, 160), 4);
            for (int i = 0; i < 3; i++) Circle(130 + i * 24, 110, 4, accent);
            Box(16, 64, 91, 110, surface, 12); Box(27, 77, 69, 67, background, 6);
            Box(40, 92, 43, 8, accent, 4); Copy(38, 113, 46);
            Box(31, 151, 60, 4, Fade(muted, 100), 2);
            Box(216, 43, 88, 143, surface, 12);
            for (int i = 0; i < 3; i++)
            { Box(226, 56 + i * 39, 68, 29, background, 6); Check(240, 70 + i * 39); Box(258, 67 + i * 39, 24, 4, Fade(muted, 140), 2); }
            Circle(157, 175, 23, Fade(accent, 35)); Circle(152, 173, 6, accent);
            Line(158, 173, 172, 173, accent, 4); Line(169, 173, 169, 178, accent, 3);
        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                var x = 22 + i % 2 * 146; var y = 35 + i / 2 * 99;
                Box(x, y, 130, 78, surface, 12); Check(x + 109, y + 18);
                Box(x + 14, y + 14, 26, 26, Fade(accent, 35), 7);
                if (i % 2 == 0)
                { Line(x + 22, y + 23, x + 29, y + 32, accent, 2); Line(x + 29, y + 32, x + 35, y + 22, accent, 2); }
                else
                { Circle(x + 27, y + 27, 7, accent); Circle(x + 27, y + 27, 3, surface); }
                Copy(x + 14, y + 53, 84);
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
