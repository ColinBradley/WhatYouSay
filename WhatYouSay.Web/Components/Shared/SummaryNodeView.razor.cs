using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Web.Components.Shared;

public partial class SummaryNodeView
{
    [CascadingParameter]
    public TopicFeed Feed { get; set; } = default!;

    [CascadingParameter]
    public TopicView View { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public SummaryNode Node { get; set; } = default!;

    /// <summary>Zero at a root. The tree's own CSS does the indenting.</summary>
    [Parameter]
    public int Depth { get; set; }

    /// <summary>Where the group and this viewer sit on this node.</summary>
    private ReactionTally Tally =>
        this.View.TallyFor(this.Node.Id);

    private static string QuoteTitle(SummaryNodeReference reference) =>
        reference.IsOnCurrentRevision
            ? $"“{reference.Quote}”"
            : $"“{reference.Quote}” (the response has been edited since)";

    private Task ToggleAsync(ReactionKind kind) =>
        this.Feed.ToggleOnNodeAsync(this.Node.Id, kind);
}
