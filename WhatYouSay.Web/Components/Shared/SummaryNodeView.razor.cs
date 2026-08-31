using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Web.Components.Shared;

public partial class SummaryNodeView
{
    /// <summary>
    /// Below this, children arrive collapsed. A rendering decision, not a stored one:
    /// nothing about depth is a property of the data.
    /// </summary>
    private const int CollapseChildrenBelow = 2;

    [CascadingParameter]
    public SummaryReading Reading { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public SummaryNode Node { get; set; } = default!;

    /// <summary>Zero at a root. Drives heading level, indent and collapsing.</summary>
    [Parameter]
    public int Depth { get; set; }

    /// <summary>Whether something above this node cites a response.</summary>
    [Parameter]
    public bool Inherited { get; set; }

    /// <summary>Where the group and this viewer sit on this node.</summary>
    private NodeReactionTally Tally =>
        this.Reading.TallyFor(this.Node.Id);

    private bool PassesSupportDown()
    {
        return this.Inherited || this.Node.References.Count > 0;
    }

    private bool Collapse()
    {
        return this.Depth >= CollapseChildrenBelow;
    }

    /// <summary>
    /// Depth is the only thing left to render by. A root reads as a heading because that is
    /// what the top of a tree is, not because the node says so.
    /// </summary>
    private string ItemClass()
    {
        return this.Depth == 0 ? "mb-4" : "mb-3";
    }

    private static string Pressed(NodeReactionTally tally, ReactionKind kind)
    {
        return tally.Mine.Contains(kind) ? "btn-success" : "btn-outline-secondary";
    }

    /// <summary>
    /// If the offsets no longer select the stored quote, show the quote alone rather than
    /// slicing the body blindly.
    /// </summary>
    private static (string Before, string Quote, string After)? Highlight(SummaryNodeReference reference)
    {
        var body = reference.Response.Body;

        return QuoteLocator.Matches(body, reference.Quote, reference.StartIndex, reference.EndIndex)
            ? (body[..reference.StartIndex], reference.Quote, body[reference.EndIndex..])
            : null;
    }
}
