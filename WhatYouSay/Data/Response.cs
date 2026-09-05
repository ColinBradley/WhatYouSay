namespace WhatYouSay.Data;

public class Response
{
    public Guid Id { get; set; }

    public Guid TopicId { get; set; }

    public Topic Topic { get; set; } = null!;

    public required string Body { get; set; }

    /// <summary>Self-declared and unverified. Never collected when the topic is Anonymous.</summary>
    public string? Author { get; set; }

    /// <summary>SHA-256 of the plaintext token held in the responder's cookie.</summary>
    public required string AuthTokenHash { get; set; }

    /// <summary>
    /// Prevent editing. Helping tie references to responses - otherwise they can drift.
    /// </summary>
    public bool IsFrozen { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>Null when the topic is Anonymous. Not collected, not merely hidden.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Null when Anonymous, or when never edited.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public List<SummaryNodeReference> References { get; set; } = [];
}
