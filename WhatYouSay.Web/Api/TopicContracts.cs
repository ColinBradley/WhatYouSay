namespace WhatYouSay.Web.Api;

/// <summary>What <c>GET /api/topics/{code}</c> tells an agent about the topic its token is scoped to.</summary>
public record TopicInfo
{
    public required string Code { get; init; }

    public required string Title { get; init; }

    public required string Prompt { get; init; }

    public required string ResponseIdentity { get; init; }

    /// <summary>Whether the topic records when responses arrive: <c>Recorded</c> or <c>NotRecorded</c>.</summary>
    public required string ResponseTiming { get; init; }

    public required bool IsAcceptingResponses { get; init; }

    public required bool AreResponsesPublic { get; init; }

    public required int ResponseCount { get; init; }

    public required int SummaryCount { get; init; }

    /// <summary>Counts changes to anything the summariser reads. See <c>GET /live</c>.</summary>
    public required int InputRevision { get; init; }
}

/// <summary>
/// A page of responses, with the total so an agent knows what it is dealing with before
/// it starts. The 60-100 response topic is a real shape, and one unbounded array is both
/// a context problem and an obstacle to splitting the work between sub-agents.
/// </summary>
public record ResponsePage
{
    public required int Total { get; init; }

    public required int Skip { get; init; }

    /// <summary>
    /// The topic's input revision when the page was read. A different value on a later page
    /// means responses or reactions changed in between.
    /// </summary>
    public required int InputRevision { get; init; }

    /// <summary>The id to pass as <c>after</c> for the next page. Null on the last page.</summary>
    public Guid? NextAfter { get; init; }

    public required IReadOnlyList<ResponseInfo> Items { get; init; }
}

public record ResponseInfo
{
    public required Guid Id { get; init; }

    public required string Body { get; init; }

    /// <summary>The revision that <see cref="Body"/> belongs to. It goes up by one with every edit.</summary>
    public required int Revision { get; init; }

    /// <summary>Null on anonymous topics, where it was never collected.</summary>
    public string? Author { get; init; }

    /// <summary>Null on topics that record no timing.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Counts by reaction kind. Only kinds anyone used appear.</summary>
    public required IReadOnlyDictionary<string, int> Reactions { get; init; }
}
