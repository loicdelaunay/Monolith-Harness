using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MonolithHarness.Core;

public static class MarkdownDisplay
{
    public const string Instructions = "DISPLAY: Use Markdown with blank lines between paragraphs, headings and lists. The application renders LaTeX mathematics: $...$ inline and $$...$$ on separate lines for display equations. Use fenced ```mermaid blocks for diagrams (flowcharts, sequences, state diagrams, etc.). These are display formats, not executable tools; no tool call is needed to render them. Choose them when they clarify the answer, and keep ordinary text and code in their normal Markdown formats.";
    static readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseMathematics().DisableHtml().Build();
    static readonly MarkdownPipeline plainMathPipeline = BuildPlainMathPipeline();
    static MarkdownPipeline BuildPlainMathPipeline()
    {
        var builder = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml();
        var mathematics = builder.Extensions.OfType<MathExtension>().FirstOrDefault();
        if (mathematics != null) builder.Extensions.Remove(mathematics);
        return builder.Build();
    }
    public static string Html(string markdown, bool renderMath = true)
    {
        var selected = renderMath ? pipeline : plainMathPipeline;
        var doc = Markdown.Parse(markdown, selected); LocalFileLinks.Decorate(doc);
        return Markdown.ToHtml(doc, selected);
    }
    public static bool HasVisuals(MarkdownDocument document, bool renderMermaid = true, bool renderMath = true) => Nodes(document).Any(x =>
        renderMath && (x is MathBlock or MathInline) || renderMermaid && x is FencedCodeBlock code && string.Equals(code.Info, "mermaid", StringComparison.OrdinalIgnoreCase));
    static IEnumerable<MarkdownObject> Nodes(MarkdownObject root)
    {
        yield return root;
        if (root is ContainerBlock blocks) foreach (var block in blocks) foreach (var node in Nodes(block)) yield return node;
        else if (root is LeafBlock { Inline: { } inline }) foreach (var node in Nodes(inline)) yield return node;
        else if (root is ContainerInline inlines) foreach (var child in inlines) foreach (var node in Nodes(child)) yield return node;
    }
}
