using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using MonolithHarness.Core;
using System.Text.Json.Nodes;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeReasoningChronology(string output)
    {
        var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        Check(chat != null && project != null && provider != null, "Isolated chat available.");
        var features = FeatureSettings.Read(state.FeaturesJson); features.GuiUpdateMode = AutomaticUpdateMode.Disabled;
        state.FeaturesJson = features.Json(); ConfigureAutomaticUpdates();
        messages = CreateMessagePanel(); scroll.Content = messages;
        ClearMessagePanel(messages);
        var first = AddAssistantMessage("Première étape", "Réflexion initiale", messages);
        MoveToActivity(first);
        var group = first.Group!;
        AddMessage("assistant", "Réponse à une question", [], messages);
        var later = AddAssistantMessage("Étape suivante", "Nouvelle réflexion", messages);
        Check(!ReferenceEquals(group, later.Group), "Visible question answer seals the previous reasoning group.");
        Check(messages.Children.IndexOf(later.Group!.Card) > messages.Children.IndexOf(group.Card), "The new group follows the previous one.");
        Check(!group.Details.Children.Contains(later.ThinkingCard), "New reasoning is not inserted in the old group.");

        ClearMessagePanel(messages);
        var external = new Provider { Id = provider!.Id, Kind = "opencode", Name = "Offline chronology fixture", Model = "fixture/model" };
        using var run = new ConversationRun(chat!, project!, external, state, "Offline chronology fixture", []) { Messages = messages };
        var active = new Message { ChatId = chat!.Id, Role = "assistant", State = "interrupted", Content = "Avant la question." };
        run.Db.Messages.Add(active); await run.Db.SaveChangesAsync();
        var streamed = AddAssistantMessage("Avant la question.", "Réflexion avant.", messages);
        run.ExportProgress = new(active.Id, new("Avant la question.", "Réflexion avant.", null, null, 1));
        var oldGroup = streamed.Group!;
        var task = AskWorkflowQuestionsAsync(run, [new AgentQuestion("Quel détail ajouter ?", [], false, true)], run.Cancellation.Token);
        await Task.Delay(120); root.UpdateLayout();
        var questionHost = MessageHost(messages);
        var question = questionHost.Children.OfType<Border>().Last();
        Check(question.IsLoaded && question.ActualHeight > 0, "The question card is mounted and measured in the visible chat.");
        var input = QuestionVisuals(question).OfType<TextBox>().Single(item => AutomationProperties.GetAutomationId(item) == "workflow-free-0");
        input.Text = "Continuer ici";
        var submit = QuestionVisuals(question).OfType<Button>().Single(item => AutomationProperties.GetAutomationId(item) == "workflow-next");
        (new ButtonAutomationPeer(submit).GetPattern(PatternInterface.Invoke) as IInvokeProvider ?? throw new InvalidOperationException("No button provider")).Invoke();
        var answer = await task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!answer.Cancelled && answer.Answers.Single().Single() == "Continuer ici", "Question answer is accepted in the real application window.");
        Check(streamed.Continuation != null, "An external continuous response starts a new display segment after the answer.");
        streamed.UpdateThinking("Réflexion avant.Réflexion après.");
        streamed.UpdateContent("Avant la question.Après la réponse.");
        var next = streamed.Continuation!;
        Check(next.CurrentText == "Après la réponse.", "The continued response displays only its new text.");
        Check(next.ThinkingBody.Text == "Réflexion après.", "The new group displays only its new reasoning.");
        Check(streamed.ThinkingBody.Text == "Réflexion avant.", "The earlier reasoning stays unchanged.");
        Check(questionHost.Children.IndexOf(next.Group!.Card) > questionHost.Children.IndexOf(oldGroup.Card), "Continued reasoning is below the old group.");
        var interaction = await run.Db.Messages.AsNoTracking().Where(item => item.ChatId == chat.Id && item.Role == "interaction").OrderByDescending(item => item.Id).FirstAsync();
        Check(ConversationTimeline.Read(interaction) is { MessageId: var id, Text: 18, Reasoning: 16 } && id == active.Id,
            "Question boundary is persisted with cumulative text and reasoning offsets.");
        active.Content = "Avant la question.Après la réponse."; active.State = "complete";
        active.WireJson = new JsonObject { ["role"] = "assistant", ["content"] = active.Content, ["reasoning_content"] = "Réflexion avant.Réflexion après." }.ToJsonString();
        await run.Db.SaveChangesAsync();
        var persisted = await run.Db.Messages.AsNoTracking().Where(item => item.ChatId == chat.Id).OrderBy(item => item.Id).ToListAsync();
        var timeline = ConversationTimeline.ForDisplay(persisted);
        Check(timeline.Count == 3 && timeline[0].Content == "Avant la question." && timeline[1].Role == "interaction" && timeline[2].Content == "Après la réponse.",
            "Reloaded history preserves before-question, answer, then continued-response order.");
        var reloaded = CreateMessagePanel(); RenderHistory(persisted, reloaded, project);
        scroll.Content = reloaded;
        await Task.Delay(150); root.UpdateLayout();
        Check(reloaded.Children.OfType<Border>().Count() >= 3, "The reloaded history renders its separate chronological blocks.");
        Check(reloaded.ActualHeight > 0 && reloaded.IsLoaded, "Reloaded chronological blocks are mounted in the visible chat.");
        await Capture(root, Path.Combine(output, "reasoning-after-question.png"));
        var fork = await ConversationBranches.CreateAsync(run.Db.FilePath, chat.Id, active.Id, false);
        await using var copiedDb = new HarnessDb(run.Db.FilePath);
        var forkHistory = await copiedDb.Messages.AsNoTracking().Where(item => item.ChatId == fork.Id).OrderBy(item => item.Id).ToListAsync();
        var forkTimeline = ConversationTimeline.ForDisplay(forkHistory);
        Check(forkTimeline.Count == 3 && forkTimeline[1].Role == "interaction" && forkTimeline[2].Content == "Après la réponse.", "Forks retain questions completed inside the selected assistant response.");
        Check(ConversationTimeline.Read(forkHistory.Single(item => item.Role == "interaction"))!.MessageId == forkHistory.First().Id, "Copied question boundaries point to the copied assistant message.");
        await File.WriteAllLinesAsync(Path.Combine(output, "smoke-ok.txt"), checks);
    }
}
