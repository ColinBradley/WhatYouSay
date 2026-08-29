using Markdig;

namespace WhatYouSay.Web.Markdown;

/// <summary>
/// Summary narratives are written by agents as well as humans, so raw HTML passthrough is
/// disabled. Markdig allows it by default, which would turn a summary into an injection
/// point for anything an agent decided to emit.
/// </summary>
public static class SummaryMarkdown
{
    private static readonly MarkdownPipeline sPipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAutoLinks()
        .Build();

    public static string ToHtml(string markdown) => Markdig.Markdown.ToHtml(markdown, sPipeline);
}
