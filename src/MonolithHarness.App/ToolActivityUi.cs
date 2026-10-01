using System.Diagnostics;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    sealed class ToolMessageUi(Action<double, string?> progress, Action<string, double, byte[]?, string?, bool> complete)
    {
        public bool IsRunning { get; private set; } = true;
        public void UpdateProgress(double seconds, string? phase = null)
        { if (IsRunning) progress(seconds, phase); }
        public void Complete(string result, double seconds, byte[]? image = null, string? mime = null, bool cancelled = false)
        {
            if (!IsRunning) return;
            IsRunning = false;
            complete(result, seconds, image, mime, cancelled);
        }
    }
    sealed class RunningTool(string name, string arguments)
    {
        public string Name { get; } = name;
        public string Detail { get; } = ToolActivity.Detail(name, arguments);
        public Stopwatch Timer { get; } = Stopwatch.StartNew();
        public required ToolMessageUi Message { get; init; }
        public string? Phase { get; set; }
    }
    RunningTool BeginToolActivity(ConversationRun run, string name, string arguments)
    {
        var tool = new RunningTool(name, arguments)
        { Message = AddToolMessage(name, arguments, target: run.Messages, sourceProject: run.Project) };
        run.CurrentTool = tool;
        SetRunStatus(run, ToolActivityText(tool));
        ScrollRunToBottom(run);
        return tool;
    }
    string ToolActivityText(RunningTool tool) => WorkflowText("Outil : ", "Tool: ") + tool.Name +
        (tool.Detail.Length > 0 ? " · " + tool.Detail : "") +
        (tool.Phase is { Length: > 0 } phase ? " · " + phase : "") +
        " · " + WorkflowText("en cours depuis ", "running for ") + ToolActivity.Duration(tool.Timer.Elapsed.TotalSeconds);
    void EndToolActivity(ConversationRun run, RunningTool tool)
    {
        tool.Timer.Stop();
        if (ReferenceEquals(run.CurrentTool, tool)) run.CurrentTool = null;
    }
}
