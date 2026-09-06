namespace WhatYouSay.Services;

/// <summary>
/// What an agent submits to create or replace a draft summary. Nested rather than flat, so
/// a mistyped parent cannot spawn a spurious node or a cycle. Carries no quote offsets: the
/// app locates them, so a wrong offset is not expressible.
/// </summary>
public record SummaryDraft
{
    public required string Body { get; init; }

    public required IReadOnlyList<NodeDraft> Nodes { get; init; }
}

/// <summary>
/// One node in a submitted tree.
/// </summary>
public record NodeDraft
{
    /// <summary>An existing node in the summary being revised. Null on anything new.</summary>
    public int? Id { get; init; }

    public string? Text { get; init; }

    /// <summary>
    /// Replaces whatever the node cited. Only meaningful with <see cref="Text"/>, since
    /// citations belong to the assertion they support. Empty is fine anywhere but a leaf: a
    /// node under a cited one inherits the support above it.
    /// </summary>
    public IReadOnlyList<ReferenceDraft>? References { get; init; }

    public IReadOnlyList<NodeDraft> Children { get; init; } = [];
}

public record ReferenceDraft
{
    public required Guid ResponseId { get; init; }

    /// <summary>Copied character for character from the response body, or the call is rejected.</summary>
    public required string Quote { get; init; }
}

/// <summary>One thing wrong with a submitted draft, located within it.</summary>
public record GroundingFailure
{
    /// <summary>
    /// JSON Pointer into the submitted draft, so a caller can point at the offending field
    /// without matching on prose.
    /// </summary>
    public required string Path { get; init; }

    public required string Reason { get; init; }

    public required string Message { get; init; }

    /// <summary>
    /// For a quote that did not match, the response text it was probably reaching for,
    /// copied exactly. Null for every other reason.
    /// </summary>
    public string? Nearest { get; init; }

    /// <summary>For a quote that did not match, where it first diverges.</summary>
    public string? Detail { get; init; }
}

/// <summary>
/// Thrown when a draft fails grounding validation. <see cref="Failures"/> holds every
/// problem found in one pass, so a caller fixing them all needs one retry rather than one
/// per mistake.
/// </summary>
public class SummaryGroundingException : Exception
{
    public SummaryGroundingException(
        string reason,
        string message,
        IReadOnlyList<GroundingFailure> failures
    )
        : base(message)
    {
        this.Reason = reason;
        this.Failures = failures;
    }

    /// <summary>Short machine-readable cause, used as a metric and span tag.</summary>
    public string Reason { get; }

    public IReadOnlyList<GroundingFailure> Failures { get; }
}
