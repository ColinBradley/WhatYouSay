using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>
/// One editable node and its subtree. Talks to <see cref="SummaryEditor"/> directly rather
/// than through a callback per operation, because the tree recurses to an arbitrary depth and
/// threading nine callbacks down it says nothing the cascade does not.
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

    private bool IsFirst()
    {
        return this.Editor.SiblingsOf(this.Node) is [var first, ..] && first.Id == this.Node.Id;
    }

    private bool IsLast()
    {
        return this.Editor.SiblingsOf(this.Node) is [.., var last] && last.Id == this.Node.Id;
    }

    /// <summary>
    /// Disabled controls keep a title saying why, so nobody has to guess whether the feature
    /// exists or they are just at the end of a group.
    /// </summary>
    private string? MoveTitle(bool stuck, string action, string reason)
    {
        return this.Editor.LockedReason ?? (stuck ? reason : action);
    }
}
