namespace WhatYouSay.Web.Api;

/// <summary>What <c>GET /api/topics/{code}</c> tells an agent about the topic its token is scoped to.</summary>
public record TopicInfo
{
    public required string Code { get; init; }

    public required string Title { get; init; }

    public required string Prompt { get; init; }

    public required string ResponseIdentity { get; init; }

    public required bool IsAcceptingResponses { get; init; }

    public required bool AreResponsesPublic { get; init; }

    public required int ResponseCount { get; init; }

    public required int SummaryCount { get; init; }
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

    public required IReadOnlyList<ResponseInfo> Items { get; init; }
}

public record ResponseInfo
{
    public required Guid Id { get; init; }

    public required string Body { get; init; }

    /// <summary>Null on anonymous topics, where it was never collected.</summary>
    public string? Author { get; init; }

    /// <summary>Null on anonymous topics, where it was never recorded.</summary>
    public DateTimeOffset? CreatedAt { get; init; }
}
