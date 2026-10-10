namespace MonolithHarness.Core;

internal sealed record CompletionUsage(long Input, long Output, bool InputEstimated, bool OutputEstimated)
{
    public long? CachedInputTokens { get; init; }
}
