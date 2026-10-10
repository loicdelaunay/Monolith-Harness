using System.Net;

namespace MonolithHarness.Core;

public sealed record RetryProgress(int Attempt, int Maximum, int DelaySeconds)
{
    public string Caption => $"Nouvelle tentative {Attempt}/{Maximum} dans {DelaySeconds} s / Retry {Attempt}/{Maximum} in {DelaySeconds} s";
    public string Describe(string language) => language == "en" ? $"Retry {Attempt}/{Maximum} in {DelaySeconds} s…" : $"Nouvelle tentative {Attempt}/{Maximum} dans {DelaySeconds} s…";
}

public static class RequestRetry
{
    public static bool IsTransient(Exception error) => error switch
    {
        HttpRequestException request => request.StatusCode == null || request.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)request.StatusCode >= 500,
        IOException => true,
        OperationCanceledException => true, // The caller's token is checked separately.
        _ => false
    };

    // Retry one model request, never the surrounding agent turn or its tool effects.
    public static async Task<T> RunAsync<T>(Func<Task<T>> request, IRetrySettings? settings, CancellationToken ct,
        Action<RetryProgress>? progress = null, Func<Exception, bool>? canRetry = null)
    {
        int retries = settings?.RetryEnabled == true ? Math.Clamp(settings.RetryCount, 0, 10) : 0;
        int delay = Math.Clamp(settings?.RetryDelaySeconds ?? 5, 1, 300);
        for (int attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { return await request(); }
            catch (Exception ex) when (!ct.IsCancellationRequested && attempt < retries && (canRetry?.Invoke(ex) ?? IsTransient(ex)))
            {
                progress?.Invoke(new(attempt + 1, retries, delay));
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            }
        }
    }
}
