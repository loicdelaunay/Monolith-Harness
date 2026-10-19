using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public sealed record SuggestedAction(string Id, string Kind, string Label, string Description, string Prompt);
public sealed record ActionSuggestion(string Title, string Description, IReadOnlyList<SuggestedAction> Actions, string Selected = "");

/// <summary>Inert, persisted proposals. Only a host user interaction can dispatch an action.</summary>
public static class ActionSuggestions
{
    public const string SkillId = "action_suggestions";
    public const string Tool = "suggest_actions";
    public const string Instructions = "Use suggest_actions, when exposed, to offer 1 to 4 useful next steps as clickable buttons. Available kinds: continue_here (send the supplied prompt in this conversation after the current turn), launch_agent (start one agent in a separate discussion with the same project and restrictions), new_chat (start a dedicated discussion), enqueue (save a prompt in this conversation's queue). Provide a concise title, label, optional description and a self-contained prompt with paths, findings and scope for each action. Include only relevant alternatives. This tool only displays a proposal and returns immediately: nothing has started, no user answer or approval has been granted. Finish your answer and wait for the user's actual click; do not execute the suggested work yourself or poll for a choice. Do not repeatedly propose the same actions. Actions never grant extra tool permissions. If the tool is not exposed by the host, use ordinary text without claiming clickable actions exist.";
    public static void AddDefinition(JsonArray definitions) => definitions.Add(JsonNode.Parse("""
    {"type":"function","function":{"name":"suggest_actions","description":"Display inert action buttons in desktop chat. Returns immediately; only the user's later click starts the selected action. Never treat displaying a proposal as approval or execute its prompt yourself.","parameters":{"type":"object","properties":{"title":{"type":"string","maxLength":160},"description":{"type":"string","maxLength":1000},"actions":{"type":"array","minItems":1,"maxItems":4,"items":{"type":"object","properties":{"kind":{"type":"string","enum":["continue_here","launch_agent","new_chat","enqueue"]},"label":{"type":"string","maxLength":60},"description":{"type":"string","maxLength":400},"prompt":{"type":"string","maxLength":12000}},"required":["kind","label","prompt"],"additionalProperties":false}}},"required":["title","actions"],"additionalProperties":false}}}
    """));
    public static ActionSuggestion Parse(JsonObject args)
    {
        var actions = (args["actions"] as JsonArray ?? throw new ArgumentException("actions requis / actions required"))
            .Select((a, i) => new SuggestedAction((i + 1).ToString(), a?["kind"]?.GetValue<string>() ?? "", a?["label"]?.GetValue<string>()?.Trim() ?? "",
                a?["description"]?.GetValue<string>()?.Trim() ?? "", a?["prompt"]?.GetValue<string>()?.Trim() ?? "")).ToArray();
        var result = new ActionSuggestion(args["title"]?.GetValue<string>()?.Trim() ?? "", args["description"]?.GetValue<string>()?.Trim() ?? "", actions);
        Validate(result); return result;
    }
    public static void Validate(ActionSuggestion value)
    {
        if (value.Title.Length is < 1 or > 160 || value.Description.Length > 1000 || value.Actions.Count is < 1 or > 4 || value.Actions.Sum(a => a.Prompt.Length) > 32000 ||
            value.Actions.Any(a => a.Kind is not ("continue_here" or "launch_agent" or "new_chat" or "enqueue") || string.IsNullOrWhiteSpace(a.Label) || a.Label.Length > 60 || a.Description.Length > 400 || string.IsNullOrWhiteSpace(a.Prompt) || a.Prompt.Length > 12000 || string.IsNullOrWhiteSpace(a.Id)) ||
            value.Actions.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != value.Actions.Count || value.Selected.Length > 0 && value.Selected != "dismissed" && !value.Actions.Any(a => a.Id == value.Selected))
            throw new ArgumentException("Proposition d’actions invalide / Invalid action suggestion.");
    }
    public static string Wire(ActionSuggestion value, int providerId) => new JsonObject { ["action_suggestion"] = JsonSerializer.SerializeToNode(value), ["provider_id"] = providerId }.ToJsonString();
    public static (ActionSuggestion Suggestion, int ProviderId)? Read(Message message)
    {
        if (message.Role != "interaction" || message.State != "ui" || message.WireJson.Length == 0) return null;
        try {
            var wire = JsonNode.Parse(message.WireJson);
            if (wire?["action_suggestion"] is not { } data) return null;
            var value = data.Deserialize<ActionSuggestion>() ?? throw new JsonException(); Validate(value);
            return (value, wire?["provider_id"]?.GetValue<int>() ?? 0);
        } catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or NullReferenceException) { return null; }
    }
    public static string Summary(ActionSuggestion value) => value.Title + (value.Description.Length == 0 ? "" : "\n" + value.Description) + "\n\n" +
        string.Join("\n\n", value.Actions.Select(a => $"- {a.Label} ({a.Kind})" + (a.Description.Length == 0 ? "" : " : " + a.Description) + "\n  " + a.Prompt)) +
        (value.Selected.Length == 0 ? "\n\n[En attente d’un clic / Awaiting user selection]" : value.Selected == "dismissed" ? "\n\n[Ignoré / Dismissed]" : "\n\n[Choisi / Selected: " + value.Actions.Single(a => a.Id == value.Selected).Label + "]");
}
