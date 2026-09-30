using System.Net;
using System.Text.Json.Nodes;
using System.IO.Compression;
using MonolithHarness.Core;

internal static class TextToolRegressionChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var input="A sufficiently long source paragraph that is deliberately used only for the offline fixture.";
        const string response="{\"score\":42,\"explanation\":\"Style homogène\",\"signals\":[{\"quote\":\"source paragraph\",\"reason\":\"Observation\"}],\"paragraphs\":[{\"index\":0,\"score\":35,\"explanation\":\"Passage observé\"}]}";
        var prompt=AiTextDetection.Request(input,true,"fr");
        check(prompt.System.Contains("untrusted data")&&prompt.System.Contains("subjective")&&prompt.Messages().Count==2,"AI detector separates subjective scoring and untrusted input");
        var report=AiTextDetection.Report(input,response,true,"Fixture model");
        check(report["overall_score"]!.GetValue<double>()==42&&report["detection_method"]!.GetValue<string>()=="language_model","AI score is validated without silent normalization");
        check(report["paragraph_analysis"]!["paragraphs"]![0]!["text"]!.GetValue<string>()==input,"Paragraph report uses original text rather than model-generated text");
        bool Invalid(string value){try{AiTextDetection.Report(input,value,true,"Fixture");return false;}catch(FormatException){return true;}}
        check(Invalid(response.Replace("\"score\":42","\"score\":101"))&&Invalid(response.Replace("\"score\":42","\"score\":\"high\"")),"Invalid and nonnumeric scores do not become a fabricated zero");
        check(Invalid(response.Replace("source paragraph","invented quote")),"Model evidence must match the original source");
        check(Invalid(response.Replace("\"index\":0","\"index\":3"))&&Invalid(response.Replace("\"paragraphs\":[{","\"paragraphs\":[{\"index\":0,\"score\":35,\"explanation\":\"duplicate\"},{")),"Out-of-range and duplicate paragraph references are rejected");
        var unknown=AiTextDetection.Report(input,response.Replace("\"score\":42","\"score\":null"),true,"Fixture");
        check(SlopTotalClient.EngineFailed(unknown["engine_results"]![0]!.AsObject())&&unknown["overall_score"]==null,"Insufficient evidence remains indeterminate instead of being reported as human-written");
        var noParagraphs="{\"score\":20,\"explanation\":\"Peu d'indices\",\"signals\":[],\"paragraphs\":[]}";
        check(AiTextDetection.Report(input,noParagraphs,false,"Fixture")["paragraph_analysis"]!["paragraphs"]!.AsArray().Count==0,"Global-only provider analysis is supported");
        var settings=new FeatureSettings { AiDetectorProviderId=4,AiDetectorModel="saved-model" };
        var restored=FeatureSettings.Read(settings.Json());check(restored.AiDetectorProviderId==4&&restored.AiDetectorModel=="saved-model","Provider/model selection persists independently of the chat model");
        var styled=new FormattedText("<p><b><!--omh-text-0--></b> <!--omh-text-1--></p>",[new(0,"salu"),new(1,"ami")]);
        var corrected=styled.Apply([new(0,4,"salu","salut","Orthographe")]);
        check(corrected.Html=="<p><b>salut</b> ami</p>","Proofreading preserves bold style and paragraph structure");
        var transform=styled.ReadTransformation("{\"segments\":[{\"id\":0,\"text\":\"hello\"},{\"id\":1,\"text\":\"friend\"}]}");
        check(transform.Html=="<p><b>hello</b> friend</p>","Translation preserves the original HTML template");
        JsonObject? wire=null;
        using(var http=new HttpClient(new FakeHandler(async request=>{
            wire=JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
            var data=new JsonObject { ["choices"]=new JsonArray(new JsonObject { ["delta"]=new JsonObject { ["content"]=response } }) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent("data: "+data.ToJsonString()+"\n\ndata: [DONE]\n\n") };
        })))
        {
            var provider=new Provider { BaseUrl="https://fixture.invalid/v1",Model="selected-model",Kind="openai" };
            var result=await new ModelToolClient(http).RunAsync(provider,"fixture-key",prompt,_=>{},default);
            check(result.Text==response&&wire?["model"]?.GetValue<string>()=="selected-model"&&wire?["tools"]==null,"Detection uses selected provider/model with no tools enabled");
        }
        var folder=Path.Combine(Path.GetTempPath(),"monolith-text-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var text=Path.Combine(folder,"text.txt");await File.WriteAllTextAsync(text,input);
            check(await AiTextDetection.ReadDocumentAsync(text,default)==input,"Text documents extract locally without SlopTotal");
            var docx=Path.Combine(folder,"text.docx");
            using(var zip=ZipFile.Open(docx,ZipArchiveMode.Create))
            { using var writer=new StreamWriter(zip.CreateEntry("word/document.xml").Open());writer.Write("<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>Premier</w:t></w:r></w:p><w:p><w:r><w:t>Deuxième</w:t></w:r></w:p></w:body></w:document>"); }
            check(await AiTextDetection.ReadDocumentAsync(docx,default)=="Premier\n\nDeuxième","DOCX paragraph extraction stays local and preserves boundaries");
        }
        finally { Directory.Delete(folder,true); }
    }
}
