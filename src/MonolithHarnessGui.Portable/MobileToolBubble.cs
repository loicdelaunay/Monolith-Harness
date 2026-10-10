using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
namespace MonolithHarnessGui.Portable;

/// <summary>One compact activity card per user turn; raw persisted results stay available to the model.</summary>
sealed class MobileToolBubble : Border
{
    readonly List<ConversationToolActivity> calls=[];
    readonly TextBlock caption=new() {FontSize=13,MaxLines=2,TextWrapping=TextWrapping.Wrap};
    readonly TextBlock arrow=new() {Text="▸",FontSize=16,VerticalAlignment=VerticalAlignment.Center};
    readonly ProgressRing progress=new() {Width=20,Height=20,Visibility=Visibility.Collapsed};
    readonly StackPanel details=new() {Spacing=10,Visibility=Visibility.Collapsed};
    public MobileToolBubble()
    {
        CornerRadius=new(20);Padding=new(4);Background=Role("SurfaceBrush");BorderBrush=Role("OutlineVariantBrush");BorderThickness=new(1);HorizontalAlignment=HorizontalAlignment.Left;
        var root=new StackPanel {Spacing=4};var row=new Grid {ColumnSpacing=10};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        arrow.Foreground=caption.Foreground=Role("OnSurfaceVariantBrush");row.Children.Add(arrow);Grid.SetColumn(caption,1);row.Children.Add(caption);Grid.SetColumn(progress,2);row.Children.Add(progress);
        var toggle=new Button {Content=row,MinHeight=48,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new(12,8,12,8),CornerRadius=new(16),Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent),BorderThickness=new(0)};
        toggle.Click+=(_,_)=>{var open=details.Visibility!=Visibility.Visible;details.Visibility=open?Visibility.Visible:Visibility.Collapsed;arrow.Text=open?"▾":"▸";};
        details.Margin=new(12,0,12,12);root.Children.Add(toggle);root.Children.Add(details);Child=root;
    }
    static Brush Role(string name)=>Application.Current.Resources.TryGetValue(name,out var value) && value is Brush brush?brush:new SolidColorBrush(Microsoft.UI.Colors.Gray);
    static string Short(string value,int max=160)=>value.Replace('\r',' ').Replace('\n',' ').Trim() is var text && text.Length>max?text[..max]+"…":value.Replace('\r',' ').Replace('\n',' ').Trim();
    static JsonNode? Parse(string value) {try{return JsonNode.Parse(value);}catch(System.Text.Json.JsonException){return null;}}
    static string Value(JsonNode? node,string name)=>node is JsonObject obj?obj[name]?.ToString()??"":"";
    public static string ToolName(string name)=>name switch {
        "web_search"=>"Recherche web","web_read"=>"Lecture web","device_information"=>"Informations Android",
        "list_sources"=>"Liste des documents","read_source"=>"Lecture d’un document","grep_sources"=>"Recherche dans les documents",
        "write_source"=>"Écriture d’un document","edit_source"=>"Modification d’un document",
        "memory_search"=>"Recherche de souvenirs","memory_save"=>"Souvenir enregistré","memory_delete"=>"Suppression d’un souvenir",_=>name.Length>0?name:"Outil"
    };
    public void Update(ConversationToolActivity activity)
    {
        // Providers may omit a call ID. A completion still updates the latest matching running call.
        var index=activity.CallId.Length>0?calls.FindIndex(call=>call.CallId==activity.CallId):calls.FindLastIndex(call=>call.Name==activity.Name && call.Running);
        if(index<0)calls.Add(activity);else calls[index]=activity;
        var running=calls.LastOrDefault(call=>call.Running);var failed=calls.Count(call=>!call.Running && !call.Success);
        caption.Text=$"{calls.Count} appel{(calls.Count>1?"s":"")} d’outil{(calls.Count>1?"s":"")} · "+(running!=null?ToolName(running.Name)+"…":failed>0?$"{failed} refus ou erreur":"Terminé");
        progress.IsActive=running!=null;progress.Visibility=running!=null?Visibility.Visible:Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this,caption.Text);
        details.Children.Clear();
        foreach(var call in calls) {
            var row=new StackPanel {Spacing=3};row.Children.Add(new TextBlock {Text=(call.Running?"◌ ":call.Success?"✓ ":"! ")+ToolName(call.Name),TextWrapping=TextWrapping.Wrap,FontSize=13,Foreground=Role(!call.Running && !call.Success?"ErrorBrush":"OnSurfaceBrush")});
            var description=Summary(call);if(description.Length>0)row.Children.Add(new TextBlock {Text=description,TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=Role("OnSurfaceVariantBrush"),IsTextSelectionEnabled=true});details.Children.Add(row);
        }
    }
    static string Summary(ConversationToolActivity call)
    {
        var args=Parse(call.Arguments);var result=Parse(call.Result);var target=Value(args,"query");if(target.Length==0)target=Value(args,"url");if(target.Length==0)target=Value(args,"path");
        if(call.Running)return Short(target.Length>0?target:"En cours…");
        var error=Value(result,"error");if(error.Length>0)return Short(error);
        string detail=call.Name switch {
            "web_search"=>$"{(result?["results"] as JsonArray)?.Count??0} résultats · "+Value(result,"query"),
            "web_read"=>Value(result,"title")+" · "+Value(result,"url"),
            "read_source"=>Value(result,"path")+" · ligne "+Value(result,"start_line"),
            "list_sources"=>$"{(result as JsonArray)?.Count??0} documents disponibles",
            "grep_sources"=>$"{(result?["matches"] as JsonArray)?.Count??0} correspondances · "+target,
            "write_source" or "edit_source"=>Value(result,"path")+" · copie privée enregistrée",
            "memory_search"=>$"{(result as JsonArray)?.Count??0} souvenirs trouvés",
            "memory_save"=>"Portée : "+Value(result,"scope"),
            "memory_delete"=>"Souvenir n° "+Value(result,"deleted"),
            "device_information"=>"Catégories activées consultées",
            _=>call.Detail
        };
        return Short(detail);
    }
    public static Dictionary<string,(string Name,string Arguments)> Calls(string wire)
    {
        var map=new Dictionary<string,(string,string)>();var node=Parse(wire);
        if(node is JsonObject obj && obj["tool_calls"] is JsonArray array)foreach(var call in array.OfType<JsonObject>()) {
            var id=Value(call,"id");if(id.Length>0)map[id]=(Value(call["function"],"name"),Value(call["function"],"arguments"));
        }
        return map;
    }
    public static ConversationToolActivity Stored(Message message,Dictionary<string,(string Name,string Arguments)> known)
    {
        var id=Value(Parse(message.WireJson),"tool_call_id");var call=known.GetValueOrDefault(id,("Outil","{}"));var result=Parse(message.Content);
        var success=result!=null && Value(result,"error").Length==0;
        return new(call.Item1,false,success,success?"Terminé":"Refus ou erreur",id,call.Item2,message.Content);
    }
}
