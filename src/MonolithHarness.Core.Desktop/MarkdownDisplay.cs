using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MonolithHarness.Core;

public static class MarkdownDisplay
{
    public const string Instructions = ReplyWebDocument.Instructions + " DISPLAY: Use Markdown with blank lines between paragraphs, headings and lists. The application renders LaTeX mathematics: $...$ inline and $$...$$ on separate lines for display equations. Use fenced ```mermaid blocks for diagrams (flowcharts, sequences, state diagrams, etc.). These are display formats, not executable tools; no tool call is needed to render them. Choose them when they clarify the answer, and keep ordinary text and code in their normal Markdown formats.";
    public static string InstructionsFor(FeatureSettings? settings = null) => Instructions + FileCitation.Instructions + " " +
        (settings?.ReplyRendering ?? ReplyRenderOptions.Default).Instructions +
        (settings is { AllowWebViewInChat: false }
            ? " Desktop web previews are currently disabled. Include ordinary inline CSS and a text/table or SVG alternative for charts, so the native static preview remains useful. The user can enable web previews in Settings / Chat."
            : "");
    static readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseMathematics().DisableHtml().Build();
    static readonly MarkdownPipeline plainMathPipeline = BuildPlainMathPipeline();
    static MarkdownPipeline BuildPlainMathPipeline()
    {
        var builder = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml();
        var mathematics = builder.Extensions.OfType<MathExtension>().FirstOrDefault();
        if (mathematics != null) builder.Extensions.Remove(mathematics);
        return builder.Build();
    }
    public static string Html(string markdown, bool renderMath = true, bool fileBadges = false)
    {
        var selected = renderMath ? pipeline : plainMathPipeline;
        var doc = Markdown.Parse(markdown, selected); LocalFileLinks.Decorate(doc);
        if (!fileBadges) return Markdown.ToHtml(doc, selected);
        using var writer = new StringWriter();
        var renderer = new Markdig.Renderers.HtmlRenderer(writer);
        selected.Setup(renderer);
        renderer.ObjectRenderers.Insert(0, new FileCitationLinkRenderer());
        renderer.Render(doc);
        return writer.ToString();
    }
    sealed class FileCitationLinkRenderer : Markdig.Renderers.Html.Inlines.LinkInlineRenderer
    {
        protected override void Write(Markdig.Renderers.HtmlRenderer renderer, LinkInline link)
        {
            if (link.IsImage || FileCitation.FromUrl(link.Url) is not { } citation) { base.Write(renderer, link); return; }
            static string Encode(string text) => System.Net.WebUtility.HtmlEncode(text);
            // Only emit fixed markup and encoded data; file contents are never loaded here.
            renderer.Write("<a class=\"file-citation\" href=\"" + Encode(LocalFileLinks.Prefix + Uri.EscapeDataString(citation.Target)) +
                "\" title=\"" + Encode(citation.Path) + "\"><img aria-hidden=\"true\" alt=\"\" class=\"file-citation-icon\" src=\"" +
                FileIconCatalog.DataUrl(citation.Extension) + "\"><span class=\"file-citation-name\">" + Encode(citation.Name) + "</span>");
            if (citation.StartLine is { } first)
                renderer.Write("<span class=\"file-citation-lines\">L." + first + (citation.EndLine > first ? "–" + citation.EndLine : "") + "</span>");
            renderer.Write("</a>");
        }
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
