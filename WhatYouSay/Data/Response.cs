namespace WhatYouSay.Data;

public class Response
{
    public Guid Id { get; set; }

    public Guid SurveyId { get; set; }

    public Survey Survey { get; set; } = null!;

    public required string Body { get; set; }

    /// <summary>Self-declared and unverified. Never collected when the survey is Anonymous.</summary>
    public string? Author { get; set; }

    /// <summary>SHA-256 of the plaintext token held in the responder's cookie.</summary>
    public required string AuthTokenHash { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>Null when the survey is Anonymous. Not collected, not merely hidden.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Null when Anonymous, or when never edited.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public List<SummaryTopicPointResponseReference> References { get; set; } = [];
}
