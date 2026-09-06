using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Web.Components.Shared;

public partial class SummaryEditToolbar
{
    private static readonly (NodeMove Move, string Glyph)[] Directions =
    [
        (NodeMove.Up, "↑"),
        (NodeMove.Down, "↓"),
        (NodeMove.Indent, "→"),
        (NodeMove.Outdent, "←"),
    ];

    [CascadingParameter]
    public SummaryEditor Editor { get; set; } = default!;

    /// <summary>
    /// The picked node, passed rather than read off <see cref="Editor"/>. The cascade is
    /// <c>IsFixed</c>, so a component whose only input comes through it is never re-rendered;
    /// this is the parameter that changes when the pick does.
    /// </summary>
    [Parameter]
    public SummaryNode? Selected { get; set; }

    private Task Move(NodeMove move)
    {
        return this.Selected is { } node ? this.Editor.MoveAsync(node.Id, move) : Task.CompletedTask;
    }
}
