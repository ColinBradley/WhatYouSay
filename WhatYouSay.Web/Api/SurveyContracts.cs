namespace WhatYouSay.Web.Api;

/// <summary>What <c>GET /api/surveys/{code}</c> tells an agent about the survey its token is scoped to.</summary>
public record SurveyInfo
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

public record ResponseInfo
{
    public required Guid Id { get; init; }

    public required string Body { get; init; }

    /// <summary>Null on anonymous surveys, where it was never collected.</summary>
    public string? Author { get; init; }

    /// <summary>Null on anonymous surveys, where it was never recorded.</summary>
    public DateTimeOffset? CreatedAt { get; init; }
}
