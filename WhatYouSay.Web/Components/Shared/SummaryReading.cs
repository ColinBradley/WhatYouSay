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
        Agree = 0,
        Important = 0,
        Misrepresents = 0,
        Mine = new HashSet<ReactionKind>(),
    };

    public required bool ResponsesArePublic { get; init; }

    public required bool CanReact { get; init; }

    /// <summary>Why the reaction controls are disabled, or null when they are not.</summary>
    public required string? ReactReason { get; init; }

    public required IReadOnlyDictionary<int, NodeReactionTally> Tallies { get; init; }

    public NodeReactionTally TallyFor(int nodeId)
    {
        return this.Tallies.TryGetValue(nodeId, out var tally) ? tally : sNoReactions;
    }

    /// <summary>
    /// The objection textarea's form field. Static SSR posts the whole form, so every node's
    /// note needs a name of its own.
    /// </summary>
    public static string NoteField(int nodeId)
    {
        return $"Note_{nodeId}";
    }
}
