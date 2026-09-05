namespace WhatYouSay.Data;

/// <summary>
/// The group answering back. Keyed on the responder's cookie token, which is both the
/// permission check and the dedupe key — only people who responded may react.
/// </summary>
public class NodeReaction
{
    public int Id { get; set; }

    public int NodeId { get; set; }

    public SummaryNode Node { get; set; } = null!;

    /// <summary>SHA-256 of the wys_resp cookie token for this topic.</summary>
    public required string ResponderTokenHash { get; set; }

    public ReactionKind Kind { get; set; }

    /// <summary>Mainly for Misrepresents, where the detail is the whole point.</summary>
    public string? Note { get; set; }

    /// <summary>Null when the topic is Anonymous, following the same rule as responses.</summary>
    public DateTimeOffset? CreatedAt { get; set; }
}
