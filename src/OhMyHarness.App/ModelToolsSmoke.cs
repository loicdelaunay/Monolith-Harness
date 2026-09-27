using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeModelTools(string output)
    {
        var fixture = new Provider { Name = "Modèle de démonstration", Model = "fixture-tools", BaseUrl = "https://fixture.invalid/v1" };
        db.Providers.Add(fixture); await db.SaveChangesAsync();
        provider = fixture; PopulateModelSelector();
        var originalProvider = provider; var originalModel = provider.Model;
        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            yield return parent;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }
        var menu = Descendants(root).OfType<DropDownButton>().Single(b => b.Content?.ToString() == "Outils");
        if (menu.Flyout is not MenuFlyout flyout || flyout.Items.Count != 3) throw new Exception("Dedicated tools menu is missing.");
        await Capture(root, Path.Combine(output, "tools-menu-location.png"));
        foreach (var kind in Enum.GetValues<ModelToolKind>())
        {
            await ShowDedicatedTool(kind);
            var window = dedicatedTools[kind];
            await ShowDedicatedTool(kind);
            if (!ReferenceEquals(window, dedicatedTools[kind])) throw new Exception("Reopening a tool lost its draft.");
            await window.ExerciseSmoke(kind);
            await Task.Delay(180);
            await Capture(window.Panel, Path.Combine(output, kind.ToString().ToLowerInvariant() + "-dark.png"));
            FluentDesign.SetTheme("fluent-light"); window.Panel.RequestedTheme = ElementTheme.Light;
            await Task.Delay(160);
            await Capture(window.Panel, Path.Combine(output, kind.ToString().ToLowerInvariant() + "-light.png"));
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 720, Height = 920 });
            await Task.Delay(160);
            await Capture(window.Panel, Path.Combine(output, kind.ToString().ToLowerInvariant() + "-narrow.png"));
            window.Close(); FluentDesign.SetTheme("fluent-dark");
        }
        if (!ReferenceEquals(provider, originalProvider) || provider.Model != originalModel) throw new Exception("Tools changed the chat's selected model.");
        await ShowDedicatedTool(ModelToolKind.Translator);
        var closing = dedicatedTools[ModelToolKind.Translator];
        await closing.CheckCloseCancellation();
        if (!closing.ClosedForTools || dedicatedTools.Count != 0) throw new Exception("Tool windows were not cleaned up on close.");
        File.WriteAllText(Path.Combine(output, "smoke-ok.txt"), "Dedicated tool menu; independent windows and drafts; translation and swapping; proofreader apply/ignore/manual-edit invalidation/rewrite; 7 benchmark cases; error/stop/close; themes and responsive layouts passed with a simulated model.");
    }
}

internal sealed partial class ModelToolsWindow
{
    const string ProofFixture = """
        {"edits":[
          {"start":0,"length":4,"original":"salu","replacement":"Salut,","reason":"La salutation prend un t et une majuscule."},
          {"start":13,"length":6,"original":"est ce","replacement":"est-ce","reason":"L’inversion interrogative prend un trait d’union."},
          {"start":27,"length":2,"original":"va","replacement":"vas ?","reason":"À la deuxième personne : tu vas. Ajoutez le point d’interrogation."}]}
        """;

    internal async Task ExerciseSmoke(ModelToolKind kind)
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        SmokeRequest = async (_, request, update, ct) =>
        {
            await Task.Delay(50, ct);
            string text;
            var benchmark = ModelBenchmark.Cases.FirstOrDefault(c => c.Prompt == request.Input);
            if (benchmark != null)
            {
                if (benchmark.ExpectedJson == null) text = string.Join(" ", Enumerable.Repeat("Rainwater flows from the roof through clean gutters into a storage tank.", 25));
                else
                {
                    var answer = ModelUtilityPrompts.ParseObject(benchmark.ExpectedJson);
                    if (benchmark.AcceptedPatches != null) answer["patch"] = benchmark.AcceptedPatches[0];
                    text = answer.ToJsonString();
                }
            }
            else if (request.System.StartsWith("Proofread")) text = ProofFixture;
            else
            {
                var segments = ModelUtilityPrompts.ParseObject(request.Input)["segments"]!.AsArray();
                var value = request.System.StartsWith("Rephrase") ? "Salut, comment vas-tu ?" : "Hi, how are you?\n\nI hope you're having a great day.";
                foreach (var segment in segments) segment!["text"] = value;
                text = new System.Text.Json.Nodes.JsonObject { ["segments"] = segments.DeepClone() }.ToJsonString();
            }
            update(new(text, "", 84, 27, .8));
            return new(text, 84, 27, false, false, 1.2, .25);
        };
        await Task.Delay(120);
        if (kind == ModelToolKind.Translator)
        {
            Check(!run.IsEnabled, "Blank translation is enabled.");
            translationInput.Text = "Salut, comment ça va ?\n\nJ’espère que tu passes une bonne journée.";
            await Translate(); await Task.Delay(50);
            Check(translationOutput.Text.StartsWith("Hi,") && run.IsEnabled && usage.Visibility == Visibility.Collapsed, "Translation did not complete.");
            var original = translationInput.Text;
            translationInput.Text += "!";
            await Task.Delay(60);
            Check(translationOutput.Text.Length == 0, "Translation remained stale after input edit.");
            translationInput.Text = original; await Translate();
            await SwapTranslation(); await Task.Delay(60);
            Check(translationInput.Text.StartsWith("Hi,") && translationOutput.Text == original && sourceLanguage.SelectedIndex == 2 && targetLanguage.SelectedIndex == 0, "Swapping languages lost a translated text.");
            await SwapTranslation(); await Task.Delay(60);
            // Simulate a provider error, then ensure the window can run again.
            var good = SmokeRequest;
            SmokeRequest = (_, _, _, _) => throw new IOException("Fixture API failure");
            await Translate(); Check(notice.Text.Contains("Fixture API failure") && run.IsEnabled, "Provider failure left translation busy.");
            SmokeRequest = good; await Translate();
            var active = CancelProbe(); await active;
            Check(!Busy && notice.Text.StartsWith("Action annulée"), "Cancel did not restore translation controls.");
            SmokeRequest = good; await Translate();
        }
        else if (kind == ModelToolKind.Proofreader)
        {
            proofInput.Text = "salu comment est ce que tu va"; await CheckWriting();
            Check(proofDocument?.Corrections.Count == 3, "Proofreading suggestions are missing.");
            ApplyProof(proofDocument!.Corrections[0]);
            await Task.Delay(60);
            Check(proofInput.Text.StartsWith("Salut,") && proofDocument.Corrections.Count == 2, "Applying a suggestion failed.");
            proofDocument.Ignore(proofDocument.Corrections[0]); ApplyProof(null);
            Check(proofInput.Text.EndsWith("vas ?") && proofInput.Text.Contains("est ce"), "Ignore or apply all failed.");
            proofInput.Text += " "; await Task.Delay(60); Check(proofDocument == null, "Manual editing retained stale corrections.");
            proofTabs.SelectedItem = rewriteTab; await CheckWriting();
            Check(rewritten.Text == "Salut, comment vas-tu ?", "Rephrasing failed.");
            proofTabs.SelectedItem = correctTab; proofInput.Text = "salu comment est ce que tu va"; await CheckWriting();
        }
        else
        {
            await RunBenchmark();
            Check(benchmarkReport is { Completed: true, Passed: 6, Graded: 6 } && benchmarkReport.Entries.Count == 7,
                "Benchmark did not complete all scored cases.");
            Check(benchmarkReport!.InputTokens == 588 && benchmarkReport.OutputTokens == 189 && copyBenchmark.IsEnabled,
                "Benchmark counters or report copy are wrong.");
            ((Expander)benchmarkRows.Children[4]).IsExpanded = true;
        }
    }

    async Task CancelProbe()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SmokeRequest = async (_, _, _, ct) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); throw new Exception("Unreachable"); };
        var work = Translate(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancellation!.Cancel(); await work;
    }

    internal async Task CheckCloseCancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SmokeRequest = async (_, _, _, ct) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); throw new Exception("Unreachable"); };
        translationInput.Text = "Test";
        var active = Translate(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); Close();
        await active.WaitAsync(TimeSpan.FromSeconds(3));
    }
}
