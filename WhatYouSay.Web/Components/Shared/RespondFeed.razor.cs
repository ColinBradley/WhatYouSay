using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Web.Components.Pages;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>
/// The respond page below its form: the responder's own responses, and everyone's where the
/// topic's responses are public. It is interactive, so reacting and live updates never reload
/// the static form or lose what is typed into it.
/// </summary>
public partial class RespondFeed
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

    /// <summary>The response open in the form. Null while the form adds a new one.</summary>
    [Parameter]
    public Guid? EditingId { get; set; }

    /// <summary>
    /// Why one of the responder's own responses cannot be opened for editing, or null when it can.
    /// </summary>
    private string? EditBlockedReason(Response response)
    {
        if (response.Id == this.EditingId)
        {
            return "This is the response in the form above.";
        }

        return response.IsFrozen ? RespondPage.FrozenReason : null;
    }
}
