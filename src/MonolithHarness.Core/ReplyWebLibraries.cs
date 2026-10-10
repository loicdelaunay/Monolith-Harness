namespace MonolithHarness.Core;

/// <summary>Pinned official browser bundles; see WebAssets/manifest.json for integrity and licenses.</summary>
public static class ReplyWebLibraries
{
    public const string TailwindVersion = "4.3.3";
    public const string ChartJsVersion = "4.5.1";
    static readonly Lazy<string> tailwind = new(() => Read("tailwind-browser.js"));
    static readonly Lazy<string> chart = new(() => Read("chart.umd.js"));

    internal static IEnumerable<(string Name, string Script)> Enabled(ReplyRenderOptions options)
    {
        if (options.Tailwind) yield return ("Tailwind CSS " + TailwindVersion, tailwind.Value);
        if (options.ChartJs) yield return ("Chart.js " + ChartJsVersion, chart.Value);
    }
    static string Read(string file)
    {
        using var stream = typeof(ReplyWebLibraries).Assembly.GetManifestResourceStream("MonolithHarness.WebAssets." + file)
            ?? throw new IOException("Bibliothèque locale absente : " + file);
        using var reader = new StreamReader(stream);
        var script = reader.ReadToEnd();
        // Preserve the licensed bundle; escape HTML's script terminator when embedding it inline.
        return script.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);
    }
}
