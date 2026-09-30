using OhMyHarness.Core;
using Microsoft.EntityFrameworkCore;

namespace OhMyHarness.Core.Hosting;

public sealed partial class HarnessService
{
    AgentRuntime CreateAgentRuntime(ConversationSession run, string secret, McpSession mcp) => new(run, new CustomSkills(CustomSkills.DefaultRoot, run.Project.GetSourceFolders(), run.Project.Id),
        async (wire, definitions, ct) =>
        {
            using var consumption = TokenConsumption.Activity("agent");
            if (!run.Provider.IsOpenCode) return await new ChatEngine(http).StreamAsync(run.Provider, secret, wire, definitions, _ => { }, ct, run.Options.ThinkingLevel, FeatureSettings.Read(run.Options.FeaturesJson));
            var directory = OpenCodeDirectory(run.Project);
            await EnsureOpenCode(run.Provider, secret, directory, ct);
            var engine = new OpenCodeEngine(http);
            var session = await engine.CreateSessionAsync(run.Provider, secret, directory, "Sous-agent · " + run.Chat.Title, ct);
            return await engine.PromptAsync(run.Provider, secret, directory, session, wire.Last()?["content"]?.GetValue<string>() ?? "",
                wire[0]?["content"]?.GetValue<string>() ?? "", [], _ => { }, ct, policy: new("plan", "disabled"), workflow: run.Workflow?.ForChild(), retrySettings: FeatureSettings.Read(run.Options.FeaturesJson));
        },
        (scope, diff, ct) => Approve(scope, run.Chat.Title + (scope.StartsWith("web-http|") ? " · HTTP .NET" : scope.StartsWith("memory|") ? " · Mémoire / Memory" : " · Sous-agent · Patch"), diff, ct),
        text => emit(new { @event = "status", chatId = run.Chat.Id, text }),
        async ct => { await using var db = Db(); return await db.States.Select(x => x.EnabledSkills).SingleAsync(ct); },
        child => emit(new { @event="subagent", chatId=run.Chat.Id, child }),
        async (target,wire,definitions,update,ct)=> {
            using var consumption = TokenConsumption.Activity("agent");
            var key=await Decrypt(target.ProtectedKey,ct);
            if(!target.IsOpenCode)return await new ChatEngine(http).StreamAsync(target,key,wire,definitions,update,ct,run.Options.ThinkingLevel,FeatureSettings.Read(run.Options.FeaturesJson));
            var directory=OpenCodeDirectory(run.Project);await EnsureOpenCode(target,key,directory,ct);
            var engine=new OpenCodeEngine(http);var id=await engine.CreateSessionAsync(target,key,directory,"Sous-agent · "+run.Chat.Title,ct);
            return await engine.PromptAsync(target,key,directory,id,wire.Last()?["content"]?.GetValue<string>()??"",wire[0]?["content"]?.GetValue<string>()??"",[],update,ct,
                authorize: async (permission,token) => await Approve($"opencode|{target.Id}|{directory}|{permission.Action}|{string.Join('|',permission.Resources)}",run.Chat.Title + " · Sous-agent · " + permission.Action,string.Join('\n',permission.Resources) + "\n" + permission.Details,token) ? "once" : "reject",
                policy:new(definitions.Count == 0 ? "plan" : run.Chat.ExecutionMode,"disabled"),workflow:run.Workflow?.ForChild(),retrySettings:FeatureSettings.Read(run.Options.FeaturesJson));
        }, async (id, ct) => {
            await using var db = Db();
            var state = await db.States.SingleAsync(ct);
            if (!Skills.Enabled(state.EnabledSkills, id)) state.EnabledSkills += "," + id;
            await db.SaveChangesAsync(ct);
        }, async ct => {
            await tools.WaitAsync(ct);
            try
            {
                await using var db = Db(); run.Options.EnabledSkills = await db.States.Select(x => x.EnabledSkills).SingleAsync(ct);
                var definitions = Definitions(run);
                AgentPolicy.Filter(definitions,run.Chat.ExecutionMode); SandboxWorkspace.Filter(definitions,run.Chat.SandboxEnabled);
                if (!run.Chat.SandboxEnabled && !AgentPolicy.ReadOnly(run.Chat.ExecutionMode))
                    foreach (var definition in await mcp.RefreshAsync(ct)) definitions.Add(definition!.DeepClone());
                return definitions;
            }
            finally { tools.Release(); }
        }, async (name,args,ct) => {
            await tools.WaitAsync(ct);
            try
            {
                if (name.StartsWith("mcp_",StringComparison.Ordinal))
                {
                    var mcpResult = await mcp.CallAsync(name,args,run.Provider.SupportsImages || VisionBridge.Enabled(run.Options.EnabledSkills),ct);
                    return new AgentToolResult(mcpResult.Text,mcpResult.Image);
                }
                var result = await Tool(run,name,args,ct);
                return new AgentToolResult(result.Text,result.Image);
            }
            finally { tools.Release(); }
        });
}
