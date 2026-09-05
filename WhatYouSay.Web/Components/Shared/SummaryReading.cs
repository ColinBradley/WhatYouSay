using System.Collections.Immutable;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>
/// What every node of a rendered summary needs to know about the person reading it.
/// Cascaded rather than passed, because the tree recurses to an arbitrary depth.
/// </summary>
public record SummaryReading
{
    private static readonly NodeReactionTally sNoReactions = new()
    {
        Counts = new Dictionary<ReactionKind, int>(),
        Mine = new HashSet<ReactionKind>(),
    };

    /// <summary>The bar, in display order.</summary>
    public static readonly ImmutableArray<ReactionKind> Reactions =
        [.. Enum.GetValues<ReactionKind>()];

    public required bool ResponsesArePublic { get; init; }

    public required IReadOnlyDictionary<int, NodeReactionTally> Tallies { get; init; }

    public required IReadOnlyList<NodeCommentView> Comments { get; init; }

    public NodeReactionTally TallyFor(int nodeId)
    {
        return this.Tallies.TryGetValue(nodeId, out var tally) ? tally : sNoReactions;
    }

    public IReadOnlyList<NodeCommentView> CommentsFor(int nodeId)
    {
        return [.. this.Comments.Where(comment => comment.NodeId == nodeId)];
    }

    /// <summary>
    /// The comment box's form field. Static SSR posts the whole form, so every node's box
    /// needs a name of its own.
    /// </summary>
    public static string CommentField(int nodeId)
    {
        return $"Comment_{nodeId}";
    }
}
