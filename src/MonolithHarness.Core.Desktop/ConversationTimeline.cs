using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public static class ConversationTimeline
{
    const string Key = "_monolith_question_boundary";
    public const string SegmentKey = "_monolith_display_segment";
    public sealed record Boundary(int MessageId, int Text, int Reasoning);
    public static string Capture(ConversationSession run) => run.Provider.IsExternalAgent && run.ExportProgress is { } progress
        ? new JsonObject { [Key] = JsonSerializer.SerializeToNode(new Boundary(progress.MessageId, progress.Update.Text.Length, progress.Update.Reasoning.Length)) }.ToJsonString() : "";
    public static Boundary? Read(Message message)
    {
        try { return message.Role == "interaction" && message.State == "ui" && message.WireJson.Length > 0
            ? JsonNode.Parse(message.WireJson)?[Key]?.Deserialize<Boundary>() : null; }
        catch (JsonException) { return null; }
    }
    public static void RemapBoundary(Message message, IReadOnlyDictionary<int, int> ids)
    {
        if (Read(message) is not { } boundary || !ids.TryGetValue(boundary.MessageId, out var replacement)) return;
        var wire = JsonNode.Parse(message.WireJson)!.AsObject();
        wire[Key] = JsonSerializer.SerializeToNode(boundary with { MessageId = replacement }); message.WireJson = wire.ToJsonString();
    }
    public static List<Message> ForDisplay(List<Message> history)
    {
        var entries = history.Select(message => (Message: message, Boundary: Read(message))).Where(x => x.Boundary != null).ToArray();
        if (entries.Length == 0) return history;
        var parts = new Dictionary<int, Message>();
        var continuations = new Dictionary<int, Message>();
        foreach (var original in history.Where(message => message.Role == "assistant"))
        {
            JsonObject wire;
            try { wire = original.WireJson.Length > 0 ? JsonNode.Parse(original.WireJson) as JsonObject ?? new() : new(); } catch (JsonException) { continue; }
            var reasoning = wire["reasoning_content"]?.ToString() ?? "";
            var boundaries = entries.Where(x => x.Boundary!.MessageId == original.Id && x.Message.Id > original.Id).ToArray();
            if (boundaries.Length == 0) continue;
            int textStart = 0, reasoningStart = 0;
            if (boundaries.Any(x => x.Boundary!.Text < 0 || x.Boundary.Text > original.Content.Length || x.Boundary.Reasoning < 0 || x.Boundary.Reasoning > reasoning.Length)) continue;
            bool monotonic = true;
            foreach (var item in boundaries) { if (item.Boundary!.Text < textStart || item.Boundary.Reasoning < reasoningStart) monotonic = false; textStart = item.Boundary.Text; reasoningStart = item.Boundary.Reasoning; }
            if (!monotonic) continue;
            textStart = reasoningStart = 0;
            Message Slice(int textEnd, int reasoningEnd, bool final)
            {
                var json = final ? (JsonObject)wire.DeepClone() : new JsonObject();
                var content = original.Content[textStart..textEnd];
                json["content"] = content; json["reasoning_content"] = reasoning[reasoningStart..reasoningEnd]; json[SegmentKey] = true;
                var result = new Message { Id = original.Id, ChatId = original.ChatId, Role = "assistant", State = final ? original.State : "ui",
                    Content = content, WireJson = json.ToJsonString(), Seconds = final ? original.Seconds : 0, CompletedUtc = final ? original.CompletedUtc : null,
                    CachedInputTokens = final ? original.CachedInputTokens : null, InputTokens = final ? original.InputTokens : null,
                    OutputTokens = final ? original.OutputTokens : null, Attachments = final ? original.Attachments : [],
                    CompatibilityNotice = final ? original.CompatibilityNotice : "" };
                textStart = textEnd; reasoningStart = reasoningEnd; return result;
            }
            parts[original.Id] = Slice(boundaries[0].Boundary!.Text, boundaries[0].Boundary!.Reasoning, false);
            for (int i = 0; i < boundaries.Length; i++)
            {
                bool final = i == boundaries.Length - 1;
                continuations[boundaries[i].Message.Id] = Slice(final ? original.Content.Length : boundaries[i + 1].Boundary!.Text,
                    final ? reasoning.Length : boundaries[i + 1].Boundary!.Reasoning, final);
            }
        }
        var result = new List<Message>();
        foreach (var message in history)
        {
            result.Add(parts.GetValueOrDefault(message.Id) ?? message);
            if (continuations.TryGetValue(message.Id, out var continuation)) result.Add(continuation);
        }
        return result;
    }
}
