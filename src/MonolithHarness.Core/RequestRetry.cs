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
        HttpRequestException request => request.StatusCode == null || request.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)request.StatusCode >= 500 || IsIncompleteJsonBadRequest(request.StatusCode, request.Message),
        IOException => true,
        OperationCanceledException => true, // The caller's token is checked separately.
        _ => false
    };

    // A gateway can reject a truncated downstream JSON body as HTTP 400 even though
    // our outgoing request is serialized correctly. Retry only this known parse failure;
    // authentication, context limits, model parameters and other 400s remain permanent.
    public static bool IsIncompleteJsonBadRequest(HttpStatusCode? status, string? detail)
    {
        if (status != HttpStatusCode.BadRequest || string.IsNullOrWhiteSpace(detail)) return false;
        bool Has(string text) => detail.Contains(text, StringComparison.OrdinalIgnoreCase);
        var decoding = Has("json_invalid") || Has("JSON decode error") || Has("JSONDecodeError") || Has("JSON parse error");
        var incomplete = Has("Unterminated string") || Has("unexpected end of JSON") || Has("unexpected EOF")
            || Has("EOF while parsing") || Has("end of file while parsing") || Has("unexpected end of data");
        return decoding && incomplete;
    }

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
