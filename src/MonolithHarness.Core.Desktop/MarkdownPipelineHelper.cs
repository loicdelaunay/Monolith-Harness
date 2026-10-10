using Markdig;
using Markdig.Syntax;

namespace MonolithHarness.Core;

public static class MarkdownPipelineHelper
{
    public static MarkdownPipeline Pipeline { get; } = new MarkdownPipelineBuilder()
        .UseAutoLinks()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .UseMathematics()
        .Build();

    static readonly MarkdownPipeline plainMathPipeline = new MarkdownPipelineBuilder().UseAutoLinks().UsePipeTables().UseTaskLists().UseEmphasisExtras().Build();
    public static MarkdownDocument Parse(string? markdown, bool renderMath = true)
    {
        if (string.IsNullOrEmpty(markdown))
            return new MarkdownDocument();
        var document = Markdown.Parse(markdown, renderMath ? Pipeline : plainMathPipeline);
        LocalFileLinks.Decorate(document);
        return document;
    }
}
