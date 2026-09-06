using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>One sibling group. Split from the node itself so the pair can recurse.</summary>
public partial class SummaryNodeList
{
    [Parameter]
    [EditorRequired]
    public IReadOnlyList<SummaryNode> Nodes { get; set; } = [];

    [Parameter]
    public int Depth { get; set; }
}
