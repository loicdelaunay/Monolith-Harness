namespace MonolithHarness.Core;

/// <summary>Existence checks are bounded and never perform disk/network I/O on the UI thread.</summary>
public static class FileCitationVerification
{
    static readonly SemaphoreSlim slots = new(4);
    public static async Task<string?> ResolveAsync(FileCitation citation, Func<string, Task<string?>>? resolve)
    {
        try { return await Task.Run(() => ResolveCoreAsync(citation, resolve)).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { return null; }
    }
    static async Task<string?> ResolveCoreAsync(FileCitation citation, Func<string, Task<string?>>? resolve)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await slots.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            var path = resolve == null ? citation.Path : await resolve(citation.Target).ConfigureAwait(false);
            if (path == null || !Path.IsPathFullyQualified(path)) return null;
            return await Task.Run(() => File.Exists(path) ? Path.GetFullPath(path) : null).ConfigureAwait(false);
        }
        finally { slots.Release(); }
    }
}
