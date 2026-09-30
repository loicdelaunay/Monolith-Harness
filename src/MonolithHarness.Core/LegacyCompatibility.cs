namespace MonolithHarness.Core;

// Persisted identifiers and published downloads must remain readable after the rename.
// New projects, resources, keys and file-index writes use MonolithHarness identifiers.
internal static class LegacyCompatibility
{
    internal const string ProductName = "OhMyHarness";
    internal const string FileIndexFormat = "ohmyharness.file-index";
    internal const string ReleaseDownloadPath = "/loicdelaunay/OhMyHarness/releases/download/";
}
