using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    static readonly TimeSpan ModelRetryGrace = TimeSpan.FromSeconds(4);

    static void CancelModelRetryFeedback(ConversationRun run)
    {
        run.RetryRevision++;
        run.RetryFeedbackActive = false;
    }

    void ShowModelRetry(ConversationRun run, RetryProgress retry, AssistantMessageUi? message, CancellationToken ct)
    {
        run.ContextRequest = null; run.ContextRequestTimer.Stop();
        SetRunStatus(run, T("Le modèle réfléchit…"));
        message?.UpdateContent(T("Le modèle réfléchit…"));
        run.RetryFeedbackActive = true;
        _ = RevealModelRetryAsync(run, retry, message, run.RetryRevision, DateTimeOffset.UtcNow, ct);
    }

    async Task RevealModelRetryAsync(ConversationRun run, RetryProgress retry, AssistantMessageUi? message,
        int revision, DateTimeOffset started, CancellationToken ct)
    {
        try { await Task.Delay(ModelRetryGrace, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        DispatcherQueue.TryEnqueue(() =>
        {
            // Output, a new attempt, cancellation or another status invalidates this notification.
            if (ct.IsCancellationRequested || run.RetryRevision != revision || !run.RetryFeedbackActive ||
                run.StatusMode != StatusKind.Activity || !ReferenceEquals(conversationRuns.GetValueOrDefault(run.Chat.Id), run)) return;
            int remaining = Math.Max(0, (int)Math.Ceiling(retry.DelaySeconds - (DateTimeOffset.UtcNow - started).TotalSeconds));
            var text = remaining > 0 ? (retry with { DelaySeconds = remaining }).Describe(run.Options.Language)
                : run.Options.Language == "en" ? $"Retry {retry.Attempt}/{retry.Maximum}…" : $"Nouvelle tentative {retry.Attempt}/{retry.Maximum}…";
            SetRunStatus(run, text); run.RetryFeedbackActive = true;
            message?.UpdateContent(text);
        });
    }
}
