using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>
/// The summary on show, with its overview, note tree and other versions, or why there is none.
/// Sits inside a <see cref="TopicFeed"/>.
/// </summary>
public partial class SummaryPane
{
    [CascadingParameter]
    public TopicFeed Feed { get; set; } = default!;

    [CascadingParameter]
    public TopicView View { get; set; } = default!;

    /// <summary>Only ever non-zero for an admin, since only an admin's versions include drafts.</summary>
    private int Unpublished =>
        this.View.Versions.Count(v => !v.IsVisibleToPublic);

    /// <summary>Versions are held newest first, but numbered oldest first.</summary>
    private int VersionNumber(Summary summary)
    {
        return this.View.Versions.Count - this.View.Versions.ToList().FindIndex(v => v.Id == summary.Id);
    }
}
