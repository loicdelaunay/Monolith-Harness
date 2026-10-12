using System.Globalization;

namespace MonolithHarness.Core.Remote;

/// <summary>
/// The round avatar of an account: its initials on a color derived from its address (so it is the same everywhere: server pages,
/// Desktop, other devices). Nothing is fetched or stored.
/// </summary>
public static class Avatar
{
    public static string Initials(string? name, string? email)
    {
        var source = !string.IsNullOrWhiteSpace(name) ? name!.Trim() : (email ?? "").Split('@')[0];
        var parts = source.Split([' ', '.', '_', '-', '+'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        var letters = parts.Length == 1 ? parts[0][..Math.Min(2, parts[0].Length)] : parts[0][..1] + parts[1][..1];
        return letters.ToUpperInvariant();
    }

    /// <summary>A stable, readable background for white initials, as RGB.</summary>
    public static (byte R, byte G, byte B) Color(string? key)
    {
        uint hash = 2166136261;
        foreach (var c in (key ?? "").ToLowerInvariant()) { hash ^= c; hash *= 16777619; }
        var hue = hash % 360 / 360.0;
        return FromHsl(hue, 0.52, 0.40);
    }

    public static string Hex(string? key)
    {
        var (r, g, b) = Color(key);
        return string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");
    }

    static (byte, byte, byte) FromHsl(double h, double s, double l)
    {
        double q = l < .5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        static double Channel(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < .5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }
        return ((byte)Math.Round(Channel(p, q, h + 1.0 / 3) * 255), (byte)Math.Round(Channel(p, q, h) * 255), (byte)Math.Round(Channel(p, q, h - 1.0 / 3) * 255));
    }
}
