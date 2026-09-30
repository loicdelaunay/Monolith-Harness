using MonolithHarness.Core;
using SkiaSharp;
using System.Text;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml.Linq;

static class AssetChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        async Task Throws<T>(Func<Task> action,string name) where T:Exception
        { try{await action();}catch(T){check(true,name);return;}throw new Exception("Expected "+typeof(T).Name+": "+name); }
        string root=Path.Combine(Path.GetTempPath(),"omh-assets-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string db=Path.Combine(root,"database.sqlite");var store=new AssetWorkspace(db,1);
            var doc=await store.CreateAsync("Test <safe>",128,128,"none",default);
            doc=await store.UpdateAsync(doc.Id,doc.Revision,d=>d.Layers[0].Shapes.Add(new() { Id="red",Type="rect",X=10,Y=10,Width=80,Height=80,Fill="#FF0000" }),default);
            doc=await store.UpdateAsync(doc.Id,doc.Revision,d=>d.Layers.Add(new() { Id="front",Name="Front",Shapes=[new(){Id="blue",Type="circle",X=50,Y=50,Radius=20,Fill="#0000FF"}] }),default);
            using(var image=SKBitmap.Decode(AssetRenderer.Export(doc,"png")))
            {check(image.GetPixel(0,0).Alpha==0,"Asset PNG preserves transparent canvas");check(image.GetPixel(50,50).Blue==255 && image.GetPixel(15,15).Red==255,"Layers compose front-to-back correctly");}
            var svg=AssetRenderer.Svg(doc);var xml=XDocument.Parse(svg);XNamespace ns="http://www.w3.org/2000/svg";
            check(xml.Root!.Element(ns+"title")!.Value=="Test <safe>" && xml.Root.Elements(ns+"g").Count()==2,"SVG escapes text and retains editable layer groups");
            var moved=doc.Clone();AssetTools.Edit(moved,new JsonArray(new JsonObject{["action"]="move_layer",["layer_id"]="front",["index"]=0}));
            using(var image=SKBitmap.Decode(AssetRenderer.Export(moved,"png")))check(image.GetPixel(50,50).Red==255,"Layer reorder changes rendered occlusion");
            var hidden=doc.Clone();hidden.Layers[1].Visible=false;
            using(var image=SKBitmap.Decode(AssetRenderer.Export(hidden,"png")))check(image.GetPixel(50,50).Red==255,"Hidden layer excluded from capture");
            var alpha=doc.Clone();alpha.Layers=[new(){Opacity=.5,Shapes=[new(){Id="green",Fill="#00FF0080",Width=128,Height=128}]}];
            using(var image=SKBitmap.Decode(AssetRenderer.Export(alpha,"png")))check(image.GetPixel(30,30).Alpha is >=63 and <=65,"Color alpha and layer opacity multiply correctly");
            var background=doc.Clone();background.Background="#FFFFFF";
            using(var image=SKBitmap.Decode(AssetRenderer.Export(background,"png",true)))check(image.GetPixel(0,0).Alpha==0,"Transparent export omits canvas background");
            using(var image=SKBitmap.Decode(AssetRenderer.Export(background,"png")))check(image.GetPixel(0,0)==SKColors.White,"Opaque export includes canvas background");
            using(var image=SKBitmap.Decode(AssetRenderer.Export(doc,"webp")))check(image.GetPixel(0,0).Alpha==0,"WebP export preserves transparency");
            using(var image=SKBitmap.Decode(AssetRenderer.Export(doc,"jpeg",background:"#FFFFFF",scale:2)))check(image.Width==256 && image.Height==256 && image.GetPixel(0,0).Alpha==255,"JPEG exports at requested scale with matte");
            check(Encoding.ASCII.GetString(AssetRenderer.Export(doc,"pdf"),0,5)=="%PDF-","Vector PDF export created");
            await Throws<ArgumentException>(()=>Task.Run(()=>AssetRenderer.Export(doc,"jpeg",true)),"JPEG transparency explicitly rejected");
            await Throws<ArgumentException>(()=>Task.Run(()=>AssetRenderer.Export(new(){Width=4096,Height=4096},"png",scale:4)),"Raster export memory cap enforced");
            using(var image=SKBitmap.Decode(AssetRenderer.Preview(doc,64)))check(image.Width==64 && image.Height==64,"Capture size limit preserves aspect and dimensions");
            var persisted=await new AssetWorkspace(db,1).ReadAsync(doc.Id,default);
            check(persisted.Revision==3 && persisted.Layers[1].Shapes[0].Id=="blue","Drawings persist across workspace reloads");
            check((await new AssetWorkspace(db,2).ListAsync(default)).Count==0,"Conversations have isolated assets");
            await Throws<InvalidOperationException>(()=>store.UpdateAsync(doc.Id,1,d=>d.Name="stale",default),"Stale edits rejected without overwriting user changes");
            await Throws<ArgumentException>(()=>store.UpdateAsync(doc.Id,doc.Revision,d=>{d.Name="bad";d.Layers[0].Shapes[0].Fill="url(https://example.com)";},default),"Remote SVG paint references rejected");
            check((await store.ReadAsync(doc.Id,default)).Name==doc.Name,"Invalid operation leaves the original document unchanged");
            await Throws<ArgumentException>(()=>store.ReadAsync("../escape",default),"Asset path traversal blocked");
            var text=doc.Clone();text.Layers[0].Shapes.Add(new(){Id="text",Type="text",Text="<script>alert(1)</script>",X=5,Y=100,Fill="#FFFFFF"});
            check(!AssetRenderer.Svg(text).Contains("<script>"),"SVG text cannot inject markup");
            var path=doc.Clone();path.Layers=[new(){Shapes=[new(){Id="curve",Type="path",Path="M 10 10 C 20 0 80 0 90 10 L 90 90 L 10 90 Z",Fill="#FF00FF"}]}];
            using(var image=SKBitmap.Decode(AssetRenderer.Export(path,"png")))check(image.GetPixel(50,50).Red==255 && image.GetPixel(50,50).Blue==255,"Arbitrary SVG curve paths render");
            foreach(string format in new[]{"svg","png","webp","jpeg","pdf"})
            {var file=await store.ExportAsync(doc,format,false,"#FFFFFF",1,default);check(File.Exists(file)&&new FileInfo(file).Length>10,"Export saved safely: "+format);}
            var sprite=await store.CreateAsync("Pixel animation",32,32,"none",4,default);
            sprite=await store.UpdateAsync(sprite.Id,sprite.Revision,d=>AssetTools.Edit(d,new JsonArray(
                new JsonObject{["action"]="pixel_rect",["layer_id"]="layer-1",["x"]=1,["y"]=2,["width"]=2,["height"]=2,["color"]="#FF0000"},
                new JsonObject{["action"]="frame_add",["frame_id"]="step-1",["duration_ms"]=80},
                new JsonObject{["action"]="frame_add",["frame_id"]="step-2",["source_frame_id"]="step-1",["duration_ms"]=140},
                new JsonObject{["action"]="pixel",["frame_id"]="step-2",["layer_id"]="layer-1",["x"]=1,["y"]=2,["color"]="#0000FF"})),default);
            check(sprite.PixelSize==4&&sprite.Frames.Count==2&&sprite.Frames[1].Layers[0].Pixels.Count==4,"Pixel grid and editable animation frames persist");
            using(var first=SKBitmap.Decode(AssetRenderer.Preview(sprite,64,false,0)))
            using(var second=SKBitmap.Decode(AssetRenderer.Preview(sprite,64,false,1)))
                check(first.Width==64&&first.GetPixel(10,18).Red==255&&second.GetPixel(10,18).Blue==255,"Frame preview enlarges pixel changes with hard edges");
            var tiny=await store.CreateAsync("Tiny custom icon",16,16,"none",1,"pixel_art",default);
            tiny=await store.UpdateAsync(tiny.Id,tiny.Revision,d=>AssetTools.Edit(d,new JsonArray(
                new JsonObject{["action"]="pixel",["layer_id"]="layer-1",["x"]=1,["y"]=2,["color"]="#FF0000"})),default);
            check(tiny.IsPixelArt&&tiny.Mode=="pixel_art"&&(await store.ReadAsync(tiny.Id,default)).Mode=="pixel_art","Explicit pixel-art mode and custom resolution persist");
            using(var original=SKBitmap.Decode(AssetRenderer.Export(tiny,"png")))
                check(original.Width==16&&original.Height==16&&original.GetPixel(1,2).Red==255&&original.GetPixel(2,2).Alpha==0,"Pixel-art export draws individual pixels at native resolution");
            using(var preview=SKBitmap.Decode(AssetRenderer.Preview(tiny,160)))
                check(preview.Width==160&&preview.GetPixel(19,29).Red==255&&preview.GetPixel(20,29).Alpha==0,"Pixel-art preview enlarges with nearest-neighbor edges");
            check(AssetRenderer.PreviewScale(tiny,160)==10&&AssetRenderer.Svg(tiny).Contains("shape-rendering=\"crispEdges\""),"Pixel-art capture scale and SVG disable smoothing");
            await Throws<ArgumentException>(()=>store.UpdateAsync(tiny.Id,tiny.Revision,d=>d.Layers[0].Shapes.Add(new(){Id="not-a-pixel"}),default),"Pixel-art mode rejects vector shapes");
            var custom=await store.CreateAsync("Custom sprite",24,20,"none",1,"pixel_art",default);
            check(custom.Width==24&&custom.Height==20,"Pixel-art mode accepts custom non-square resolutions");
            var guided=JsonNode.Parse(AssetGuides.Describe(sprite,"step-1"))!;
            check(guided["center"]!["x"]!.GetValue<double>()==16&&guided["bounds"]![0]!["width"]!.GetValue<int>()==8,"Numeric guides expose pixel bounding box and center");
            var withGuides=AssetRenderer.Preview(sprite,64,true,0,true,true);
            check(!withGuides.SequenceEqual(AssetRenderer.Preview(sprite,64,true,0)),"Alignment guides and grid appear only in preview");
            using(var clearGuide=SKBitmap.Decode(AssetRenderer.Preview(sprite,64,false,0,true)))check(clearGuide.GetPixel(0,0).Alpha==0,"Guide capture keeps transparent canvas transparent");
            var animatedSvg=XDocument.Parse(Encoding.UTF8.GetString(AssetRenderer.Export(sprite,"svg-animated")));
            check(animatedSvg.Descendants(ns+"animate").Count()==2&&animatedSvg.Descendants(ns+"g").Any(x=>(string?)x.Attribute("id")=="frame-2"),"Animated SVG contains timed frame groups");
            var gif=AssetRenderer.Export(sprite,"gif");
            using(var codec=SKCodec.Create(new MemoryStream(gif)))check(Encoding.ASCII.GetString(gif,0,6)=="GIF89a"&&codec.FrameCount==2,"GIF decodes as a two-frame animation");
            var sequence=AssetRenderer.Export(sprite,"frames");
            using(var zip=new ZipArchive(new MemoryStream(sequence),ZipArchiveMode.Read))check(zip.Entries.Count==3&&zip.GetEntry("frames.json")!=null&&zip.GetEntry("frame-002.png")!=null,"PNG sequence export includes frame files and timing manifest");
            await Throws<ArgumentException>(()=>Task.Run(()=>AssetRenderer.Export(new(){Width=33,Height=32,PixelSize=4},"png")),"Pixel size must divide canvas");
            await Throws<ArgumentException>(()=>Task.Run(()=>AssetRenderer.Export(sprite,"gif",scale:2)),"GIF rejects unsupported scaling");
            await Throws<ArgumentException>(()=>Task.Run(()=>AssetRenderer.Export(new(){Width=4096,Height=4096,Frames=[new(){Id="a",Layers=[new()]},new(){Id="b",Layers=[new()]},new(){Id="c",Layers=[new()]},new(){Id="d",Layers=[new()]}]},"frames",scale:2)),"Frame sequence rejects excessive total pixels");
            using var run=new ConversationSession(new(){Id=1,ExecutionMode="execute"},new(),new(){SupportsImages=true},new(){EnabledSkills=AssetTools.SkillId},"",[],db);
            string enabled=AssetTools.SkillId;bool allowed=true;int asks=0;
            Task<AssetTools.Result> Tool(string name,JsonObject args)=>AssetTools.CallAsync(run,name,args,_=>Task.FromResult(enabled),(_,_,_,_)=>{asks++;return Task.FromResult(allowed);},default);
            var capture=await Tool("asset_capture",new(){["asset_id"]=doc.Id});
            check(capture.Image?.Mime=="image/png" && asks==1,"Capture returns an image attachment after approval");
            check(capture.Image!.Data.Length>0 && JsonNode.Parse(capture.Text)!["capture_width"]!.GetValue<int>()==128,"Capture metadata identifies artwork coordinates");
            var compact=await Tool("asset_edit",new(){["asset_id"]=tiny.Id,["expected_revision"]=tiny.Revision,["compact_response"]=true,
                ["operations"]=new JsonArray(new JsonObject{["action"]="pixel_stamp",["layer_id"]="layer-1",["x"]=0,["y"]=0,["rows"]=new JsonArray("AA"),["palette"]=new JsonObject{["A"]="#00FF00"}})});
            check(JsonNode.Parse(compact.Text)!["document"]==null && compact.Document?.Layers[0].Pixels.Count==3,"Compact tool replies retain the full document for live GUI updates");
            var batch=await store.CreateAsync("Multi-action sprite",16,16,"none",1,"pixel_art",default);
            int approvalsBeforeBatch=asks;
            var editedBatch=await Tool("asset_edit",new(){["asset_id"]=batch.Id,["expected_revision"]=batch.Revision,["compact_response"]=true,
                ["operations"]=new JsonArray(
                    new JsonObject{["action"]="layer",["layer_id"]="sprite",["name"]="Sprite"},
                    new JsonObject{["action"]="pixel_stamp",["layer_id"]="sprite",["x"]=2,["y"]=3,["rows"]=new JsonArray("AB","BA"),["palette"]=new JsonObject{["A"]="#FF0000",["B"]="#0000FF"}},
                    new JsonObject{["action"]="frame_add",["frame_id"]="walking",["duration_ms"]=90},
                    new JsonObject{["action"]="pixel_transform",["frame_id"]="walking",["layer_id"]="sprite",["x"]=2,["y"]=3,["width"]=2,["height"]=2,["to_x"]=6,["to_y"]=3,["copy"]=false})});
            var savedBatch=await store.ReadAsync(batch.Id,default);
            check(asks==approvalsBeforeBatch+1 && editedBatch.Document?.Revision==batch.Revision+1 && savedBatch.Revision==batch.Revision+1
                && savedBatch.Layers.Single(l=>l.Id=="sprite").Pixels.Count==4
                && savedBatch.Frames.Single(f=>f.Id=="walking").Layers.Single(l=>l.Id=="sprite").Pixels.All(p=>p.X is 6 or 7),
                "One asset_edit runs ordered layer, stamp, frame and transform actions with one approval and revision");
            await Throws<ArgumentException>(()=>Tool("asset_edit",new(){["asset_id"]=batch.Id,["expected_revision"]=savedBatch.Revision,
                ["operations"]=new JsonArray(
                    new JsonObject{["action"]="pixel",["layer_id"]="sprite",["x"]=0,["y"]=0,["color"]="#00FF00"},
                    new JsonObject{["action"]="pixel_stamp",["layer_id"]="sprite",["x"]=4,["y"]=4,["rows"]=new JsonArray("X"),["palette"]=new JsonObject()})}),
                "A later invalid action rejects the whole asset_edit batch");
            var afterFailedBatch=await store.ReadAsync(batch.Id,default);
            check(afterFailedBatch.Revision==savedBatch.Revision && afterFailedBatch.Layers.Single(l=>l.Id=="sprite").Pixels.All(p=>p.X!=0 || p.Y!=0),
                "A failed multi-action edit does not persist earlier drawing actions");
            var large=await store.CreateAsync("Large pixel canvas",128,128,"none",1,"pixel_art",default);
            await Throws<ArgumentException>(()=>Tool("asset_capture",new(){["asset_id"]=large.Id,["max_size"]=64}),"Capture rejects a size that would merge logical pixels");
            allowed=false;var denied=await Tool("asset_edit",new(){["asset_id"]=doc.Id,["expected_revision"]=doc.Revision,["operations"]=new JsonArray(new JsonObject{["action"]="delete_layer",["layer_id"]="front"})});
            check(denied.Text.Contains("denied") && (await store.ReadAsync(doc.Id,default)).Revision==3,"Denied asset edit leaves drawing unchanged");
            enabled="";await Throws<UnauthorizedAccessException>(()=>Tool("asset_capture",new(){["asset_id"]=doc.Id}),"Disabled skill blocks capture");
            enabled=AssetTools.SkillId;allowed=true;run.Chat.ExecutionMode="plan";
            await Throws<UnauthorizedAccessException>(()=>Tool("asset_create",new(){["name"]="blocked"}),"Plan mode blocks canvas writes");
            check((await Tool("asset_inspect",new(){["asset_id"]=doc.Id})).Text.Contains("Test"),"Plan mode permits asset inspection");
            run.Chat.SandboxEnabled=true;await Throws<UnauthorizedAccessException>(()=>Tool("asset_inspect",new(){["asset_id"]=doc.Id}),"Offline sandbox cannot access host assets");
            var tools=new JsonArray();AssetTools.AddDefinitions(tools,AssetTools.SkillId);check(tools.Count==6,"Asset skill exposes six shared GUI/CLI tools");
            var none=new JsonArray();AssetTools.AddDefinitions(none,"");check(none.Count==0,"Disabled skill hides all asset definitions");
        }
        finally {Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
    }
}
