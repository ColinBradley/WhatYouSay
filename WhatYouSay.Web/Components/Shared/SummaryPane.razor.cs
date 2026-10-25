using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using WhatYouSay.Data;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>
/// The summary on show, with its overview, note tree and other versions, or why there is none.
/// Sits inside a <see cref="TopicFeed"/>.
/// </summary>
public partial class SummaryPane
{
    private ElementReference mPane;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [CascadingParameter]
    public TopicFeed Feed { get; set; } = default!;

    [CascadingParameter]
    public TopicView View { get; set; } = default!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await this.JS.InvokeVoidAsync("whatYouSayPanes.keep", mPane, "li, .prose");
        }
    }

    /// <summary>When a summary last changed, with its date only when that was not today.</summary>
    private string WhenUpdated(Summary summary)
    {
        var updated = summary.UpdatedAt.ToLocalTime();

        return updated.Date == DateTime.Today
            ? updated.ToString("HH:mm")
            : updated.ToString("d MMM yyyy HH:mm");
    }

    /// <summary>Only ever non-zero for an admin, since only an admin's versions include drafts.</summary>
    private int Unpublished =>
        this.View.Versions.Count(v => !v.IsVisibleToPublic);

    /// <summary>Versions are held newest first, but numbered oldest first.</summary>
    private int VersionNumber(Summary summary)
    {
        return this.View.Versions.Count - this.View.Versions.ToList().FindIndex(v => v.Id == summary.Id);
    }
}
