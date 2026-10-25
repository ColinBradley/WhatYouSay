using Microsoft.AspNetCore.Components;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>
/// The topic page's live body: the topic, its responses and its summary side by side.
/// Interactive, inside the static shell that mints the responder cookie.
/// </summary>
public partial class TopicReader
{
    [Parameter]
    [EditorRequired]
    public Guid TopicId { get; set; }

    [Parameter]
    [EditorRequired]
    public string Code { get; set; } = string.Empty;

    /// <summary>The version that a version's own URL names. Null at the topic's URL.</summary>
    [Parameter]
    public Guid? SummaryId { get; set; }

    [Parameter]
    [EditorRequired]
    public string ReactorToken { get; set; } = string.Empty;

    [Parameter]
    public bool IsAdmin { get; set; }
}
