using MonolithHarness.Core;
using System.Text.Json.Nodes;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    AgentRuntime CreateAgentRuntime(ConversationRun run, string secret, McpSession mcp) => new(run, new CustomSkills(CustomSkills.DefaultRoot, run.Project.GetSourceFolders(), run.Project.Id),
        async (wire, definitions, ct) =>
        {
            using var consumption = TokenConsumption.Activity("agent");
            if (!run.Provider.IsOpenCode) return await engine.StreamAsync(run.Provider, secret, wire, definitions, _ => { }, ct, run.Options.ThinkingLevel, FeatureSettings.Read(run.Options.FeaturesJson));
            var directory = OpenCodeDirectory(run.Project);
            await EnsureOpenCodeServerAsync(run.Provider, secret, ct, run.Project);
            var session = await openCodeEngine.CreateSessionAsync(run.Provider, secret, directory, "Sous-agent · " + run.Chat.Title, ct);
            return await openCodeEngine.PromptAsync(run.Provider, secret, directory, session, wire.Last()?["content"]?.GetValue<string>() ?? "",
                wire[0]?["content"]?.GetValue<string>() ?? "", [], _ => { }, ct, policy: new("plan", "disabled"), workflow: AgentRuntime.CurrentChildWorkflow ?? run.Workflow?.ForChild(), retrySettings: FeatureSettings.Read(run.Options.FeaturesJson));
        },
        (scope, diff, ct) => RequestAccessAsync(scope, scope.StartsWith("web-http|") ? "Recherche web · Requête HTTP" : scope.StartsWith("memory|") ? "Mémoire / Memory" : "Sous-agent · Patch", diff, scope.StartsWith("web-http|") ? "HTTP .NET" : scope.StartsWith("memory|") ? "Mémoire / Memory" : "Patch des sources", ct),
        text => { SetRunStatus(run, text); return Task.CompletedTask; },
        _ => Task.FromResult(RunSkills(run)), child => { UpdateSubagent(run,child); return Task.CompletedTask; },
        async (target,wire,definitions,update,ct)=> {
            using var consumption = TokenConsumption.Activity("agent");
            var key=KeyVault.Decrypt(target.ProtectedKey);
            if(!target.IsOpenCode)return await engine.StreamAsync(target,key,wire,definitions,update,ct,run.Options.ThinkingLevel,FeatureSettings.Read(run.Options.FeaturesJson));
            var directory=OpenCodeDirectory(run.Project);await EnsureOpenCodeServerAsync(target,key,ct,run.Project);
            var id=await openCodeEngine.CreateSessionAsync(target,key,directory,"Sous-agent · "+run.Chat.Title,ct);
            return await openCodeEngine.PromptAsync(target,key,directory,id,wire.Last()?["content"]?.GetValue<string>()??"",wire[0]?["content"]?.GetValue<string>()??"",[],update,ct,
                authorize:(permission,token) => AuthorizeOpenCodePermissionAsync(target,directory,permission,token),
                policy:new(definitions.Count == 0 ? "plan" : run.Chat.ExecutionMode,"disabled"),workflow:AgentRuntime.CurrentChildWorkflow ?? run.Workflow?.ForChild(),retrySettings:FeatureSettings.Read(run.Options.FeaturesJson));
        }, async (id, ct) => {
            if (!Skills.Enabled(state.EnabledSkills, id)) state.EnabledSkills += "," + id;
            await db.SaveChangesAsync(ct);
        }, async ct => {
            await toolQueue.WaitAsync(ct);
            try
            {
                run.Options.EnabledSkills = RunSkills(run);
                var source = run.Project.GetSourceFolders().Count > 0;
                var enabled = run.Options.EnabledSkills;
                var definitions = ChatEngine.ToolDefinitions(source && SourceTools.CanRead(enabled), BrowserSkillAccess.Enabled(enabled) && Skills.Enabled(enabled,"web"), source && Skills.Enabled(enabled,"write_sources"));
                SourceTools.AddDefinitions(definitions, source, enabled); AddWorkspaceToolDefinitions(definitions, run);
                FeatureSettings.Read(run.Options.FeaturesJson).FilterBrowser(definitions);
                AgentPolicy.Filter(definitions, run.Chat.ExecutionMode); SandboxWorkspace.Filter(definitions, run.Chat.SandboxEnabled);
                if (!run.Chat.SandboxEnabled && !AgentPolicy.ReadOnly(run.Chat.ExecutionMode))
                    foreach (var definition in await mcp.RefreshAsync(ct)) definitions.Add(definition!.DeepClone());
                return definitions;
            }
            finally { toolQueue.Release(); }
        }, async (name,args,ct) => {
            await toolQueue.WaitAsync(ct);
            try
            {
                if (name.StartsWith("mcp_",StringComparison.Ordinal))
                {
                    var result = await mcp.CallAsync(name,args,run.Provider.SupportsImages || VisionBridge.Enabled(RunSkills(run)),ct);
                    return new AgentToolResult(result.Text,result.Image);
                }
                var call = new JsonObject { ["function"] = new JsonObject { ["name"] = name, ["arguments"] = args.ToJsonString() } };
                var text = await RunTool(call,new SourceAccess(run.Project.GetSourceFolders()),run,ct);
                var capture = TakePendingToolScreenshot();
                return new AgentToolResult(text, capture == null ? null : new Attachment { Name = capture.Value.Label, Mime = capture.Value.Mime, Data = capture.Value.Data });
            }
            finally { TakePendingToolScreenshot(); toolQueue.Release(); }
        });
}
