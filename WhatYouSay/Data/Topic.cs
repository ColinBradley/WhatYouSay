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

    /// <summary>
    /// Whether the summariser's writes to the live summary are published straight away.
    /// Defaults to Reviewed. Topics created through <c>TopicAdminService</c> start Live.
    /// </summary>
    public SummaryPolicy SummaryPolicy { get; set; }

    public ResponseIdentity ResponseIdentity { get; set; } = ResponseIdentity.Required;

    public ResponseTiming ResponseTiming { get; set; } = ResponseTiming.Recorded;

    /// <summary>
    /// Counts changes to what the summariser reads, starting at 1.
    /// Database triggers move it in the same statement as each change, so nothing in code sets it.
    /// A tracked topic keeps the value it was loaded with, so read the current one with a projection.
    /// </summary>
    public int InputRevision { get; private set; } = 1;

    /// <summary>What kind of feedback the admin expects, such as questions for a panel. Summariser guidance.</summary>
    public string? ExpectedFeedback { get; set; }

    /// <summary>How the admin wants the feedback summarised. Summariser guidance.</summary>
    public string? SummarisingInstructions { get; set; }

    /// <summary>
    /// How long the admin would like the summary to be, in their own words. Summariser guidance,
    /// not a limit anything enforces.
    /// </summary>
    public string? SummaryLengthTarget { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<Response> Responses { get; set; } = [];

    public List<Summary> Summaries { get; set; } = [];

    public bool IsAnonymous => this.ResponseIdentity == ResponseIdentity.Anonymous;

    public bool RecordsTiming => this.ResponseTiming == ResponseTiming.Recorded;

    /// <summary>Identity is only unchangeable once somebody has answered under it.</summary>
    public bool CanChangeIdentity => this.Responses.Count == 0;
}
