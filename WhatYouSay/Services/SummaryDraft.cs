namespace WhatYouSay.Services;

/// <summary>
/// What an agent submits to create or replace a draft summary. Carries no quote offsets:
/// the app locates them, so a wrong offset is not expressible.
/// </summary>
public record SummaryDraft
{
    public required string Body { get; init; }

    public required IReadOnlyList<TopicDraft> Topics { get; init; }
}

public record TopicDraft
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public required IReadOnlyList<PointDraft> Points { get; init; }
}

public record PointDraft
{
    public required string Description { get; init; }

    /// <summary>-1 (negative) to +1 (positive).</summary>
    public double? Sentiment { get; init; }

    /// <summary>0 (pure opinion) to 1 (verifiable fact).</summary>
    public double? Objectivity { get; init; }

    /// <summary>Required, so an uncited point cannot happen by omitting a field.</summary>
    public required IReadOnlyList<ReferenceDraft> References { get; init; }
}

public record ReferenceDraft
{
    public required Guid ResponseId { get; init; }

    /// <summary>Copied character for character from the response body, or the call is rejected.</summary>
    public required string Quote { get; init; }

    /// <summary>0 to 1, how strongly this quote supports its point.</summary>
    public double? Intensity { get; init; }
}

/// <summary>
/// Thrown when a draft fails grounding validation. The message names the offending point
/// and quote; agents are expected to read it and retry.
/// </summary>
public class SummaryGroundingException(string reason, string message) : Exception(message)
{
    /// <summary>Short machine-readable cause, used as a metric and span tag.</summary>
    public string Reason { get; } = reason;
}
