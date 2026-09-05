namespace WhatYouSay.Data;

/// <summary>
/// Free text on one node, standing alone rather than hanging off a reaction. Where the
/// group says the summary has misread them.
/// </summary>
public class NodeComment
{
    public int Id { get; set; }

    public int NodeId { get; set; }

    public SummaryNode Node { get; set; } = null!;

    /// <summary>SHA-256 of the wys_resp cookie token for this topic.</summary>
    public required string AuthorTokenHash { get; set; }

    /// <summary>Self-declared and unverified. Never collected when the topic is Anonymous.</summary>
    public string? Author { get; set; }

    public required string Body { get; set; }

    /// <summary>
    /// Hidden by an admin or by the comment's own author. Never by an agent, and never
    /// deleted: a settled objection is closed, not erased.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>Null when the topic is Anonymous, following the same rule as responses.</summary>
    public DateTimeOffset? CreatedAt { get; set; }
}
