using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;
using System.Text;
using System.Text.Json.Nodes;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    static List<Message> OrderContextHistory(IEnumerable<Message> messages)
    {
        var list = messages.ToList();
        var summary = list.LastOrDefault(x => x.Role == "compaction");
        var ordered = new List<Message>();
        if (summary != null) ordered.Add(summary);
        ordered.AddRange(list.Where(x => x.Role != "compaction").OrderBy(x => x.Id));
        return ordered;
    }

    async Task<List<Message>> LoadContextHistoryAsync(ConversationRun run, CancellationToken ct)
    {
        var messages = await run.Db.Messages.Include(x => x.Attachments)
            .Where(x => x.ChatId == run.Chat.Id && x.State == "complete")
            .OrderBy(x => x.Id).ToListAsync(ct);
        return OrderContextHistory(messages);
    }

    static JsonArray ComposeWire(string systemPrompt, IEnumerable<Message> history)
    {
        var wire = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = systemPrompt } };
        ChatEngine.AppendHistoryWithToolImages(wire, history, item =>
        {
            var image = item.Attachments[0];
            var toolName = item.Content.Split('\n')[0];
            return new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = $"[Image issue de l’outil {toolName}]" },
                    new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = $"data:{image.Mime};base64,{Convert.ToBase64String(image.Data)}" } }
                }
            };
        });
        return wire;
    }

    async Task<List<Message>> AutoCompactHistoryAsync(ConversationRun run, List<Message> history, string systemPrompt, JsonArray definitions, string secret, CancellationToken ct, bool force = false)
    {
        history = OrderContextHistory(history);
        using var consumption = TokenConsumption.Activity("compaction");
        var result = await ConversationCompactor.CompactAsync(run, history, systemPrompt, definitions, async (text, instruction, token) =>
        {
            SetRunStatus(run, T("Compaction automatique du contexte…"));
            var completion = await engine.StreamAsync(run.Provider, secret, new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = instruction },
                new JsonObject { ["role"] = "user", ["content"] = text }), [], _ => { }, token,
                retrySettings: FeatureSettings.Read(run.Options.FeaturesJson));
            return completion.Message["content"]?.GetValue<string>() ?? "";
        }, ct, automatic: !force);
        ShowContextUsage(run, result.AfterTokens, estimated: true);
        if (result.Changed) SetRunStatus(run, WorkflowText("Contexte compacté · ", "Context compacted · ") + $"{result.BeforeTokens:N0} → {result.AfterTokens:N0} tokens", StatusKind.Notice);
        else if (result.Reason == "protected") SetRunStatus(run, WorkflowText("Cible de compactage impossible : la demande en cours ou les instructions prennent trop de place.", "Compaction target cannot be met: the current request or instructions are too large."), StatusKind.Notice);
        else if (result.Reason == "no_reduction") SetRunStatus(run, WorkflowText("Résumé sans réduction suffisante : historique conservé.", "Insufficient summary reduction: original history preserved."), StatusKind.Notice);
        return result.History;
    }
}