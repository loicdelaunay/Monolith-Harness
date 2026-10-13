using System.Text;

namespace MonolithHarness.Server.Services;

/// <summary>
/// A plain daily log file (<c>server-yyyyMMdd.log</c>, two weeks kept) for the server running as a service, which has no console to read.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    readonly string folder;
    readonly object gate = new();
    string day = "";

    public FileLoggerProvider(string folder)
    {
        this.folder = folder;
        Directory.CreateDirectory(folder);
        Prune();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() { }

    void Write(string line)
    {
        lock (gate)
        {
            try
            {
                var today = DateTime.Now.ToString("yyyyMMdd");
                if (day != today) { day = today; Prune(); }
                File.AppendAllText(Path.Combine(folder, $"server-{today}.log"), line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* logging must never take the server down */ }
        }
    }

    void Prune()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "server-*.log").Where(x => File.GetLastWriteTimeUtc(x) < DateTime.UtcNow.AddDays(-14)))
                File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var text = $"{DateTime.Now:HH:mm:ss.fff} [{logLevel.ToString()[..3].ToUpperInvariant()}] {category}: {formatter(state, exception)}";
            if (exception != null) text += Environment.NewLine + exception;
            owner.Write(text);
        }
    }
}
