using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using MonolithHarness.Core;
using System.Text.Json;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    static IEnumerable<FrameworkElement> QuestionVisuals(DependencyObject parent)
    {
        if (parent is FrameworkElement element) yield return element;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in QuestionVisuals(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    async Task SmokeWorkflowQuestions(string output)
    {
        var checks = new List<string>();
        void Check(bool valid, string name) { if (!valid) throw new Exception(name); checks.Add(name); }
        static void Click(Button button) => (new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider
            ?? throw new Exception("Button invocation unavailable")).Invoke();
        Check(chat != null && project != null && provider != null, "Isolated chat, project and provider available.");
        ApplyTheme("fluent-dark-v2");
        AppWindow.Resize(new() { Width = 1600, Height = 1000 });
        shell.IsPaneOpen = false; ClearMessagePanel(messages);
        using var run = new ConversationRun(chat!, project!, provider!, state, "Question UI fixture", []) { Messages = messages };
        AgentQuestion[] questions = [
            new("1. Quel thème préfères-tu ?", [new("Mode Clair", "Thème lumineux, uniquement parmi les choix proposés.", "sun"), new("Mode Sombre", "Idéal pour reposer les yeux, sans champ libre.", "moon"), new("Automatique", "Suit automatiquement les réglages du système.", "system")], false, false),
            new("2. Quel type de projet veux-tu créer ?", [new("Application web", "Une interface accessible dans un navigateur.", "code"), new("Application desktop", "Une application pour ordinateur.", "system")], false, true),
            new("3. Quelles fonctionnalités souhaites-tu inclure ?", [new("Recherche", "Rechercher rapidement dans tous les documents du projet.", "folder"), new("Notifications", "Signaler les événements importants.", "warning"), new("Export", "Enregistrer les résultats dans un fichier.", "check")], true, false),
            new("4. Une remarque ou un détail particulier à ajouter ?", [], false, true)
        ];
        var answerTask = AskWorkflowQuestionsAsync(run, questions, run.Cancellation.Token);
        await Task.Delay(350); root.UpdateLayout();
        var card = run.Messages.Children.OfType<Border>().Last();
        Button Find(string tag) => QuestionVisuals(card).OfType<Button>().Single(b => Equals(AutomationProperties.GetAutomationId(b), tag));
        FrameworkElement Page(int i) => QuestionVisuals(card).Single(e => Equals(AutomationProperties.GetAutomationId(e), "workflow-question-page-" + i));
        TextBox Free(int i) => QuestionVisuals(card).OfType<TextBox>().Single(e => Equals(AutomationProperties.GetAutomationId(e), "workflow-free-" + i));
        RadioButton Radio(int i, string label) => QuestionVisuals(Page(i)).OfType<RadioButton>().Single(b => AutomationProperties.GetName(b) == label);
        void Select(RadioButton radio) => (new RadioButtonAutomationPeer(radio).GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider
            ?? throw new Exception("Radio selection pattern unavailable")).Select();
        void Toggle(CheckBox box) => (new CheckBoxAutomationPeer(box).GetPattern(PatternInterface.Toggle) as IToggleProvider
            ?? throw new Exception("Checkbox toggle pattern unavailable")).Toggle();
        async Task Layout() { await Task.Delay(100); root.UpdateLayout(); scroll.ChangeView(null, 0, null, true); await Task.Delay(60); root.UpdateLayout(); }
        bool Shown(FrameworkElement e) {
            for (DependencyObject? node = e; node != null && node != card; node = VisualTreeHelper.GetParent(node))
                if (node is FrameworkElement f && f.Visibility != Visibility.Visible) return false;
            return e.ActualWidth > 0 && e.ActualHeight > 0;
        }
        var geometry = new List<object>();
        void Alignment(string stage)
        {
            foreach (var tile in QuestionVisuals(card).OfType<Border>().Where(b => Equals(AutomationProperties.GetAutomationId(b), "workflow-choice") && Shown(b)))
            {
                var toggle = QuestionVisuals(tile).OfType<ToggleButton>().Single();
                var center = toggle.TransformToVisual(tile).TransformPoint(new()).Y + toggle.ActualHeight / 2;
                var bullet = QuestionVisuals(toggle).FirstOrDefault(e => e.Name == "OuterEllipse" || e.Name == "NormalRectangle");
                var bulletCenter = bullet == null ? center : bullet.TransformToVisual(tile).TransformPoint(new()).Y + bullet.ActualHeight / 2;
                var layout = ((Grid)tile.Child).Children[1] as FrameworkElement ?? throw new Exception("Choice layout missing");
                var contentCenter = layout.TransformToVisual(tile).TransformPoint(new()).Y + layout.ActualHeight / 2;
                geometry.Add(new { stage, label = AutomationProperties.GetName(toggle), height = tile.ActualHeight, center, bulletCenter, contentCenter, texts = QuestionVisuals(layout).OfType<TextBlock>().Select(t => new { t.Text, t.ActualWidth, t.ActualHeight, t.Opacity, color = (t.Foreground as SolidColorBrush)?.Color.ToString() }).ToArray() });
                Check(Math.Abs(center - tile.ActualHeight / 2) <= 1, stage + ": selector centered · " + AutomationProperties.GetName(toggle));
                Check(Math.Abs(bulletCenter - contentCenter) <= 2, stage + ": bullet and content aligned · " + AutomationProperties.GetName(toggle));
                Check(tile.ActualWidth <= card.ActualWidth + 1, stage + ": tile within card bounds");
            }
            var next = Find("workflow-next");
            var nextRight = next.TransformToVisual(card).TransformPoint(new()).X + next.ActualWidth;
            Check(nextRight <= card.ActualWidth + 1, stage + ": footer actions within card bounds");
            Check(card.ActualHeight <= scroll.ActualHeight + 2, stage + ": whole card fits the chat viewport");
        }
        try
        {
            await Layout();
            ApplyLanguage(); await Layout();
            Check(QuestionVisuals(card).OfType<Button>().Single(b => Equals(AutomationProperties.GetAutomationId(b), "workflow-question-0")).Content is Grid, "Language refresh preserves question headers and their controls.");
            Check(QuestionVisuals(card).OfType<TextBlock>().Single(t => Equals(AutomationProperties.GetAutomationId(t), "workflow-error")).Text.Length == 0, "Language refresh does not display internal test identifiers.");
            Alignment("dark wide");
            Check(!QuestionVisuals(card).OfType<TextBlock>().Any(t => t.Text == questions[0].Question), "Model-supplied numbering is not duplicated in presentation.");
            Click(Find("workflow-next")); await Layout();
            Check(Page(0).Visibility == Visibility.Visible && QuestionVisuals(card).OfType<TextBlock>().Single(t => Equals(AutomationProperties.GetAutomationId(t), "workflow-error")).Text.Length > 0, "Empty answers cannot advance.");
            Select(Radio(0, "Mode Clair")); Select(Radio(0, "Mode Sombre")); await Layout();
            Check(QuestionVisuals(Page(0)).OfType<RadioButton>().Count(b => b.IsChecked == true) == 1, "Native single selection stays exclusive.");
            Check(QuestionVisuals(card).OfType<TextBlock>().Single(t => Equals(AutomationProperties.GetAutomationId(t), "workflow-error")).Text.Length == 0, "Validation errors clear when a valid answer is selected.");
            await Capture(card, Path.Combine(output, "questions-dark-wide.png"));
            await Capture(root, Path.Combine(output, "application-dark-wide.png"));
            Click(Find("workflow-next")); await Layout();
            Select(Radio(1, "Autre réponse")); await Layout();
            Check(Free(1).Visibility == Visibility.Visible, "Custom selection opens its text field.");
            Click(Find("workflow-next")); await Layout();
            Check(Page(1).Visibility == Visibility.Visible, "An empty custom answer cannot advance.");
            Free(1).Text = "Un outil local de classement";
            Select(Radio(1, "Application web")); await Layout();
            Check(Free(1).Visibility == Visibility.Collapsed, "Choosing a predefined option hides the custom field.");
            Select(Radio(1, "Autre réponse"));
            Check(Free(1).Text == "Un outil local de classement", "Returning to custom preserves its draft.");
            Click(Find("workflow-next")); await Layout();
            var boxes = QuestionVisuals(Page(2)).OfType<CheckBox>().ToArray();
            Toggle(boxes[0]); Toggle(boxes[1]); await Layout();
            Check(boxes.Count(b => b.IsChecked == true) == 2, "Multiple selection retains two native checkboxes.");
            Alignment("multiple choice"); await Capture(card, Path.Combine(output, "questions-multiple.png"));
            Click(Find("workflow-back")); await Layout();
            Check(Free(1).Text == "Un outil local de classement", "Previous navigation preserves the answer.");
            Click(Find("workflow-next")); Click(Find("workflow-next")); await Layout();
            Check(QuestionVisuals(Page(3)).OfType<ToggleButton>().Count() == 0, "Pure free text has no redundant toggle.");
            Click(Find("workflow-next")); await Layout(); Check(!answerTask.IsCompleted, "An empty free-text answer cannot submit.");
            Free(3).Text = "Garder le thème courant et une interface compacte.";
            await Layout(); // TextChanged is dispatched asynchronously by Uno.
            Check(QuestionVisuals(card).OfType<TextBlock>().Single(t => Equals(AutomationProperties.GetAutomationId(t), "workflow-error")).Text.Length == 0, "Typing a valid free answer clears its validation error.");
            Click(Find("workflow-collapse")); await Layout();
            Check(!Shown(Free(3)), "Collapsing the whole question card hides its form.");
            Click(Find("workflow-collapse")); await Layout();
            Check(Shown(Free(3)) && Free(3).Text.StartsWith("Garder"), "Expanding restores the form and its draft.");
            await Capture(card, Path.Combine(output, "questions-free-text.png"));
            foreach (var theme in new[] { "fluent-light", "fluent-dark-v2" })
            {
                ApplyTheme(theme); await Layout();
                foreach (int width in new[] { 1200, 720, 420 })
                {
                    AppWindow.Resize(new() { Width = width, Height = 1000 }); await Layout();
                    Click(QuestionVisuals(card).OfType<Button>().Single(b => Equals(AutomationProperties.GetAutomationId(b), "workflow-question-0"))); await Layout();
                    if (Page(0).Visibility != Visibility.Visible) { Click(QuestionVisuals(card).OfType<Button>().Single(b => Equals(AutomationProperties.GetAutomationId(b), "workflow-question-0"))); await Layout(); }
                    Alignment(theme + " " + width);
                    await Capture(card, Path.Combine(output, $"questions-{theme}-{width}.png"));
                    Click(QuestionVisuals(card).OfType<Button>().Single(b => Equals(AutomationProperties.GetAutomationId(b), "workflow-question-3"))); await Layout();
                    Alignment(theme + " free-text " + width);
                    await Capture(card, Path.Combine(output, $"questions-{theme}-free-{width}.png"));
                }
            }
            TextZoom.Set(150); AppWindow.Resize(new() { Width = 720, Height = 1000 }); await Layout();
            Click(QuestionVisuals(card).OfType<Button>().Single(b => Equals(AutomationProperties.GetAutomationId(b), "workflow-question-0"))); await Layout();
            Alignment("150 percent zoom"); await Capture(card, Path.Combine(output, "questions-zoom-150.png"));
            Click(QuestionVisuals(card).OfType<Button>().Single(b => Equals(AutomationProperties.GetAutomationId(b), "workflow-question-2"))); await Layout();
            Alignment("150 percent zoom multiple");
            Click(QuestionVisuals(card).OfType<Button>().Single(b => Equals(AutomationProperties.GetAutomationId(b), "workflow-question-3"))); await Layout();
            Alignment("150 percent zoom final footer"); await Capture(card, Path.Combine(output, "questions-zoom-150-free.png"));
            TextZoom.Set(100); await Layout();
            Click(Find("workflow-next"));
            var answer = await answerTask.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!answer.Cancelled && answer.Answers.Count == 4 && answer.Answers[0].SequenceEqual(["Mode Sombre"]) && answer.Answers[1].SequenceEqual(["Un outil local de classement"]) && answer.Answers[2].Count == 2 && answer.Answers[3].Single().StartsWith("Garder"), "Final submission returns all selected and typed answers.");
            Check(!run.Messages.Children.Contains(card), "Submitting releases the interactive card.");
            using var cancelRun = new ConversationRun(chat!, project!, provider!, state, "Cancel fixture", []) { Messages = messages };
            var cancelled = AskWorkflowQuestionsAsync(cancelRun, [questions[3] with { Question = "Une remarque ?" }], cancelRun.Cancellation.Token);
            await Layout(); var cancelCard = run.Messages.Children.OfType<Border>().Last();
            await Capture(cancelCard, Path.Combine(output, "question-single-free-text.png"));
            Click(QuestionVisuals(cancelCard).OfType<Button>().Single(b => Equals(AutomationProperties.GetAutomationId(b), "workflow-cancel")));
            Check((await cancelled.WaitAsync(TimeSpan.FromSeconds(5))).Cancelled, "Cancellation remains distinct from approval.");
            await File.WriteAllLinesAsync(Path.Combine(output, "smoke-ok.txt"), checks);
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(output, "question-geometry.json"), JsonSerializer.Serialize(geometry, new JsonSerializerOptions { WriteIndented = true }));
            if (!answerTask.IsCompleted) { run.Cancellation.Cancel(); try { await answerTask; } catch (OperationCanceledException) { } }
        }
    }
}
