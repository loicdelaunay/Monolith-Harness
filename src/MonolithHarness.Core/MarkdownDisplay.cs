using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MonolithHarness.Core;

public static class MarkdownDisplay
{
    public const string Instructions = "DISPLAY: Use Markdown with blank lines between paragraphs, headings and lists. The application renders LaTeX mathematics: $...$ inline and $$...$$ on separate lines for display equations. Use fenced ```mermaid blocks for diagrams (flowcharts, sequences, state diagrams, etc.). These are display formats, not executable tools; no tool call is needed to render them. Choose them when they clarify the answer, and keep ordinary text and code in their normal Markdown formats.";
    static readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseMathematics().DisableHtml().Build();
    public static string Html(string markdown)
    {
        var doc = Markdown.Parse(markdown, pipeline); LocalFileLinks.Decorate(doc);
        return Markdown.ToHtml(doc, pipeline);
    }
    public static bool HasVisuals(MarkdownDocument document) => Nodes(document).Any(x => x is MathBlock or MathInline ||
        x is FencedCodeBlock code && string.Equals(code.Info, "mermaid", StringComparison.OrdinalIgnoreCase));
    static IEnumerable<MarkdownObject> Nodes(MarkdownObject root)
    {
        yield return root;
        if (root is ContainerBlock blocks) foreach (var block in blocks) foreach (var node in Nodes(block)) yield return node;
        else if (root is LeafBlock { Inline: { } inline }) foreach (var node in Nodes(inline)) yield return node;
        else if (root is ContainerInline inlines) foreach (var child in inlines) foreach (var node in Nodes(child)) yield return node;
    }
}
