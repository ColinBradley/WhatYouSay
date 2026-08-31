namespace WhatYouSay.Web.Api;

public record SummaryInfo
{
    public required Guid Id { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required bool IsDraft { get; init; }

    public required bool IsPublic { get; init; }

    public string? CreatedBy { get; init; }

    public required int TopicCount { get; init; }

    public required int PointCount { get; init; }
}

public record SummaryDetail
{
    public required SummaryInfo Info { get; init; }

    public required string Body { get; init; }

    public required IReadOnlyList<TopicDetail> Topics { get; init; }
}

public record TopicDetail
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public required IReadOnlyList<PointDetail> Points { get; init; }
}

public record PointDetail
{
    public required int Id { get; init; }

    public required string Description { get; init; }

    public double? Sentiment { get; init; }

    public double? Objectivity { get; init; }

    public required IReadOnlyList<ReferenceDetail> References { get; init; }
}

public record ReferenceDetail
{
    public required Guid ResponseId { get; init; }

    public required string Quote { get; init; }

    public double? Intensity { get; init; }
}

/// <summary>Per-point reaction counts, plus every objection in full.</summary>
public record ReactionInfo
{
    public required int PointId { get; init; }

    public required string PointDescription { get; init; }

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

    public required int TopicCount { get; init; }

    public required int PointCount { get; init; }

    public required int ReferenceCount { get; init; }
}
