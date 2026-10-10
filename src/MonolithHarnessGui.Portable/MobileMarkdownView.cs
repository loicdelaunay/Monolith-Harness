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

/// <summary>Native, theme-aware Markdown. No browser/WebView or automatic remote image requests.</summary>
sealed class MobileMarkdownView : UserControl
{
    static readonly MarkdownPipeline pipeline=new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    readonly StackPanel blocks=new() {Spacing=10};
    readonly DispatcherTimer timer=new() {Interval=TimeSpan.FromMilliseconds(180)};
    readonly Brush ink;
    readonly double size;
    readonly List<string> keys=[];
    string text="";
    public string Text {get=>text;set {if(text==value)return;text=value;timer.Start();}}
    public MobileMarkdownView(string value,double fontSize,Brush foreground)
    {
        size=fontSize;ink=foreground;text=value;Content=blocks;
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
            var document=Markdown.Parse(text,pipeline);int index=0;
            foreach(var block in document) {
                var key=Slice(text,block.Span.Start,block.Span.End);
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
            case ParagraphBlock paragraph:return InlineText(paragraph.Inline);
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
            default:return Plain(Slice(text,block.Span.Start,block.Span.End));
        }
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
            default:return new Run {Text=Slice(text,inline.Span.Start,inline.Span.End)};
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
        return new Border {Child=panel,CornerRadius=new(16),Padding=new(12),BorderBrush=Role("OutlineVariantBrush"),BorderThickness=new(1)};
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
