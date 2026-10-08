using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeTextToolRegression(string output)
    {
        var fixture = new Provider { Name = "Text tool fixture", Model = "fixture-one", SelectedModelsJson = "[\"fixture-one\",\"fixture-two\"]", BaseUrl = "https://fixture.invalid/v1" };
        db.Providers.Add(fixture); await db.SaveChangesAsync(); provider = fixture; PopulateModelSelector();
        var notes = new List<string>();
        foreach (var kind in new[] { ModelToolKind.Translator, ModelToolKind.Proofreader })
        {
            await ShowDedicatedTool(kind); var window = dedicatedTools[kind];
            notes.Add(await window.ExerciseTextEditorRegression(kind));
            await Capture(window.Panel, Path.Combine(output, kind + "-stable.png")); window.Close();
        }
        await ShowAiDetectorAsync(); var detector = aiDetectorWindow!;
        await detector.ExerciseProviderRegression(fixture); detector.Close();
        await ShowAiDetectorAsync(); detector = aiDetectorWindow!;
        detector.CheckSavedProvider(fixture.Id, "fixture-two");
        var until=DateTime.UtcNow.AddSeconds(3);
        while (detector.Panel.ActualWidth<=0 && DateTime.UtcNow<until) await Task.Delay(25);
        await Task.Delay(250);
        await Capture(detector.Panel, Path.Combine(output, "detector-provider.png")); detector.Close();
        notes.Add("AI provider/model selection, native report, subjective score label, save/reopen and unchanged SlopTotal option passed.");
        File.WriteAllLines(Path.Combine(output, "smoke-ok.txt"), notes);
    }
}

internal sealed partial class ModelToolsWindow
{
    internal async Task<string> ExerciseTextEditorRegression(ModelToolKind kind)
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var input = kind == ModelToolKind.Translator ? translationInput : proofInput;
        var elapsed = Stopwatch.StartNew(); double last = 0, maxGap = 0; int ticks = 0;
        var heartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        heartbeat.Tick += (_, _) => { var now=elapsed.Elapsed.TotalMilliseconds; maxGap=Math.Max(maxGap,now-last);last=now;ticks++; };
        heartbeat.Start();
        try
        {
            var errors = new List<string>(); input.Error += errors.Add;
            await input.PasteForSmoke("<p><b>salu</b> comment est ce que tu va</p><p><a href='https://example.org'>Lien</a></p>", "salu comment est ce que tu va\nLien");
            var until=DateTime.UtcNow.AddSeconds(5);
            while (!input.Text.StartsWith("salu") && DateTime.UtcNow<until) await Task.Delay(25);
            Check(input.Text.StartsWith("salu"), "Native change notification did not reach the tool window.");
            var original=await input.CaptureAsync(); Check(original.Html.Contains("<b>")&&original.Html.Contains("href=\"https://example.org\""), "Formatted paste lost bold or links.");
            SmokeRequest = (_, request, _, _) =>
            {
                var result=request.System.StartsWith("Proofread") ? "{\"edits\":[{\"start\":0,\"length\":4,\"original\":\"salu\",\"replacement\":\"Salut\",\"reason\":\"Orthographe\"}]}" : request.Input;
                return Task.FromResult(new ModelToolResult(result, 100, 20, false, false, .1, .01));
            };
            if (kind == ModelToolKind.Translator) { await Translate(); Check(translationOutput.Text.Contains("salu"), "Translation did not complete."); }
            else { await CheckWriting(); Check(proofDocument?.Corrections.Count==1,"Proofreading report missing.");ApplyProof(null);Check(proofInput.Text.StartsWith("Salut"),"Correction did not apply."); }
            Check((kind==ModelToolKind.Translator?translationOutput:proofInput).Document.Html.Contains("<b>"), "Transformation lost inline formatting.");
            input.Text=""; await input.CaptureAsync();
            var rich="<p>"+string.Concat(Enumerable.Range(0,3500).Select(i=>"<span style='font-weight:bold'>"+(i%10)+" </span>"))+"</p>";
            var pasteStart=elapsed.Elapsed.TotalMilliseconds; await input.PasteForSmoke(rich,string.Concat(Enumerable.Repeat("1 ",3500)));
            var pasted=await input.CaptureAsync(); var pasteMs=elapsed.Elapsed.TotalMilliseconds-pasteStart;
            Check(pasted.Text.Length>=7000,"Long formatted paste lost text.");
            var before=pasted.Text; await input.PasteForSmoke(string.Concat(Enumerable.Repeat("<div>",500))+"Deep"+string.Concat(Enumerable.Repeat("</div>",500)),"Deep");
            Check((await input.CaptureAsync()).Text==before,"Rejected paste changed original text.");
            await Task.Delay(600); var calls=input.ScriptCallCount;
            await Task.Delay(1000); Check(input.ScriptCallCount==calls,"Editor executes scripts while idle.");
            Check(errors.Any(x=>x.Contains("complex")),"Complex paste error was not surfaced.");
            Check(ticks>10 && maxGap<1000,"Window dispatcher blocked during paste: "+maxGap+" ms.");
            return kind+": rich paste "+pasteMs.ToString("0")+" ms; maximum dispatcher gap "+maxGap.ToString("0")+" ms; no idle script polling; formatting preserved.";
        }
        finally { heartbeat.Stop(); }
    }
}

internal sealed partial class AiDetectorWindow
{
    internal async Task ExerciseProviderRegression(Provider fixture)
    {
        detectionService.SelectedIndex=providers.FindIndex(p=>p.Id==fixture.Id)+1; detectionModel.SelectedItem="fixture-two";
        text.Text="This is a sufficiently long source paragraph to inspect as part of the local fixture.";
        SmokeRequest=(_,_,_)=>Task.FromResult(new ModelToolResult("{\"score\":40,\"explanation\":\"Style homogène\",\"signals\":[{\"quote\":\"source paragraph\",\"reason\":\"Observation\"}],\"paragraphs\":[{\"index\":0,\"score\":40,\"explanation\":\"Observation locale\"}]}",100,50,false,false,.1,.01));
        await AnalyzeAsync();
        if (report?["detection_method"]?.GetValue<string>()!="language_model"||report["model"]?.GetValue<string>()!="fixture-two"||report["overall_score"]?.GetValue<double>()!=40||!caution.Text.Contains("étalonn")) throw new Exception("AI detector provider report failed: "+status.Text);
        if (request!=null||!analyze.IsEnabled) throw new Exception("AI detector left busy.");
        if (!((Choice[])detectionService.ItemsSource).Any(c=>c.Id=="sloptotal")) throw new Exception("SlopTotal option disappeared.");
    }
    internal void CheckSavedProvider(int id,string model)
    {
        if(SelectedProvider?.Id!=id||detectionModel.SelectedItem as string!=model)throw new Exception("AI detector provider selection was not restored.");
        connection.IsExpanded=true;
    }
}
