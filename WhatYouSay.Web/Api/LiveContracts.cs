using System.Text.Json;

namespace WhatYouSay.Web.Api;

/// <summary>
/// What <c>GET /api/topics/{code}/live</c> returns: the topic's summariser input, the live
/// summary's write state, and what to tell a model. Every field comes from one consistent read.
/// </summary>
public record LiveSnapshot
{
    /// <summary>
    /// Counts changes to anything in <see cref="Input"/> except the live summary itself.
    /// Send it back as <c>inputRevision</c> with a write generated from this snapshot.
    /// </summary>
    public required int InputRevision { get; init; }

    /// <summary>
    /// Whether the live summary was generated from older input, or there is no live summary yet.
    /// Always false while there are no responses.
    /// </summary>
    public required bool IsPending { get; init; }

    /// <summary>Whether <c>PUT /live</c> would be accepted now.</summary>
    public required bool IsWritable { get; init; }

    /// <summary>
    /// Why <see cref="IsWritable"/> is false: <c>paused</c> when an admin has paused the
    /// summariser, or <c>reviewed</c> when the topic has review switched on. Null when writable.
    /// </summary>
    public string? NotWritableReason { get; init; }

    /// <summary>The live summary's write state. Null when the topic has none, and the next write creates it.</summary>
    public LiveSummaryState? LiveSummary { get; init; }

    /// <summary>What the model reads.</summary>
    public required LiveInput Input { get; init; }

    /// <summary>How to ask a model for the next version of the live summary.</summary>
    public required ModelBrief Model { get; init; }
}

public record LiveSummaryState
{
    public required Guid Id { get; init; }

    /// <summary>Send it back as <c>basedOnRevision</c>.</summary>
    public required int Revision { get; init; }

    /// <summary>
    /// The input revision that the last summariser write was generated from.
    /// Null when that write did not say, which counts as pending.
    /// </summary>
    public int? AppliedInputRevision { get; init; }
}

/// <summary>The part of a snapshot that a model reads.</summary>
public record LiveInput
{
    public required LiveTopic Topic { get; init; }

    public required LiveGuidance Guidance { get; init; }

    /// <summary>Every live response, in the topic's listing order. Never truncated.</summary>
    public required IReadOnlyList<LiveResponse> Responses { get; init; }

    /// <summary>The live summary's current tree, for the model to revise. Null when there is none.</summary>
    public LiveSummaryContent? Summary { get; init; }
}

public record LiveTopic
{
    public required string Title { get; init; }

    public required string Question { get; init; }

    /// <summary><c>Required</c>, <c>Optional</c> or <c>Anonymous</c>.</summary>
    public required string ResponseIdentity { get; init; }

    /// <summary><c>Recorded</c> or <c>NotRecorded</c>.</summary>
    public required string ResponseTiming { get; init; }
}

/// <summary>The organiser's guidance. Each field is null when none was given.</summary>
public record LiveGuidance
{
    public string? ExpectedFeedback { get; init; }

    public string? Instructions { get; init; }

    public string? LengthTarget { get; init; }
}

public record LiveResponse
{
    public required Guid Id { get; init; }

    /// <summary>The revision that <see cref="Body"/> belongs to. Cite it with this number.</summary>
    public required int Revision { get; init; }

    public required string Body { get; init; }

    /// <summary>Null when no name was given or none was collected.</summary>
    public string? Author { get; init; }

    /// <summary>Null on topics that record no timing.</summary>
    public DateTimeOffset? SubmittedAt { get; init; }

    public required bool IsEdited { get; init; }

    /// <summary>Counts by reaction kind. Only kinds anyone used appear.</summary>
    public required IReadOnlyDictionary<string, int> Reactions { get; init; }
}

public record LiveSummaryContent
{
    public required string Body { get; init; }

    public required IReadOnlyList<LiveNode> Nodes { get; init; }
}

public record LiveNode
{
    public required int Id { get; init; }

    public required string Text { get; init; }

    public required IReadOnlyList<ReferenceDetail> References { get; init; }

    /// <summary>Counts by reaction kind. Only kinds anyone used appear.</summary>
    public required IReadOnlyDictionary<string, int> Reactions { get; init; }

    /// <summary>Visible comments on this node, in the order they were left.</summary>
    public required IReadOnlyList<LiveComment> Comments { get; init; }

    public required IReadOnlyList<LiveNode> Children { get; init; }
}

public record LiveComment
{
    public string? Author { get; init; }

    public required string Body { get; init; }

    /// <summary>
    /// The commenter's own live responses, by id. The comment concerns its node, not any one
    /// of these responses.
    /// </summary>
    public required IReadOnlyList<Guid> CommenterResponseIds { get; init; }
}

public record ModelBrief
{
    /// <summary>The system prompt. The snapshot's <c>input</c>, as JSON, is the user message.</summary>
    public required string Instructions { get; init; }

    /// <summary>A JSON Schema for the model's answer: the summary's <c>body</c> and <c>nodes</c>.</summary>
    public required JsonElement OutputSchema { get; init; }
}

/// <summary>What <c>PUT /live</c> returns once a write is accepted.</summary>
public record LiveWriteResult
{
    public required Guid SummaryId { get; init; }

    /// <summary>The revision this write created. Send it as <c>basedOnRevision</c> on the next write.</summary>
    public required int Revision { get; init; }

    /// <summary>The input revision the write was accepted for.</summary>
    public required int AppliedInputRevision { get; init; }

    /// <summary>The topic's input revision after the write. Higher than the applied one when newer input is waiting.</summary>
    public required int InputRevision { get; init; }

    /// <summary>Whether newer input is waiting.</summary>
    public required bool IsPending { get; init; }

    public required int NodeCount { get; init; }

    public required int MaxDepth { get; init; }

    public required int ReferenceCount { get; init; }
}
