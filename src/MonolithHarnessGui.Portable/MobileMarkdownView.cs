using MonolithHarness.Core;
using MonolithHarness.Presentation;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Text;
using MdBlock=Markdig.Syntax.Block;
using MdInline=Markdig.Syntax.Inlines.Inline;
namespace MonolithHarnessGui.Portable;

/// <summary>Native Markdown and media previews. Interactive HTML opens only on user action.</summary>
sealed class MobileMarkdownView : UserControl
{
    static readonly MarkdownPipeline pipeline=new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    readonly StackPanel blocks=new() {Spacing=10};
    readonly DispatcherTimer timer=new() {Interval=TimeSpan.FromMilliseconds(180)};
    readonly Brush ink;
    readonly double size;
    readonly bool media,allowNetwork;
    readonly Action<ReplyWebDocument>? openWeb;
    int imageCount;
    readonly List<string> keys=[];
    string text="",renderText="";
    public string Text {get=>text;set {if(text==value)return;text=value;timer.Start();}}
    public MobileMarkdownView(string value,double fontSize,Brush foreground,bool media=false,bool allowNetwork=true,Action<ReplyWebDocument>? openWeb=null)
    {
        size=fontSize;ink=foreground;text=value;this.media=media;this.allowNetwork=allowNetwork;this.openWeb=openWeb;Content=blocks;
        timer.Tick+=(_,_)=>Flush();Unloaded+=(_,_)=>timer.Stop();Loaded+=(_,_)=>Flush();Flush();
    }
    static Brush Role(string name)=>Application.Current.Resources.TryGetValue(name,out var value) && value is Brush brush?brush:new SolidColorBrush(Microsoft.UI.Colors.Gray);
    TextBlock Plain(string value,double? fontSize=null)=>new() {Text=value,FontSize=fontSize??size,Foreground=ink,IsTextSelectionEnabled=true,TextWrapping=TextWrapping.Wrap};
    static string Slice(string source,int start,int end)
    {var first=Math.Clamp(start,0,source.Length);var last=(int)Math.Clamp((long)end+1,first,source.Length);return source.Substring(first,last-first);}
    public void Flush()
    {
        timer.Stop();
        try {
            renderText=media?ReplyWebDocument.PrepareMarkdown(text):text;
            var document=Markdown.Parse(renderText,pipeline);int index=0;imageCount=0;
            var cssKey=media && document.OfType<FencedCodeBlock>().Any(b=>ReplyWebDocument.IsHtml(b.Info))?ReplyWebDocument.ExtraCss(renderText):"";
            foreach(var block in document) {
                var key=Slice(renderText,block.Span.Start,block.Span.End)+(media && block is FencedCodeBlock fence && ReplyWebDocument.IsHtml(fence.Info)?"\0"+cssKey:"");
                if(index>=keys.Count || keys[index]!=key) {
                    var element=Render(block);
                    if(index<blocks.Children.Count)blocks.Children[index]=element;else blocks.Children.Add(element);
                    if(index<keys.Count)keys[index]=key;else keys.Add(key);
                }
                index++;
            }
            while(blocks.Children.Count>index)blocks.Children.RemoveAt(blocks.Children.Count-1);
            if(keys.Count>index)keys.RemoveRange(index,keys.Count-index);
        } catch(Exception ex) when(ex is not OutOfMemoryException) {
            // A partial streaming block must never interrupt model generation.
            keys.Clear();blocks.Children.Clear();blocks.Children.Add(Plain(text));
        }
    }
    FrameworkElement Render(MdBlock block)
    {
        switch(block) {
            case HeadingBlock heading:
                var title=InlineText(heading.Inline,Math.Max(size,size+12-heading.Level*2));title.FontWeight=FontWeights.SemiBold;return title;
            case ParagraphBlock paragraph:return Paragraph(paragraph);
            case HtmlBlock html when media:
                var htmlSource=html.Lines.ToString();
                return ReplyWebDocument.IsEnvelopeFragment(htmlSource)?new Border():Html(ReplyWebDocument.FromHtml(htmlSource));
            case CodeBlock code:return Code(code);
            case QuoteBlock quote:
                return new Border {Child=Container(quote),Padding=new(12,4,0,4),BorderThickness=new(3,0,0,0),BorderBrush=Role("PrimaryBrush")};
            case ListBlock list:
                var items=new StackPanel {Spacing=6};int number=int.TryParse(list.OrderedStart,out var start)?start:1;
                foreach(var item in list.OfType<ListItemBlock>()) {
                    var row=new Grid {ColumnSpacing=8};row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
                    row.Children.Add(Plain(list.IsOrdered?$"{number++}.":"•"));var body=Container(item);Grid.SetColumn(body,1);row.Children.Add(body);items.Children.Add(row);
                }
                return items;
            case Table table:return TableView(table);
            case ThematicBreakBlock:return new Border {Height=1,Background=Role("OutlineVariantBrush"),Margin=new(0,4,0,4)};
            case ContainerBlock container:return Container(container);
            default:return Plain(Slice(renderText,block.Span.Start,block.Span.End));
        }
    }
    FrameworkElement Paragraph(ParagraphBlock paragraph)
    {
        var panel=new StackPanel {Spacing=8};panel.Children.Add(InlineText(paragraph.Inline));
        if(media && paragraph.Inline!=null)foreach(var link in Images(paragraph.Inline)) {
            if(imageCount++>=12)break;
            panel.Children.Add(new ReplyImageView(link.Url??"",link.Title??PlainInline(link),ink,allowNetwork));
        }
        return panel;
    }
    static string PlainInline(ContainerInline inline)=>string.Concat(inline.OfType<LiteralInline>().Select(x=>x.Content.ToString()));
    static IEnumerable<LinkInline> Images(ContainerInline inline)
    {foreach(var child in inline) {if(child is LinkInline {IsImage:true} image)yield return image;else if(child is ContainerInline nested)foreach(var nestedImage in Images(nested))yield return nestedImage;}}
    FrameworkElement Html(ReplyWebDocument artifact)
    {
        var panel=new StackPanel {Spacing=12};panel.Children.Add(Plain(artifact.Title,18));
        panel.Children.Add(new ScrollViewer {Content=ReplyHtmlView.Build(artifact,size,ink,Role("SurfaceBrush"),Role("OutlineVariantBrush"),Role("PrimaryBrush"),allowNetwork),MaxHeight=240,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollMode=ScrollMode.Disabled});
        if(openWeb!=null) {
            var open=new Button {Content="Ouvrir en plein écran",MinHeight=48,CornerRadius=new(24),Background=Role("PrimaryBrush"),Foreground=Role("OnPrimaryBrush"),HorizontalAlignment=HorizontalAlignment.Stretch};
            open.Click+=(_,_)=>openWeb(artifact);panel.Children.Add(open);
        }
        return new Border {Child=panel,Padding=new(12),CornerRadius=new(16),BorderBrush=Role("OutlineVariantBrush"),BorderThickness=new(1)};
    }
    StackPanel Container(ContainerBlock container)
    {var panel=new StackPanel {Spacing=8};foreach(var child in container)panel.Children.Add(Render(child));return panel;}
    TextBlock InlineText(ContainerInline? content,double? fontSize=null)
    {var label=Plain("",fontSize);if(content!=null)foreach(var inline in content)label.Inlines.Add(Inline(inline));return label;}
    Microsoft.UI.Xaml.Documents.Inline Inline(MdInline inline)
    {
        switch(inline) {
            case LiteralInline literal:return new Run {Text=literal.Content.ToString()};
            case LineBreakInline:return new LineBreak();
            case CodeInline code:return new Run {Text=code.Content,FontFamily=new("monospace"),Foreground=Role("PrimaryBrush")};
            case EmphasisInline emphasis:
                var emphasisSpan=new Span();
                if(emphasis.DelimiterChar=='~')emphasisSpan.TextDecorations=TextDecorations.Strikethrough;
                else if(emphasis.DelimiterCount>=2)emphasisSpan.FontWeight=FontWeights.Bold;
                else emphasisSpan.FontStyle=FontStyle.Italic;
                foreach(var child in emphasis)emphasisSpan.Inlines.Add(Inline(child));return emphasisSpan;
            case LinkInline {IsImage:true} image when media:return new Run {Text=""};
            case LinkInline link:
                var linked=new Span();foreach(var child in link)linked.Inlines.Add(Inline(child));
                if(SafeLink(link.Url) is {} uri) {
                    var anchor=new Hyperlink {NavigateUri=uri,Foreground=Role("PrimaryBrush")};
                    if(link.IsImage)anchor.Inlines.Add(new Run {Text="Image : "});
                    foreach(var child in link)anchor.Inlines.Add(Inline(child));return anchor;
                }
                return linked;
            case AutolinkInline autolink:
                var url=autolink.IsEmail?"mailto:"+autolink.Url:autolink.Url;
                if(SafeLink(url) is {} target) {var anchor=new Hyperlink {NavigateUri=target,Foreground=Role("PrimaryBrush")};anchor.Inlines.Add(new Run {Text=autolink.Url});return anchor;}
                return new Run {Text=autolink.Url};
            case TaskList task:return new Run {Text=task.Checked?"☑ ":"☐ "};
            case HtmlInline html:return html.Tag is "<br>" or "<br/>" or "<br />"?new LineBreak():new Run {Text=html.Tag};
            case ContainerInline container:
                var span=new Span();foreach(var child in container)span.Inlines.Add(Inline(child));return span;
            default:return new Run {Text=Slice(renderText,inline.Span.Start,inline.Span.End)};
        }
    }
    static Uri? SafeLink(string? url)=>Uri.TryCreate(url,UriKind.Absolute,out var uri) && uri.Scheme is "https" or "http" or "mailto"?uri:null;
    FrameworkElement Code(CodeBlock code)
    {
        var value=code.Lines.ToString();var language=code is FencedCodeBlock fence?fence.Info??"":"";
        var panel=new StackPanel {Spacing=8};var header=new Grid();header.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var caption=Plain(language.Length==0?"Code":language,12);caption.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(caption);
        var copy=new Button {Content="Copier",MinHeight=48,CornerRadius=new(20),Padding=new(12,4,12,4)};Grid.SetColumn(copy,1);header.Children.Add(copy);
        copy.Click+=(_,_)=>{var package=new DataPackage();package.SetText(value);Clipboard.SetContent(package);};panel.Children.Add(header);
        var source=Plain(value,Math.Max(13,size-2));source.FontFamily=new("monospace");source.TextWrapping=TextWrapping.NoWrap;
        panel.Children.Add(new ScrollViewer {Content=source,HorizontalScrollMode=ScrollMode.Enabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollMode=ScrollMode.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled});
        var sourceCard=new Border {Child=panel,CornerRadius=new(16),Padding=new(12),BorderBrush=Role("OutlineVariantBrush"),BorderThickness=new(1)};
        if(media && code is FencedCodeBlock {ClosingFencedCharCount:>0} && ReplyWebDocument.IsHtml(language) && value.Length<=ReplyWebDocument.MaxCharacters) {
            var artifact=ReplyWebDocument.Extract(renderText).FirstOrDefault(x=>x.Html.StartsWith(value,StringComparison.Ordinal))??ReplyWebDocument.FromHtml(value);
            var preview=new StackPanel {Spacing=8};preview.Children.Add(Html(artifact));preview.Children.Add(new Expander {Header="Voir le code HTML",Content=sourceCard,HorizontalAlignment=HorizontalAlignment.Stretch});return preview;
        }
        return sourceCard;
    }
    FrameworkElement TableView(Table table)
    {
        var grid=new Grid();var rows=table.OfType<TableRow>().ToArray();var count=rows.Select(row=>row.OfType<TableCell>().Count()).DefaultIfEmpty().Max();
        for(int i=0;i<count;i++)grid.ColumnDefinitions.Add(new(){Width=new(152)});
        for(int row=0;row<rows.Length;row++) {
            grid.RowDefinitions.Add(new(){Height=GridLength.Auto});int column=0;
            foreach(var cell in rows[row].OfType<TableCell>()) {
                var content=Container(cell);if(rows[row].IsHeader)foreach(var label in content.Children.OfType<TextBlock>())label.FontWeight=FontWeights.Bold;
                var border=new Border {Child=content,Padding=new(10),BorderThickness=new(0,0,0,1),BorderBrush=Role("OutlineVariantBrush")};Grid.SetRow(border,row);Grid.SetColumn(border,column++);grid.Children.Add(border);
            }
        }
        return new ScrollViewer {Content=grid,HorizontalScrollMode=ScrollMode.Enabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollMode=ScrollMode.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};
    }
}
