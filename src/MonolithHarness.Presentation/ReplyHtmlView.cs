using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using Windows.UI.Text;
namespace MonolithHarness.Presentation;

/// <summary>Native HTML/CSS subset, without a browser, scripts or external stylesheets.</summary>
internal static class ReplyHtmlView
{
    static readonly Regex rules=new(@"(?<selector>[^{}]+)\{(?<style>[^{}]*)\}",RegexOptions.None,TimeSpan.FromMilliseconds(200));
    static readonly Regex whitespace=new(@"\s+",RegexOptions.None,TimeSpan.FromMilliseconds(200));
    internal static FrameworkElement Build(ReplyWebDocument artifact,double size,Brush ink,Brush surface,Brush stroke,Brush accent,bool allowNetwork=true,Func<string,CancellationToken,Task<byte[]?>>? localImage=null)
    {
        using var document=new HtmlParser().ParseDocument(artifact.Html);
        var css=new Dictionary<IElement,Dictionary<string,string>>();
        foreach(var sheet in document.QuerySelectorAll("style").Take(16)) {
            foreach(Match rule in rules.Matches(sheet.TextContent.Length>40_000?sheet.TextContent[..40_000]:sheet.TextContent).Cast<Match>().Take(96)) {
                var selector=rule.Groups["selector"].Value.Trim();if(selector.Contains('@') || selector.Length>256)continue;
                try {foreach(var element in document.QuerySelectorAll(selector).Take(512)) {if(!css.TryGetValue(element,out var style))css[element]=style=[];Declarations(rule.Groups["style"].Value,style);}}
                catch(Exception ex) when(ex is not OutOfMemoryException) { }
            }
        }
        var count=0;
        FrameworkElement? Node(INode node,Brush inheritedInk,double font,int depth,bool pre=false)
        {
            if(++count>1024 || depth>32)return null;
            if(node is IText text) {var value=pre?text.Data:whitespace.Replace(text.Data," ");return string.IsNullOrWhiteSpace(value)?null:Label(value,inheritedInk,font,pre);}
            if(node is not IElement element || element.TagName is "SCRIPT" or "STYLE" or "META" or "LINK" or "BASE" or "IFRAME" or "OBJECT" or "EMBED" or "TEMPLATE")return null;
            if(!css.TryGetValue(element,out var style))style=[];Declarations(element.GetAttribute("style")??"",style);
            string Get(string name)=>style.GetValueOrDefault(name,"").Trim().ToLowerInvariant();
            if(Get("display")=="none" || Get("visibility")=="hidden")return null;
            var foreground=Color(Get("color"))??inheritedInk;
            var fontSize=Length(Get("font-size"),font) is {} n?Math.Clamp(n,10,48):font;
            var heading=element.TagName.Length==2 && element.TagName[0]=='H' && char.IsDigit(element.TagName[1]);
            if(heading && Get("font-size")=="")fontSize=Math.Max(font,30-(element.TagName[1]-'1')*2);
            if(element.TagName=="IMG")return new ReplyImageView(element.GetAttribute("src")??"",element.GetAttribute("alt")??"Image",foreground,allowNetwork,localImage);
            if(element.TagName=="BR")return new Border {Height=4};
            if(element.TagName=="HR")return new Border {Height=1,Background=stroke,Margin=new(0,6,0,6)};
            FrameworkElement content;
            var blockChildren=element.Children.Any(x=>x.TagName is "DIV" or "P" or "SECTION" or "ARTICLE" or "UL" or "OL" or "LI" or "TABLE" or "IMG" or "PRE" or "MAIN" || x.TagName.StartsWith('H') && x.TagName.Length==2);
            if(!blockChildren) {
                var label=Label("",foreground,fontSize,pre || element.TagName=="PRE");
                void Inlines(INode child,InlineCollection target,bool bold=false,bool italic=false,bool mono=false,int level=0) {
                    if(level>32 || ++count>1024)return;
                    if(child is IText t) {target.Add(new Run {Text=pre || mono?t.Data:whitespace.Replace(t.Data," "),FontWeight=bold?FontWeights.Bold:FontWeights.Normal,FontStyle=italic?FontStyle.Italic:FontStyle.Normal,FontFamily=mono?new FontFamily("monospace"):label.FontFamily});return;}
                    if(child is not IElement e || e.TagName is "SCRIPT" or "STYLE" or "IFRAME" or "OBJECT" or "META" or "LINK")return;
                    if(e.TagName=="BR") {target.Add(new LineBreak());return;}
                    if(e.TagName=="IMG") {target.Add(new Run {Text=" ["+(e.GetAttribute("alt")??"Image")+"] "});return;}
                    var span=new Span();
                    if(e.TagName=="A" && Uri.TryCreate(e.GetAttribute("href"),UriKind.Absolute,out var uri) && uri.Scheme is "https" or "http")span=new Hyperlink {NavigateUri=uri,Foreground=accent};
                    if(!css.TryGetValue(e,out var inlineStyle))inlineStyle=[];Declarations(e.GetAttribute("style")??"",inlineStyle);
                    if(inlineStyle.GetValueOrDefault("display","")=="none")return;
                    if(Color(inlineStyle.GetValueOrDefault("color","")) is {} color)span.Foreground=color;
                    if(Length(inlineStyle.GetValueOrDefault("font-size",""),fontSize) is {} inlineSize)span.FontSize=Math.Clamp(inlineSize,10,48);
                    if(inlineStyle.GetValueOrDefault("font-weight","") is "bold" or "600" or "700")span.FontWeight=FontWeights.Bold;
                    foreach(var item in e.ChildNodes)Inlines(item,span.Inlines,bold || e.TagName is "B" or "STRONG",italic || e.TagName is "I" or "EM",mono || e.TagName is "CODE" or "PRE",level+1);target.Add(span);
                }
                foreach(var child in element.ChildNodes)Inlines(child,label.Inlines,heading || Get("font-weight") is "bold" or "600" or "700",Get("font-style")=="italic",element.TagName is "CODE" or "PRE");
                if(element.TagName=="LI")label.Inlines.Insert(0,new Run {Text="• "});
                if(Get("text-align")=="center")label.TextAlignment=TextAlignment.Center;else if(Get("text-align")=="right")label.TextAlignment=TextAlignment.Right;
                content=label;
            } else {
                var panel=new StackPanel {Spacing=6};
                foreach(var child in element.ChildNodes)if(Node(child,foreground,fontSize,depth+1,pre || element.TagName=="PRE") is {} rendered)panel.Children.Add(rendered);
                content=panel;
            }
            var padding=Length(Get("padding"),fontSize)??0;var margin=Length(Get("margin"),fontSize)??0;
            var radius=Length(Get("border-radius"),fontSize)??0;
            var background=Color(Get("background-color"))??Color(Get("background"));
            var borderWidth=Length(Get("border-width"),fontSize)??(Get("border").Length>0?1:0);
            if(element.TagName=="BUTTON") {background??=surface;borderWidth=Math.Max(borderWidth,1);padding=Math.Max(padding,10);radius=Math.Max(radius,12);}
            return new Border {Child=content,Background=background,BorderBrush=Color(Get("border-color"))??stroke,BorderThickness=new(Math.Clamp(borderWidth,0,4)),
                Padding=new(Math.Clamp(padding,0,32)),Margin=new(Math.Clamp(margin,0,24)),CornerRadius=new(Math.Clamp(radius,0,40))};
        }
        var body=Node(document.Body!,ink,size,0);
        return body??Label("Aucun contenu HTML affichable en rendu natif.",ink,size,false);
    }
    static TextBlock Label(string text,Brush ink,double size,bool pre)=>new() {Text=text,Foreground=ink,FontSize=size,IsTextSelectionEnabled=true,TextWrapping=TextWrapping.Wrap,FontFamily=pre?new("monospace"):new("Segoe UI, Roboto, sans-serif")};
    static void Declarations(string value,Dictionary<string,string> target)
    {foreach(var entry in value.Split(';').Take(100)) {var colon=entry.IndexOf(':');if(colon>0)target[entry[..colon].Trim().ToLowerInvariant()]=entry[(colon+1)..].Trim();}}
    static double? Length(string value,double size)
    {
        var multiplier=1d;if(value.EndsWith("px"))value=value[..^2];else if(value.EndsWith("rem")){value=value[..^3];multiplier=16;}else if(value.EndsWith("em")){value=value[..^2];multiplier=size;}
        return double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)?n*multiplier:null;
    }
    static Brush? Color(string value)
    {
        value=value.Trim().ToLowerInvariant();
        var names=new Dictionary<string,string> { ["white"]="#ffffff",["black"]="#000000",["red"]="#ef4444",["green"]="#22c55e",["blue"]="#3b82f6",["orange"]="#f97316",["gray"]="#808080",["grey"]="#808080",["transparent"]="#00000000" };
        if(names.TryGetValue(value,out var hex))value=hex;
        if(value.StartsWith('#')) {
            value=value[1..];if(value.Length==3)value=string.Concat(value.Select(c=>new string(c,2)));
            if(value.Length==6 && uint.TryParse(value,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var rgb))return new SolidColorBrush(Windows.UI.Color.FromArgb(255,(byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb));
            if(value.Length==8 && uint.TryParse(value,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var rgba))return new SolidColorBrush(Windows.UI.Color.FromArgb((byte)rgba,(byte)(rgba>>24),(byte)(rgba>>16),(byte)(rgba>>8)));
        }
        var match=Regex.Match(value,@"^rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)$",RegexOptions.None,TimeSpan.FromMilliseconds(100));
        if(match.Success && int.TryParse(match.Groups[1].Value,out var red) && int.TryParse(match.Groups[2].Value,out var green) && int.TryParse(match.Groups[3].Value,out var blue))return new SolidColorBrush(Windows.UI.Color.FromArgb(255,(byte)Math.Clamp(red,0,255),(byte)Math.Clamp(green,0,255),(byte)Math.Clamp(blue,0,255)));
        return null;
    }
}
