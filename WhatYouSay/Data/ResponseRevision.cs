namespace WhatYouSay.Data;

/// <summary>
/// The exact wording of a response at one point in its life. Citations name a revision, so
/// an edit to the response never changes what an earlier citation quoted.
/// </summary>
/// <remarks>
/// Only <see cref="Response.Revise"/> creates one, and nothing changes one afterwards.
/// Revisions are internal evidence, not a public edit history.
/// </remarks>
public class ResponseRevision
{
    /// <summary>The number every response's first revision takes.</summary>
    public const int First = 1;

    public Guid ResponseId { get; set; }

    public Response Response { get; set; } = null!;

    /// <summary>
    /// Counts up from <see cref="First"/> within one response.
    /// Because the count is per response, it shows how often that response was edited but
    /// nothing about when anyone answered.
    /// </summary>
    public int Number { get; set; }

    /// <summary>Normalised by <see cref="ResponseBody.Normalise"/>.</summary>
    public required string Body { get; set; }

    /// <summary>
    /// When this wording was submitted. Null when the topic records no timing.
    /// The time is not collected, rather than collected and hidden.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; set; }
}
