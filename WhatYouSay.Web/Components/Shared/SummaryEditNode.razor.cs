using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>
/// One editable node and its subtree. Talks to <see cref="SummaryEditor"/> directly rather
/// than through a callback per operation, because the tree recurses to an arbitrary depth and
/// threading callbacks down it says nothing the cascade does not.
/// </summary>
public partial class SummaryEditNode
{
    [CascadingParameter]
    public SummaryEditor Editor { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public SummaryNode Node { get; set; } = default!;

    /// <summary>Zero at a root. Drives the indent, nothing else.</summary>
    [Parameter]
    public int Depth { get; set; }
}
