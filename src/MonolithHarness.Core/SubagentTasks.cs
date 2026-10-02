using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public sealed record SubagentTask(string Content, string Status);
public sealed record SubagentProgress(int Total, int Completed, int Cancelled, string CurrentTask)
{
    public bool HasPlan => Total > 0;
    public double? Percentage => HasPlan ? 100d * Completed / Total : null;
}

public static class SubagentTasks
{
    public const string Instructions = "\nTASK PROGRESS: Before doing the assigned work, use todowrite to announce your own concrete task list (1 to 50 tasks). This list belongs only to you; never replace your parent's list. Set the current task to in_progress, keep completed tasks in the list, and update the list after each task is actually finished. Revise the plan if new work is discovered; mark abandoned tasks cancelled. Never equate model requests or tool calls with completed tasks, and never mark unperformed work completed. Before your final report, reconcile the list with the work actually done and explain any pending or cancelled tasks.";

    public static IReadOnlyList<SubagentTask> Read(string transcript)
    {
        try
        {
            var marker = (JsonNode.Parse(transcript) as JsonArray)?.LastOrDefault(x => x?["role"]?.GetValue<string>() == "tasks");
            if (marker?["content"]?.GetValue<string>() is not { } json || JsonNode.Parse(json) is not JsonArray items) return [];
            return WorkflowTools.ValidateTasks(items).Select(x => new SubagentTask(x!["content"]!.GetValue<string>(), x["status"]!.GetValue<string>())).ToArray();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or ArgumentException) { return []; }
    }

    public static SubagentProgress Progress(string transcript)
    {
        var tasks = Read(transcript);
        return new(tasks.Count, tasks.Count(x => x.Status == "completed"), tasks.Count(x => x.Status == "cancelled"),
            tasks.FirstOrDefault(x => x.Status == "in_progress")?.Content ?? tasks.FirstOrDefault(x => x.Status == "pending")?.Content ?? "");
    }

    public static JsonArray Write(JsonArray transcript, JsonArray tasks)
    {
        var validated = WorkflowTools.ValidateTasks(tasks);
        var marker = transcript.LastOrDefault(x => x?["role"]?.GetValue<string>() == "tasks");
        if (marker != null) marker["content"] = validated.ToJsonString();
        else transcript.Add(new JsonObject { ["role"] = "tasks", ["content"] = validated.ToJsonString() });
        return validated;
    }
}
