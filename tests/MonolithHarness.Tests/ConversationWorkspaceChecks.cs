using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;
using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;

static class ConversationWorkspaceChecks
{
    sealed class Handler(Func<int, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(reply(++Calls));
    }
    static HttpResponseMessage Success() => new(HttpStatusCode.OK) { Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\ndata: [DONE]\n\n") };
    public static async Task Run(Action<bool, string> check)
    {
        var settings = new FeatureSettings { RetryEnabled = true, RetryCount = 2, RetryDelaySeconds = 1 };
        var handler = new Handler(i => i == 1 ? new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") } : Success());
        using (var http = new HttpClient(handler))
        {
            var notices = new List<RetryProgress>(); var timer = Stopwatch.StartNew();
            var wire = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "hello" });
            var response = await new ChatEngine(http).StreamAsync(new() { BaseUrl = "https://example.invalid/v1" }, "test", wire, [], x => { if (x.Retry != null) notices.Add(x.Retry); }, default, retrySettings: settings);
            check(response.Message["content"]!.GetValue<string>() == "ok" && handler.Calls == 2 && wire.Count == 1, "Retry 503 succeeds without duplicating the user input");
            check(notices is [{ Attempt: 1, Maximum: 2, DelaySeconds: 1 }] && timer.Elapsed.TotalSeconds >= .9, "Configured retry count and delay are honored");
        }
        handler = new(_ => new(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") });
        using (var http = new HttpClient(handler))
        {
            try { await new ChatEngine(http).StreamAsync(new(), "test", [], [], _ => { }, default, retrySettings: settings); throw new Exception("Expected 401 failure"); }
            catch (HttpRequestException ex) { check(ex.StatusCode == HttpStatusCode.Unauthorized && handler.Calls == 1, "Authentication errors are not retried"); }
        }
        handler = new(_ => new(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") });
        using (var http = new HttpClient(handler))
        using (var cancellation = new CancellationTokenSource())
        {
            try { await new ChatEngine(http).StreamAsync(new(), "test", [], [], update => { if (update.Retry != null) cancellation.Cancel(); }, cancellation.Token, retrySettings: settings); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { check(handler.Calls == 1, "Stop cancels the retry wait before a second request"); }
        }
        int attempts = 0;
        try { await RequestRetry.RunAsync<int>(() => { attempts++; throw new IOException("disconnected"); }, settings, default); throw new Exception("Expected exhausted retries"); }
        catch (IOException) { check(attempts == 3, "Two retries stop after exactly three attempts"); }
        handler = new(i => i == 1 ? new(HttpStatusCode.OK) { Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n") } : Success());
        using (var http = new HttpClient(handler))
        {
            var result = await new ChatEngine(http).StreamAsync(new(), "test", [], [], _ => { }, default, retrySettings: settings);
            check(handler.Calls == 2 && result.Message["content"]!.GetValue<string>() == "ok", "Interrupted stream is retried without merging partial answers");
        }
        var options = new ConversationAgents { AutomaticCount = false, AutomaticRoles = false, Count = 1, Roles = [new() { Name = "Reviewer", Instruction = "Review only, do not edit." }] };
        settings.AgentPresets.Add(new() { Name = "Review", Mode = "forced", Options = options });
        var restored = FeatureSettings.Read(settings.Json());
        check(restored.AgentPresets.Single().Options.Roles.Single().Name == "Reviewer" && restored.AgentPresets.Single().Mode == "forced", "Agent presets retain mode, count, and roles");
        var database = Path.Combine(Path.GetTempPath(), "omh-conversation-workspace-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            await using var db = new HarnessDb(database); await db.InitializeAsync();
            var owner = new Project { Name = "Workspace check" }; db.Projects.Add(owner); await db.SaveChangesAsync();
            var chat = new Chat { ProjectId = owner.Id, Title = "Test", OrchestrationMode = "forced", AgentOptionsJson = options.Json() }; db.Chats.Add(chat); await db.SaveChangesAsync();
            var stored = await db.Chats.AsNoTracking().SingleAsync(x => x.Id == chat.Id);
            check(stored.OrchestrationMode == "forced" && ConversationAgents.Read(stored.AgentOptionsJson).Roles.Single().Name == "Reviewer" && stored.UpdatedUtc != null, "Conversation agent settings and activity survive database reload");
            using var run = new ConversationSession(stored, owner, new(), new() { EnabledSkills = "" }, "Review this task", [], database);
            check(run.Chat.AgentOptionsJson == stored.AgentOptionsJson, "A running conversation captures its own agent configuration");
            var childPrompts = new List<string>();
            var runtime = new AgentRuntime(run, new CustomSkills(Path.Combine(Path.GetTempPath(), "omh-no-skills-" + Guid.NewGuid().ToString("N"))),
                (wire, _, _) => { childPrompts.Add(wire.Last()!["content"]!.GetValue<string>()); return Task.FromResult(new Completion(new JsonObject { ["role"] = "assistant", ["content"] = "done" }, 2, 1, .1)); },
                (_, _, _) => Task.FromResult(false), _ => Task.CompletedTask);
            await runtime.ForcedAsync(default);
            check(childPrompts.Count == 1 && childPrompts[0].Contains("Review only, do not edit.") && childPrompts[0].Contains("Review this task"), "Forced manual roles are applied to the selected number of children");
            var record = await db.Subagents.AsNoTracking().SingleAsync(x => x.ChatId == stored.Id);
            check(record.Name == "Reviewer" && record.Status == "completed", "Configured child completes and persists its result");
            try { await runtime.CallAsync("delegate_tasks", new JsonObject { ["tasks"] = new JsonArray(new JsonObject { ["name"] = "Reviewer", ["prompt"] = "more" }) }, default); throw new Exception("Expected agent limit"); }
            catch (InvalidOperationException) { check(true, "Agent count limit is enforced across the whole turn"); }
            var input = new Message { ChatId = chat.Id, Role = "user", Content = "hello" }; db.Messages.Add(input); await db.SaveChangesAsync();
            var branch = await ConversationBranches.CreateAsync(database, chat.Id, input.Id, false);
            check(branch.AgentOptionsJson == chat.AgentOptionsJson && branch.OrchestrationMode == "forced", "Forks retain the conversation agent configuration");
            var automatic = new Chat { ProjectId = owner.Id, OrchestrationMode = "forced", AgentOptionsJson = new ConversationAgents { AutomaticCount = false, AutomaticRoles = true, Count = 3 }.Json() };
            db.Chats.Add(automatic); await db.SaveChangesAsync();
            using var plannedRun = new ConversationSession(automatic, owner, new(), new() { EnabledSkills = "" }, "Inspect the project", [], database);
            int plannerCalls = 0, childCalls = 0;
            var plannedRuntime = new AgentRuntime(plannedRun, new CustomSkills(Path.Combine(Path.GetTempPath(), "omh-absent-planner-skills")),
                (wire, _, _) =>
                {
                    string result;
                    if (wire[0]!["content"]!.GetValue<string>().StartsWith("Plan independent", StringComparison.Ordinal))
                    {
                        plannerCalls++;
                        result = "{\"tasks\":[{\"name\":\"Architecture\",\"prompt\":\"Inspect structure\"},{\"name\":\"Review\",\"prompt\":\"Inspect correctness\"},{\"name\":\"Tests\",\"prompt\":\"Inspect coverage\"}]}";
                    }
                    else { Interlocked.Increment(ref childCalls); result = "complete"; }
                    return Task.FromResult(new Completion(new JsonObject { ["role"] = "assistant", ["content"] = result }, 1, 1, .1));
                }, (_, _, _) => Task.FromResult(false), _ => Task.CompletedTask);
            await plannedRuntime.ForcedAsync(default);
            check(plannerCalls == 1 && childCalls == 3 && await db.Subagents.CountAsync(x => x.ChatId == automatic.Id && x.Status == "completed") == 3, "Automatic roles use one planning request and honor an explicit count of three");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(database + suffix)) File.Delete(database + suffix);
        }
    }
}
