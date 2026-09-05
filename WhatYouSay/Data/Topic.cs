namespace WhatYouSay.Data;

public class Topic
{
    public Guid Id { get; set; }

    /// <summary>Short url-safe code; the public URL segment in /topics/{code}.</summary>
    public required string Code { get; set; }

    public required string Title { get; set; }

    /// <summary>The prompt. Title + Description is the entire question.</summary>
    public required string Description { get; set; }

    /// <summary>PBKDF2. Human-chosen, therefore reused elsewhere, therefore slow-hashed.</summary>
    public required string AdminPasswordHash { get; set; }

    /// <summary>SHA-256. High-entropy capability token, so a slow KDF would buy nothing.</summary>
    public required string SummariserTokenHash { get; set; }

    public bool IsPubliclyListed { get; set; }

    public bool IsAcceptingResponses { get; set; } = true;

    public bool AreResponsesPublic { get; set; }

    public ResponseIdentity ResponseIdentity { get; set; } = ResponseIdentity.Required;

    public DateTimeOffset CreatedAt { get; set; }

    public List<Response> Responses { get; set; } = [];

    public List<Summary> Summaries { get; set; } = [];

    public bool IsAnonymous => this.ResponseIdentity == ResponseIdentity.Anonymous;

    /// <summary>
    /// Reopening is allowed only while nothing references the responses. Once a summary
    /// exists the topic stays closed for good; run a new topic instead.
    /// </summary>
    public bool CanReopen => !this.IsAcceptingResponses && this.Summaries.Count == 0;
}
