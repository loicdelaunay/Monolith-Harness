using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MonolithHarness.Core;
namespace MonolithHarness.Core.Mobile;

public sealed class MobileTools : IConversationTools,IDisposable
{
    readonly MobileSettings settings;readonly MobileFiles files;readonly MobileMemory memory;readonly MobileWeb web=new();
    readonly Func<string,string,CancellationToken,Task<bool>> approve;
    readonly HashSet<string> names=[];
    public JsonArray Definitions {get;}=[];
    public MobileTools(string directory,int projectId,int chatId,MobileSettings settings,Func<string,string,CancellationToken,Task<bool>> approve)
    {
        this.settings=settings.Copy();this.settings.Normalize();this.approve=approve;files=new(directory,chatId);memory=new(directory,projectId,chatId,settings.AllowSharedMemory);
        if(settings.Enabled("informations"))Define("device_information","Get the enabled current date/time/Android categories.");
        if(settings.Enabled("web") && settings.AllowNetwork) {
            Define("web_search","Search public HTTPS pages; search terms go to DuckDuckGo.",("query","string",true));
            Define("web_read","Read public HTTPS page text/JSON, with numbered lines.",("url","string",true),("start_line","integer",false),("line_count","integer",false));
        }
        if(settings.Enabled("sources")) {
            Define("list_sources","List this conversation's imported/private text files.");
            Define("read_source","Read a file with numbered lines.",("path","string",true),("start_line","integer",false),("line_count","integer",false));
            Define("grep_sources","Find literal text in the conversation files.",("query","string",true));
            if(settings.AllowFileWrites) {
                Define("write_source","Create or replace a private conversation text file. Requires approval; never modifies the Android original.",("path","string",true),("content","string",true));
                Define("edit_source","Replace exact text once in a private file. Requires approval.",("path","string",true),("old_text","string",true),("new_text","string",true));
            }
        }
        if(settings.Enabled("memory")) {
            Define("memory_search","Search visible conversation/project memories, or shared memories when enabled.",("query","string",false));
            Define("memory_save","Save a durable fact after approval, scope conversation/project/shared.",("scope","string",true),("content","string",true));
            Define("memory_delete","Delete a visible memory by ID after approval.",("id","integer",true));
        }
    }
    void Define(string name,string description,params (string Name,string Type,bool Required)[] fields)
    {
        var properties=new JsonObject();var required=new JsonArray();foreach(var field in fields) {properties[field.Name]=new JsonObject {["type"]=field.Type};if(field.Required)required.Add(field.Name);}
        Definitions.Add(new JsonObject {["type"]="function",["function"]=new JsonObject {["name"]=name,["description"]=description,["parameters"]=new JsonObject {["type"]="object",["properties"]=properties,["required"]=required,["additionalProperties"]=false}}});names.Add(name);
    }
    public static string Information(MobileSettings s)
    {
        var info=new JsonObject();if(s.InformationDateTime) {info["local_datetime"]=DateTimeOffset.Now.ToString("O");info["utc_datetime"]=DateTimeOffset.UtcNow.ToString("O");info["weekday"]=DateTime.Now.DayOfWeek.ToString();}
        if(s.InformationTimeZone)info["time_zone"]=TimeZoneInfo.Local.Id;
        if(s.InformationSystem) {info["system"]="Android";info["version"]=Android.OS.Build.VERSION.Release;info["api_level"]=(int)Android.OS.Build.VERSION.SdkInt;}
        if(s.InformationLocale) {info["culture"]=CultureInfo.CurrentCulture.Name;info["ui_culture"]=CultureInfo.CurrentUICulture.Name;}
        return info.ToJsonString();
    }
    async Task PermitAsync(string name,JsonObject args,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(settings.ApprovalMode=="deny")throw new UnauthorizedAccessException("Outils refusés par le réglage Refuser tout. Ne pas contourner ce refus.");
        if(settings.ApprovalMode=="allow")return;
        var (heading,detail)=name switch {
            "web_search"=>("Recherche web","Recherche envoyée à DuckDuckGo :\n\n"+Text(args,"query")),
            "web_read"=>("Lire une page web",Text(args,"url")+"\n\nLe texte de cette page sera transmis au modèle de la conversation."),
            "device_information"=>("Informations Android","Lire les catégories d’informations activées dans Paramètres / Skills."),
            "list_sources"=>("Lister les documents","Lister les copies privées de cette discussion."),
            "read_source"=>("Lire un document",Text(args,"path")),
            "grep_sources"=>("Rechercher dans les documents",Text(args,"query")),
            "write_source"=>("Modifier un document",Text(args,"path")+"\n\nRemplacer ou créer la copie privée de la discussion. L’original Android reste inchangé.\n\nNouveau contenu :\n"+Preview(Text(args,"content"))),
            "edit_source"=>("Modifier un document",Text(args,"path")+"\n\nModifier la copie privée de la discussion. L’original Android reste inchangé.\n\nRemplacer :\n"+Preview(Text(args,"old_text"))+"\n\nPar :\n"+Preview(Text(args,"new_text"))),
            "memory_search"=>("Rechercher des souvenirs",Text(args,"query","Souvenirs visibles dans cette discussion et son projet.")),
            "memory_save"=>("Enregistrer un souvenir","Portée : "+Text(args,"scope")+"\n\n"+Preview(Text(args,"content"))),
            "memory_delete"=>("Supprimer un souvenir","Supprimer le souvenir n° "+args["id"]?.ToString()),
            _=>("Utiliser un outil",name)
        };
        if(!await approve(heading,detail,ct))throw new UnauthorizedAccessException("Action refusée par l’utilisateur. Ne pas contourner ce refus.");
        ct.ThrowIfCancellationRequested();
    }
    static string Preview(string text)=>text[..Math.Min(1800,text.Length)]+(text.Length>1800?"\n…":"");
    static string Text(JsonObject args,string name,string fallback="")=>args[name]?.GetValue<string>()??fallback;
    static int Number(JsonObject args,string name,int fallback)=>args[name]?.GetValue<int>()??fallback;
    public async Task<string> ExecuteAsync(string name,JsonObject args,CancellationToken ct)
    {
        if(!names.Contains(name))return new JsonObject {["error"]="Outil désactivé ou indisponible sur Android."}.ToJsonString();
        await PermitAsync(name,args,ct);JsonNode result;
        switch(name)
        {
            case "device_information":return Information(settings);
            case "web_search": {
                var query=Text(args,"query");result=await web.SearchAsync(query,ct);break;}
            case "web_read": {
                var url=Text(args,"url");MobileWeb.Validate(url);
                result=await web.ReadAsync(url,Number(args,"start_line",1),Number(args,"line_count",60),ct);break;}
            case "list_sources":result=JsonSerializer.SerializeToNode(files.List())!;break;
            case "read_source": {
                var path=Text(args,"path");var text=await files.ReadAsync(path,ct);var lines=text.Replace("\r","").Split('\n');var start=Math.Max(1,Number(args,"start_line",1));var count=Math.Clamp(Number(args,"line_count",80),1,120);
                var excerpt=string.Join('\n',lines.Skip(start-1).Take(count).Select((line,i)=>$"{start+i}: {line}"));var truncated=excerpt.Length>18000;
                result=new JsonObject {["path"]=path,["line_count"]=lines.Length,["start_line"]=start,["truncated"]=truncated,["text"]=truncated?excerpt[..18000]:excerpt};break;}
            case "grep_sources": {
                var query=Text(args,"query");if(query.Length==0 || query.Length>500)throw new ArgumentException("Recherche entre 1 et 500 caractères requise.");var hits=new JsonArray();
                foreach(var document in files.List()) {ct.ThrowIfCancellationRequested();var lines=(await files.ReadAsync(document.Path,ct)).Split('\n');for(int i=0;i<lines.Length && hits.Count<40;i++)if(lines[i].Contains(query,StringComparison.OrdinalIgnoreCase))hits.Add(new JsonObject {["path"]=document.Path,["line"]=i+1,["text"]=lines[i][..Math.Min(400,lines[i].Length)]});if(hits.Count>=40)break;}
                result=new JsonObject {["matches"]=hits,["truncated"]=hits.Count>=40};break;}
            case "write_source":case "edit_source": {
                var path=Text(args,"path");files.Resolve(path);var content=Text(args,"content");
                if(name=="edit_source") {var old=Text(args,"old_text");content=await files.ReadAsync(path,ct);var index=content.IndexOf(old,StringComparison.Ordinal);if(old.Length==0 || index<0 || content.IndexOf(old,index+old.Length,StringComparison.Ordinal)>=0)throw new ArgumentException("Le texte à remplacer doit correspondre exactement une fois.");content=content[..index]+Text(args,"new_text")+content[(index+old.Length)..];}
                await files.WriteAsync(path,content,ct);result=new JsonObject {["path"]=path,["saved"]=true,["original_modified"]=false};break;}
            case "memory_search":result=JsonSerializer.SerializeToNode(await memory.SearchAsync(Text(args,"query"),ct))!;break;
            case "memory_save": {
                var scope=Text(args,"scope");var content=Text(args,"content");if(scope=="shared" && !settings.AllowSharedMemory)throw new UnauthorizedAccessException("Mémoire partagée désactivée.");
                var id=await memory.SaveAsync(scope,content,ct);result=new JsonObject {["id"]=id,["scope"]=scope,["saved"]=true};break;}
            case "memory_delete": {
                var id=args["id"]?.GetValue<long>()??0;var entry=await memory.FindAsync(id,ct)??throw new ArgumentException("Souvenir introuvable dans les entrées visibles.");
                await memory.DeleteAsync(id,ct);result=new JsonObject {["deleted"]=id};break;}
            default:throw new InvalidOperationException("Outil indisponible.");
        }
        return result.ToJsonString();
    }
    public void Dispose()=>web.Dispose();
}
