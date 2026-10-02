namespace MonolithHarness.Core;

public sealed class SubagentRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int ChatId { get; set; }
    public string Name { get; set; } = "";
    public string Task { get; set; } = "";
    public string Status { get; set; } = "running";
    public string Activity { get; set; } = "";
    string transcriptJson = "[]";
    SubagentProgress? progress;
    public string TranscriptJson { get => transcriptJson; set { transcriptJson = value; progress = null; } }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public SubagentProgress Progress => progress ??= SubagentTasks.Progress(TranscriptJson);
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
