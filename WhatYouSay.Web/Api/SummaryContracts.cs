namespace WhatYouSay.Web.Api;

public record SummaryInfo
{
    public required Guid Id { get; init; }

    /// <summary>
    /// Goes up by one with every write to the overview or tree. Send it back as basedOnRevision
    /// when revising.
    /// </summary>
    public required int Revision { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required bool IsDraft { get; init; }

    public required bool IsPublic { get; init; }

    public required bool IsAgentEditable { get; init; }

    /// <summary>Whether this is the topic's live summary, whose accepted writes readers see straight away.</summary>
    public required bool IsLive { get; init; }

    public string? CreatedBy { get; init; }

    public required int NodeCount { get; init; }

    /// <summary>
    /// Levels, not edges. Reported rather than capped: a staircase should be visible without
    /// being illegal.
    /// </summary>
    public required int MaxDepth { get; init; }
}

public record SummaryDetail
{
    public required SummaryInfo Info { get; init; }

    public required string Body { get; init; }

    public required IReadOnlyList<NodeDetail> Nodes { get; init; }
}

public record NodeDetail
{
    public required int Id { get; init; }

    public required string Text { get; init; }

    public required IReadOnlyList<ReferenceDetail> References { get; init; }

    public required IReadOnlyList<NodeDetail> Children { get; init; }
}

public record ReferenceDetail
{
    public required Guid ResponseId { get; init; }

    /// <summary>Which wording of the response the quote was taken from.</summary>
    public required int Revision { get; init; }

    /// <summary>False once the response has been edited. The quote still matches its own revision.</summary>
    public required bool IsCurrentRevision { get; init; }

    public required string Quote { get; init; }
}

/// <summary>Per-node reaction counts. Only kinds anyone clicked appear.</summary>
public record ReactionInfo
{
    public required int NodeId { get; init; }

    public required string NodeText { get; init; }

    public required IReadOnlyDictionary<string, int> Counts { get; init; }
}

/// <summary>What the group said about a draft, for the pass that answers them.</summary>
public record CommentInfo
{
    public required int NodeId { get; init; }

    public required string NodeText { get; init; }

    public required string Body { get; init; }

    public required string? Author { get; init; }

    /// <summary>
    /// Every live response the commenter gave to the topic, in the order <c>/responses</c>
    /// lists them. Empty when they gave none. The comment concerns its node, not any one of
    /// these responses.
    /// </summary>
    public required IReadOnlyList<CommenterResponseInfo> CommenterResponses { get; init; }
}

/// <summary>One of a commenter's own responses, by the id <c>/responses</c> gives it.</summary>
public record CommenterResponseInfo
{
    public required Guid ResponseId { get; init; }

    public required string Body { get; init; }
}

/// <summary>What an agent gets back after a write passes validation.</summary>
public record DraftResult
{
    public required Guid SummaryId { get; init; }

    /// <summary>The revision this write created, to send as basedOnRevision on the next one.</summary>
    public required int Revision { get; init; }

    /// <summary>Whether the write went to the live summary, so readers already see it.</summary>
    public required bool IsLive { get; init; }

    public required string EditUrl { get; init; }

    public required int NodeCount { get; init; }

    public required int MaxDepth { get; init; }

    public required int ReferenceCount { get; init; }
}
