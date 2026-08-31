namespace WhatYouSay.Web.Api;

public record SummaryInfo
{
    public required Guid Id { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required bool IsDraft { get; init; }

    public required bool IsPublic { get; init; }

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

    public required string Quote { get; init; }
}

/// <summary>Per-node reaction counts, plus every objection in full.</summary>
public record ReactionInfo
{
    public required int NodeId { get; init; }

    public required string NodeText { get; init; }

    public required int Agree { get; init; }

    public required int Important { get; init; }

    public required int Misrepresents { get; init; }

    public required IReadOnlyList<string> Objections { get; init; }
}

/// <summary>What an agent gets back after a draft passes grounding validation.</summary>
public record DraftResult
{
    public required Guid SummaryId { get; init; }

    public required string EditUrl { get; init; }

    public required int NodeCount { get; init; }

    public required int MaxDepth { get; init; }

    public required int ReferenceCount { get; init; }
}
