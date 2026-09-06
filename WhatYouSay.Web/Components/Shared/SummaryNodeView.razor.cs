using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Web.Components.Shared;

public partial class SummaryNodeView
{
    [CascadingParameter]
    public SummaryReading Reading { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public SummaryNode Node { get; set; } = default!;

    /// <summary>Zero at a root. The tree's own CSS does the indenting.</summary>
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

    private static string Emoji(ReactionKind kind) =>
        kind switch
        {
            ReactionKind.Agree => "\U0001F44D",
            ReactionKind.Disagree => "\U0001F44E",
            ReactionKind.Important => "❗",
            ReactionKind.Question => "❓",
            ReactionKind.Celebrate => "\U0001F389",
            ReactionKind.Laugh => "\U0001F604",
            _ => "?",
        };

    private static string Label(ReactionKind kind) =>
        kind switch
        {
            ReactionKind.Agree => "Agree",
            ReactionKind.Disagree => "Disagree",
            ReactionKind.Important => "Important",
            ReactionKind.Question => "Not sure about this",
            ReactionKind.Celebrate => "Worth celebrating",
            ReactionKind.Laugh => "Made me laugh",
            _ => kind.ToString(),
        };
}
