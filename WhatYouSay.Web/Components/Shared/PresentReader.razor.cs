using Microsoft.AspNetCore.Components;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>
/// The presentation page's live body: the topic, how to join it, its responses and its summary,
/// sized to the screen for projecting. Interactive, inside the static shell that mints the
/// responder cookie.
/// </summary>
public partial class PresentReader
{
    [Parameter]
    [EditorRequired]
    public Guid TopicId { get; set; }

    [Parameter]
    [EditorRequired]
    public string Code { get; set; } = string.Empty;

    [Parameter]
    [EditorRequired]
    public string ReactorToken { get; set; } = string.Empty;

    /// <summary>The respond page's absolute URL, which the QR code encodes.</summary>
    [Parameter]
    [EditorRequired]
    public string JoinUrl { get; set; } = string.Empty;

    /// <summary>Whether the join panel warns that no public address is configured.</summary>
    [Parameter]
    public bool ShowsFallbackWarning { get; set; }
}
