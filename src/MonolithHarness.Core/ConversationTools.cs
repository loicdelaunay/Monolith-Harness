using System.Text.Json.Nodes;
namespace MonolithHarness.Core;

public interface IConversationTools
{
    JsonArray Definitions { get; }
    Task<string> ExecuteAsync(string name,JsonObject arguments,CancellationToken ct);
}
public sealed record ConversationToolActivity(string Name,bool Running,bool Success,string Detail,string CallId="",string Arguments="",string Result="");
public sealed record ConversationRunOptions(string SystemPrompt,IConversationTools? Tools=null,int MaxToolRounds=8,Action<ConversationToolActivity>? ToolProgress=null);
