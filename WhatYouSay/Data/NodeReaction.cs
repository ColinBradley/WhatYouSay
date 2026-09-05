namespace WhatYouSay.Data;

/// <summary>
/// One click on one node. Anyone who can see the topic may react; the cookie hash is the
/// dedupe key rather than a permission check.
/// </summary>
public class NodeReaction
{
    public int Id { get; set; }

    public int NodeId { get; set; }

    public SummaryNode Node { get; set; } = null!;

    /// <summary>SHA-256 of the wys_resp cookie token for this topic.</summary>
    public required string ReactorTokenHash { get; set; }

    public ReactionKind Kind { get; set; }

    /// <summary>Null when the topic is Anonymous, following the same rule as responses.</summary>
    public DateTimeOffset? CreatedAt { get; set; }
}
