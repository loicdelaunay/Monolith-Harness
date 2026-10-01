using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Text;
using static MonolithHarness.App.UiText;

using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace MonolithHarness.App;

public sealed class MarkdownRenderer
{
    private SolidColorBrush Brush(byte r, byte g, byte b) => FluentDesign.Adapt(r,g,b);

    readonly Func<string, Task>? openFile;
    readonly Func<string, MenuFlyout>? fileMenu;
    sealed class RenderState
    {
        public List<string> Keys = [];
        public string Density = "", Markdown = "";
        public Func<string, Task>? OpenFile;
        public Func<string, MenuFlyout>? FileMenu;
    }
    sealed class TextFlowState
    {
        public List<string> Keys = [];
        public List<int> InlineEnds = [];
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock, TextFlowState> textFlows = new();
    static readonly List<WeakReference<Panel>> renderedPanels = [];
    public static void RefreshDensity()
    {
        renderedPanels.RemoveAll(x => !x.TryGetTarget(out _));
        foreach (var reference in renderedPanels.ToArray())
            if (reference.TryGetTarget(out var panel) && states.TryGetValue(panel, out var state))
                RenderTo(panel, state.Markdown, state.OpenFile, state.FileMenu);
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Panel, RenderState> states = new();
    MarkdownRenderer(Func<string, Task>? openFile, Func<string, MenuFlyout>? fileMenu = null) { this.openFile = openFile; this.fileMenu = fileMenu; }
    public static void RenderTo(Panel container, string? markdown, Func<string, Task>? openFile = null, Func<string, MenuFlyout>? fileMenu = null)
    {
        markdown ??= "";
        if (!states.TryGetValue(container, out var state))
        { state = new(); states.Add(container, state); renderedPanels.Add(new(container)); }
        state.Markdown = markdown; state.OpenFile = openFile; state.FileMenu = fileMenu;
        var blocks = MarkdownPipelineHelper.Parse(markdown);
        var groups = GroupTextBlocks(blocks).ToList();
        var keys = groups.Select(group => markdown.Substring(group[0].Span.Start,
            group[^1].Span.End - group[0].Span.Start + 1)).ToList();
        int shared = 0;
        while (state.Density == ChatDensity.Id && shared < keys.Count && shared < state.Keys.Count && shared < container.Children.Count && keys[shared] == state.Keys[shared]) shared++;
        // Adjacent headings, paragraphs, lists and quotes share one selection surface.
        // Reuse that surface and its completed inlines when the streamed tail changes.
        var replacements = new List<UIElement>();
        var renderer = new MarkdownRenderer(openFile, fileMenu);
        for (int index = shared; index < groups.Count; index++)
        {
            var group = groups[index];
            StackPanel blockPanel;
            if (CanJoinText(group[0]))
            {
                TextBlock flow;
                if (state.Density == ChatDensity.Id && index < container.Children.Count &&
                    container.Children[index] is StackPanel existing && existing.Children.Count == 1 &&
                    existing.Children[0] is TextBlock existingFlow && textFlows.TryGetValue(existingFlow, out _))
                { blockPanel = existing; flow = existingFlow; }
                else
                {
                    blockPanel = new StackPanel { Spacing = 4 };
                    flow = new TextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap,
                        Foreground = FluentDesign.Primary, FontSize = ChatDensity.FontSize, LineHeight = ChatDensity.LineHeight };
                    blockPanel.Children.Add(flow);
                }
                renderer.UpdateTextFlow(flow, group, markdown);
            }
            else
            {
                blockPanel = new StackPanel { Spacing = 4 };
                if (renderer.RenderBlock(group[0]) is { } element) blockPanel.Children.Add(element);
            }
            if (fileMenu != null) ChatPathMenus.Attach(blockPanel, fileMenu);
            replacements.Add(blockPanel);
        }
        for (int i = 0; i < replacements.Count; i++)
        {
            int index = shared + i;
            if (index < container.Children.Count)
            {
                if (!ReferenceEquals(container.Children[index], replacements[i])) container.Children[index] = replacements[i];
            }
            else container.Children.Add(replacements[i]);
        }
        while (container.Children.Count > keys.Count) container.Children.RemoveAt(container.Children.Count - 1);
        state.Keys = keys; state.Density = ChatDensity.Id;
        AppTypography.Apply(container);
    }
    static IEnumerable<MdBlock[]> GroupTextBlocks(IEnumerable<MdBlock> blocks)
    {
        var text = new List<MdBlock>();
        foreach (var block in blocks)
        {
            if (CanJoinText(block)) { text.Add(block); continue; }
            if (text.Count > 0) { yield return text.ToArray(); text.Clear(); }
            yield return [block];
        }
        if (text.Count > 0) yield return text.ToArray();
    }
    void UpdateTextFlow(TextBlock flow, IReadOnlyList<MdBlock> blocks, string markdown)
    {
        var state = textFlows.GetOrCreateValue(flow);
        var keys = blocks.Select(block => markdown.Substring(block.Span.Start, block.Span.Length)).ToList();
        int shared = 0;
        while (shared < keys.Count && shared < state.Keys.Count && keys[shared] == state.Keys[shared]) shared++;
        int keep = shared > 0 ? state.InlineEnds[shared - 1] : 0;
        while (flow.Inlines.Count > keep) flow.Inlines.RemoveAt(flow.Inlines.Count - 1);
        if (state.InlineEnds.Count > shared) state.InlineEnds.RemoveRange(shared, state.InlineEnds.Count - shared);
        foreach (var block in blocks.Skip(shared))
        {
            AppendText(block, flow, 0);
            state.InlineEnds.Add(flow.Inlines.Count);
        }
        state.Keys = keys;
    }
    /// <summary>Paginated file rendering with cancellation and UI yields between blocks.</summary>
    public static async Task RenderPreviewAsync(Panel target, string markdown, Func<string, Task> openFile,
        Func<string, MenuFlyout> fileMenu, Func<string, Task<FrameworkElement?>> imagePreview, CancellationToken ct)
    {
        var blocks = await Task.Run(() => MarkdownPipelineHelper.Parse(markdown), ct);
        ct.ThrowIfCancellationRequested();
        var renderer = new MarkdownRenderer(openFile, fileMenu);
        int imageCount = 0, blockCount = 0;
        foreach (var block in blocks)
        {
            ct.ThrowIfCancellationRequested();
            if (blockCount >= 400)
            {
                target.Children.Add(new TextBlock { Text = T("La suite de cette page est disponible dans Raw."), Foreground = FluentDesign.Secondary, TextWrapping = TextWrapping.Wrap });
                break;
            }
            var nodes = PreviewNodes(block).Take(1201).ToArray();
            var panel = new StackPanel { Spacing = 6 };
            // Avoid thousands of native controls for pathological tables or highlighted code.
            if (nodes.Length > 1200 || block is FencedCodeBlock && block.Span.Length > 8000)
                panel.Children.Add(new TextBlock { Text = markdown.Substring(block.Span.Start, block.Span.Length), IsTextSelectionEnabled = true,
                    FontFamily = new FontFamily("Cascadia Code, Consolas"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Primary });
            else if (CanJoinText(block))
            {
                var text = new TextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap,
                    Foreground = FluentDesign.Primary, FontSize = ChatDensity.FontSize, LineHeight = ChatDensity.LineHeight };
                renderer.AppendText(block, text, 0); panel.Children.Add(text);
            }
            else if (renderer.RenderBlock(block) is { } element) panel.Children.Add(element);
            ChatPathMenus.Attach(panel, fileMenu);
            target.Children.Add(panel);
            foreach (var link in nodes.OfType<LinkInline>().Where(x => x.IsImage && !string.IsNullOrWhiteSpace(x.Url)))
            {
                if (imageCount >= 12) break;
                imageCount++;
                var image = await imagePreview(link.Url!);
                ct.ThrowIfCancellationRequested();
                if (image != null) panel.Children.Add(image);
            }
            if (++blockCount % 8 == 0) await Task.Delay(1, ct);
        }
        ct.ThrowIfCancellationRequested();
        AppTypography.Apply(target);
    }
    static IEnumerable<Markdig.Syntax.MarkdownObject> PreviewNodes(Markdig.Syntax.MarkdownObject root)
    {
        var pending = new Stack<Markdig.Syntax.MarkdownObject>(); pending.Push(root);
        while (pending.TryPop(out var node))
        {
            yield return node;
            if (node is ContainerBlock blocks)
                foreach (var child in blocks.Reverse()) pending.Push(child);
            else if (node is LeafBlock { Inline: { } inline }) pending.Push(inline);
            else if (node is ContainerInline inlines)
                foreach (var child in inlines.Reverse()) pending.Push(child);
        }
    }

    public static void RenderPathText(TextBlock target, string text, Func<string, Task> openFile, Func<string, MenuFlyout> fileMenu)
    {
        var renderer = new MarkdownRenderer(openFile, fileMenu);
        target.IsTextSelectionEnabled = true;
        target.Inlines.Clear(); target.Text = "";
        int offset = 0;
        foreach (var match in LocalFileLinks.Find(text))
        {
            if (match.Index > offset) target.Inlines.Add(new Run { Text = text[offset..match.Index] });
            var link = new Hyperlink { Foreground = renderer.Brush(120, 175, 255), UnderlineStyle = UnderlineStyle.Single };
            link.Inlines.Add(new Run { Text = text.Substring(match.Index, match.Length) });
            ChatPathMenus.Register(link, match.Path);
            link.Click += async (_, _) => { if (!ChatPathMenus.IsSecondaryPress(link)) await openFile(match.Path); };
            target.Inlines.Add(link);
            offset = match.Index + match.Length;
        }
        if (offset < text.Length) target.Inlines.Add(new Run { Text = text[offset..] });
        ChatPathMenus.Attach(target, fileMenu);
    }
    void Render(Panel container, string? markdown)
    {
        container.Children.Clear();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        var doc = MarkdownPipelineHelper.Parse(markdown);
        if (doc.Count == 0)
        {
            var fallback = new TextBlock
            {
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
                Foreground = FluentDesign.Primary,
                FontSize = 14.5
            };
            var p = new Paragraph();
            p.Inlines.Add(new Run { Text = markdown });
            foreach(var inline in p.Inlines.ToList()) { p.Inlines.Remove(inline); fallback.Inlines.Add(inline); }
            container.Children.Add(fallback);
            return;
        }

        TextBlock? textFlow = null;
        foreach (var block in doc)
        {
            if (CanJoinText(block))
            {
                if (textFlow == null)
                {
                    textFlow = new TextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap,
                        Foreground = FluentDesign.Primary, FontSize = ChatDensity.FontSize, LineHeight = ChatDensity.LineHeight };
                    container.Children.Add(textFlow);
                }
                AppendText(block, textFlow, 0);
                continue;
            }
            textFlow = null;
            var elem = RenderBlock(block);
            if (elem != null)
            {
                container.Children.Add(elem);
            }
        }
    }

    static bool CanJoinText(MdBlock block) => block is ParagraphBlock or HeadingBlock ||
        block is ListBlock or ListItemBlock or QuoteBlock && ((ContainerBlock)block).All(CanJoinText);

    void AppendText(MdBlock block, TextBlock flow, int depth, string prefix = "")
    {
        if (block is ParagraphBlock paragraph || block is HeadingBlock)
        {
            var p = new Span();
            if (flow.Inlines.Count > 0)
            {
                flow.Inlines.Add(new LineBreak());
            }
            if(depth > 0) p.Inlines.Add(new Run { Text = new string(' ', depth * 2) });
            if (prefix.Length > 0) p.Inlines.Add(new Run { Text = prefix, Foreground = Brush(130, 175, 245) });
            if (block is HeadingBlock heading)
            {
                p.FontSize = heading.Level switch { 1 => 21, 2 => 18, 3 => 16, _ => 14.5 };
                p.FontWeight = FontWeights.SemiBold;

                if (heading.Inline != null) RenderInlines(heading.Inline, p.Inlines);
            }
            else if (((ParagraphBlock)block).Inline is { } inline) RenderInlines(inline, p.Inlines);
            flow.Inlines.Add(p);
        }
        else if (block is ListBlock list)
        {
            int number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
            foreach (var item in list.OfType<ListItemBlock>())
            {
                bool first = true;
                foreach (var child in item)
                {
                    AppendText(child, flow, depth + 1, first ? (list.IsOrdered ? $"{number}. " : "• ") : "");
                    first = false;
                }
                number++;
            }
        }
        else if (block is ContainerBlock container)
            foreach (var child in container) AppendText(child, flow, depth + (block is QuoteBlock ? 1 : 0), block is QuoteBlock ? "│ " : prefix);
    }

    public FrameworkElement? RenderBlock(MdBlock block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                return RenderHeading(heading);

            case ParagraphBlock paragraph:
                return RenderParagraph(paragraph);

            case FencedCodeBlock fenced:
                return RenderFencedCode(fenced);

            case CodeBlock code:
                return RenderCodeBlock(code);

            case QuoteBlock quote:
                return RenderQuote(quote);

            case ListBlock list:
                return RenderList(list);

            case ThematicBreakBlock:
                return RenderThematicBreak();

            case Table table:
                return RenderTable(table);

            case ContainerBlock container:
                var panel = new StackPanel { Spacing = 4 };
                foreach (var child in container)
                {
                    var childElem = RenderBlock(child);
                    if (childElem != null) panel.Children.Add(childElem);
                }
                return panel;

            default:
                return null;
        }
    }

    private FrameworkElement RenderHeading(HeadingBlock heading)
    {
        var (fontSize, weight, margin) = heading.Level switch
        {
            1 => (21.0, FontWeights.Bold, new Thickness(0, 10, 0, 4)),
            2 => (18.0, FontWeights.SemiBold, new Thickness(0, 8, 0, 4)),
            3 => (16.0, FontWeights.SemiBold, new Thickness(0, 6, 0, 3)),
            4 => (14.5, FontWeights.SemiBold, new Thickness(0, 4, 0, 2)),
            _ => (13.5, FontWeights.SemiBold, new Thickness(0, 3, 0, 2))
        };

        var rtb = new TextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = FluentDesign.Primary,
            FontSize = fontSize,
            FontWeight = weight,
            Margin = margin
        };

        var p = new Paragraph();
        if (heading.Inline != null)
        {
            RenderInlines(heading.Inline, p.Inlines);
        }
        foreach(var inline in p.Inlines.ToList()) { p.Inlines.Remove(inline); rtb.Inlines.Add(inline); }
        return rtb;
    }

    private FrameworkElement RenderParagraph(ParagraphBlock paragraph)
    {
        var rtb = new TextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = FluentDesign.Primary,
            FontSize = ChatDensity.FontSize,
            LineHeight = ChatDensity.LineHeight,
            Margin = new Thickness(0, 2, 0, 4)
        };

        var p = new Paragraph();
        if (paragraph.Inline != null)
        {
            RenderInlines(paragraph.Inline, p.Inlines);
        }
        foreach(var inline in p.Inlines.ToList()) { p.Inlines.Remove(inline); rtb.Inlines.Add(inline); }
        return rtb;
    }

    private FrameworkElement RenderFencedCode(FencedCodeBlock fenced)
    {
        var rawCode = ExtractCode(fenced.Lines);
        var lang = string.IsNullOrWhiteSpace(fenced.Info) ? "code" : fenced.Info.Trim();
        return CreateCodeBlockElement(lang, rawCode);
    }

    private FrameworkElement RenderCodeBlock(CodeBlock code)
    {
        var rawCode = ExtractCode(code.Lines);
        return CreateCodeBlockElement("code", rawCode);
    }

    private string ExtractCode(StringLineGroup lines)
    {
        return lines.ToString();
    }

    private FrameworkElement CreateCodeBlockElement(string language, string code)
    {
        var container = new Border
        {
            Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush"),
            BorderBrush = FluentDesign.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 6, 0, 8)
        };

        var stack = new StackPanel();

        // Header bar with language and copy button
        var header = new Grid
        {
            Background = FluentDesign.Card,
            Padding = new Thickness(12, 5, 8, 5)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var langLabel = new TextBlock
        {
            Text = language.ToLowerInvariant(),
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            Foreground = Brush(145, 165, 195),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(langLabel, 0);
        header.Children.Add(langLabel);

        var copyBtn = new Button
        {
            Content = "📋 " + T("Copier"),
            FontSize = 11,
            Padding = new Thickness(8, 3, 8, 3),
            Background = Brush(34, 40, 56),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Foreground = Brush(210, 220, 235)
        };
        copyBtn.Click += async (_, _) =>
        {
            try
            {
                var dp = new DataPackage();
                dp.SetText(code);
                Clipboard.SetContent(dp);
                copyBtn.Content = "✓ " + T("Copié !");
                await Task.Delay(2000);
                copyBtn.Content = "📋 " + T("Copier");
            }
            catch { }
        };
        Grid.SetColumn(copyBtn, 1);
        header.Children.Add(copyBtn);
        stack.Children.Add(header);

        // Code body
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12, 10, 12, 10)
        };
        var codeText = new TextBlock
        {

            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 12.5,
            Foreground = Brush(220, 230, 245),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.NoWrap
        };
        try
        {
            var tokens = CodeHighlight.Tokens(code, language).ToArray();
            int position = 0, tokenIndex = 0, tokenOffset = 0;
            void AppendUntil(int end, InlineCollection target)
            {
                while (position < end && tokenIndex < tokens.Length)
                {
                    var token = tokens[tokenIndex];
                    var length = Math.Min(end - position, token.Text.Length - tokenOffset);
                    var color = token.Color;
                    target.Add(new Run { Text = token.Text.Substring(tokenOffset, length), Foreground = Brush(Convert.ToByte(color.Substring(1,2),16), Convert.ToByte(color.Substring(3,2),16), Convert.ToByte(color.Substring(5,2),16)) });
                    position += length; tokenOffset += length;
                    if (tokenOffset == token.Text.Length) { tokenIndex++; tokenOffset = 0; }
                }
            }
            if (openFile != null && fileMenu != null)
                foreach (var path in LocalFileLinks.Find(code))
                {
                    AppendUntil(path.Index, codeText.Inlines);
                    var link = new Hyperlink { UnderlineStyle = UnderlineStyle.Single };
                    ChatPathMenus.Register(link, path.Path);
                    link.Click += async (_, _) => { if (!ChatPathMenus.IsSecondaryPress(link)) await openFile(path.Path); };
                    AppendUntil(path.Index + path.Length, link.Inlines);
                    codeText.Inlines.Add(link);
                }
            AppendUntil(code.Length, codeText.Inlines);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { codeText.Inlines.Clear(); codeText.Text = code; }
        scroll.Content = codeText;
        stack.Children.Add(scroll);

        container.Child = stack;
        return container;
    }

    private FrameworkElement RenderQuote(QuoteBlock quote)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = Brush(90, 140, 230),
            Background = FluentDesign.Card,
            CornerRadius = new CornerRadius(0, 6, 6, 0),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 4, 0, 6)
        };

        var stack = new StackPanel { Spacing = 4 };
        foreach (var child in quote)
        {
            var childElem = RenderBlock(child);
            if (childElem != null) stack.Children.Add(childElem);
        }
        border.Child = stack;
        return border;
    }

    private FrameworkElement RenderList(ListBlock list)
    {
        var stack = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 2, 0, 6)
        };

        int index = int.TryParse(list.OrderedStart, out var start) ? start : 1;
        foreach (var item in list)
        {
            if (item is ListItemBlock listItem)
            {
                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                string bulletText = list.IsOrdered ? $"{index}. " : "• ";
                var bullet = new TextBlock
                {
                    Text = bulletText,
                    Foreground = Brush(130, 175, 245),
                    FontSize = list.IsOrdered ? 13 : 15,
                    FontFamily = list.IsOrdered ? new FontFamily("Cascadia Code, Segoe UI") : new FontFamily("Segoe UI"),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(4, list.IsOrdered ? 1 : -1, 8, 0)
                };
                Grid.SetColumn(bullet, 0);
                rowGrid.Children.Add(bullet);

                var itemContent = new StackPanel { Spacing = 2 };
                foreach (var child in listItem)
                {
                    var childElem = RenderBlock(child);
                    if (childElem != null) itemContent.Children.Add(childElem);
                }
                Grid.SetColumn(itemContent, 1);
                rowGrid.Children.Add(itemContent);

                stack.Children.Add(rowGrid);
                index++;
            }
        }
        return stack;
    }

    private FrameworkElement RenderThematicBreak()
    {
        return new Border
        {
            Height = 1,
            Background = Brush(45, 55, 75),
            Margin = new Thickness(0, 8, 0, 8)
        };
    }

    private FrameworkElement RenderTable(Table table)
    {
        var container = new Border
        {
            Background = Brush(20, 24, 34),
            BorderBrush = Brush(45, 55, 75),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 6, 0, 8),
            Padding = new Thickness(4)
        };

        var grid = new Grid();
        int maxCols = 0;
        var rows = table.OfType<TableRow>().ToList();
        foreach (var row in rows)
        {
            var cellCount = row.OfType<TableCell>().Count();
            if (cellCount > maxCols) maxCols = cellCount;
        }

        for (int c = 0; c < maxCols; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (int r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = rows[r];
            var cells = row.OfType<TableCell>().ToList();
            bool isHeader = row.IsHeader;

            for (int c = 0; c < cells.Count && c < maxCols; c++)
            {
                var cell = cells[c];
                var cellBorder = new Border
                {
                    Background = isHeader ? Brush(30, 36, 52) : (r % 2 == 1 ? Brush(23, 27, 38) : Brush(19, 23, 32)),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(1)
                };

                var cellContent = new StackPanel();
                foreach (var block in cell)
                {
                    var elem = RenderBlock(block);
                    if (elem != null) cellContent.Children.Add(elem);
                }
                cellBorder.Child = cellContent;

                Grid.SetRow(cellBorder, r);
                Grid.SetColumn(cellBorder, c);
                grid.Children.Add(cellBorder);
            }
        }

        container.Child = grid;
        return container;
    }

    private void RenderInlines(ContainerInline inlines, InlineCollection target)
    {
        foreach (var inline in inlines)
        {
            RenderInline(inline, target);
        }
    }

    private void RenderInline(MdInline inline, InlineCollection target)
    {
        switch (inline)
        {
            case LiteralInline literal:
                target.Add(new Run { Text = literal.Content.ToString() });
                break;

            case EmphasisInline emphasis:
                if (emphasis.DelimiterChar == '~')
                {
                    var span = new Span { TextDecorations = TextDecorations.Strikethrough };
                    RenderInlines(emphasis, span.Inlines);
                    target.Add(span);
                }
                else if (emphasis.DelimiterCount >= 2)
                {
                    var bold = new Bold();
                    RenderInlines(emphasis, bold.Inlines);
                    target.Add(bold);
                }
                else
                {
                    var italic = new Italic();
                    RenderInlines(emphasis, italic.Inlines);
                    target.Add(italic);
                }
                break;

            case CodeInline code:
                target.Add(new Run
                {
                    Text = code.Content,
                    FontFamily = new FontFamily("Cascadia Code, Consolas"),
                    Foreground = Brush(240, 195, 120),
                    FontWeight = FontWeights.Medium
                });
                break;

            case LineBreakInline:
                target.Add(new LineBreak());
                break;

            case LinkInline link:
                if (link.IsImage && LocalFileLinks.PathFromUrl(link.Url) is string imagePath && openFile != null)
                {
                    var imageLink = new Hyperlink { Foreground = Brush(120, 175, 255), UnderlineStyle = UnderlineStyle.Single };
                    if (fileMenu != null) ChatPathMenus.Register(imageLink, imagePath);
                    imageLink.Click += async (_, _) => { if (!ChatPathMenus.IsSecondaryPress(imageLink)) await openFile(imagePath); };
                    imageLink.Inlines.Add(new Run { Text = "🖼 " });
                    if (link.FirstChild != null) RenderInlines(link, imageLink.Inlines);
                    else imageLink.Inlines.Add(new Run { Text = link.Title ?? "Image" });
                    target.Add(imageLink);
                }
                else if (link.IsImage)
                {
                    target.Add(new Run
                    {
                        Text = $"[{(string.IsNullOrEmpty(link.Title) ? "Image" : link.Title)}]",
                        Foreground = Brush(145, 160, 185)
                    });
                }
                else
                {
                    var hyperlink = new Hyperlink
                    {
                        Foreground = Brush(120, 175, 255),
                        UnderlineStyle = UnderlineStyle.Single
                    };
                    if (LocalFileLinks.PathFromUrl(link.Url) is string path && openFile != null)
                    {
                        if (fileMenu != null) ChatPathMenus.Register(hyperlink, path);
                        hyperlink.Click += async (_, _) => { if (!ChatPathMenus.IsSecondaryPress(hyperlink)) await openFile(path); };
                    }
                    else if (Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto")
                    {
                        hyperlink.Click += async (_, _) =>
                        {
                            try { await Launcher.LaunchUriAsync(uri); } catch { }
                        };
                    }
                    RenderInlines(link, hyperlink.Inlines);
                    target.Add(hyperlink);
                }
                break;

            case AutolinkInline autolink:
                var autoHyperlink = new Hyperlink
                {
                    Foreground = Brush(120, 175, 255),
                    UnderlineStyle = UnderlineStyle.Single
                };
                if (LocalFileLinks.PathFromUrl(autolink.Url) is string autoPath && openFile != null)
                {
                    if (fileMenu != null) ChatPathMenus.Register(autoHyperlink, autoPath);
                    autoHyperlink.Click += async (_, _) => { if (!ChatPathMenus.IsSecondaryPress(autoHyperlink)) await openFile(autoPath); };
                }
                else if (Uri.TryCreate(autolink.Url, UriKind.Absolute, out var autoUri))
                {
                    autoHyperlink.Click += async (_, _) =>
                    {
                        try { await Launcher.LaunchUriAsync(autoUri); } catch { }
                    };
                }
                autoHyperlink.Inlines.Add(new Run { Text = autolink.Url });
                target.Add(autoHyperlink);
                break;

            case TaskList task:
                target.Add(new Run
                {
                    Text = task.Checked ? "☑ " : "☐ ",
                    Foreground = task.Checked ? Brush(100, 220, 140) : Brush(160, 170, 190),
                    FontWeight = FontWeights.SemiBold
                });
                break;

            case HtmlInline html:
                target.Add(new Run { Text = html.Tag });
                break;

            case ContainerInline container:
                RenderInlines(container, target);
                break;

            default:
                target.Add(new Run { Text = inline.ToString() });
                break;
        }
    }
}
