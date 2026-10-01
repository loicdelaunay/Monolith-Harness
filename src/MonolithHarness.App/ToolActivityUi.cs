using System.Diagnostics;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    sealed class RunningTool(string name, string arguments)
    {
        public string Name { get; } = name;
        public string Detail { get; } = ToolActivity.Detail(name, arguments);
        public Stopwatch Timer { get; } = Stopwatch.StartNew();
    }
    RunningTool BeginToolActivity(ConversationRun run, string name, string arguments)
    {
        var tool = new RunningTool(name, arguments); run.CurrentTool = tool;
        SetRunStatus(run, ToolActivityText(tool));
        return tool;
    }
    string ToolActivityText(RunningTool tool) => WorkflowText("Outil : ", "Tool: ") + tool.Name +
        (tool.Detail.Length > 0 ? " · " + tool.Detail : "") + " · " + WorkflowText("en cours depuis ", "running for ") + ToolActivity.Duration(tool.Timer.Elapsed.TotalSeconds);
    void EndToolActivity(ConversationRun run, RunningTool tool)
    {
        tool.Timer.Stop();
        if (ReferenceEquals(run.CurrentTool, tool)) run.CurrentTool = null;
    }
}
