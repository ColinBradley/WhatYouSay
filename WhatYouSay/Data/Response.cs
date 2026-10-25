using System.ComponentModel.DataAnnotations.Schema;

namespace WhatYouSay.Data;

public class Response
{
    public Guid Id { get; set; }

    public Guid TopicId { get; set; }

    public Topic Topic { get; set; } = null!;

    /// <summary>Self-declared and unverified. Never collected when the topic is Anonymous.</summary>
    public string? Author { get; set; }

    /// <summary>SHA-256 of the plaintext token held in the responder's cookie.</summary>
    public required string AuthTokenHash { get; set; }

    /// <summary>
    /// Stops the responder editing or withdrawing the response. Only the admin sets or clears it.
    /// </summary>
    public bool IsFrozen { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>
    /// The number of the current revision. <see cref="Revise"/> maintains it, so don't set it.
    /// </summary>
    public int RevisionNumber { get; set; }

    /// <summary>Every wording the response has had. The response's text exists only here.</summary>
    public List<ResponseRevision> Revisions { get; set; } = [];

    public List<SummaryNodeReference> References { get; set; } = [];

    /// <summary>The current wording. Requires <see cref="Revisions"/> to be loaded.</summary>
    [NotMapped]
    public ResponseRevision Current =>
        this.Revision(this.RevisionNumber);

    /// <summary>
    /// When the response was first submitted. Null when the topic records no timing.
    /// Requires <see cref="Revisions"/> to be loaded.
    /// </summary>
    [NotMapped]
    public DateTimeOffset? SubmittedAt =>
        this.Revision(ResponseRevision.First).CreatedAt;

    /// <summary>Whether the wording has changed since the response was submitted.</summary>
    [NotMapped]
    public bool IsEdited =>
        this.RevisionNumber > ResponseRevision.First;

    /// <summary>
    /// Makes <paramref name="body"/> the current wording, after normalising it.
    /// Earlier wordings are kept, because citations may quote them.
    /// Returns false, and changes nothing, when the normalised body matches the current wording.
    /// Requires <see cref="Revisions"/> to be loaded.
    /// </summary>
    public bool Revise(string body, DateTimeOffset? at)
    {
        var normalised = ResponseBody.Normalise(body);

        if (this.RevisionNumber > 0 && this.Current.Body == normalised)
        {
            return false;
        }

        this.RevisionNumber++;

        this.Revisions.Add(new ResponseRevision()
        {
            Number = this.RevisionNumber,
            Body = normalised,
            CreatedAt = at,
        });

        return true;
    }

    private ResponseRevision Revision(int number)
    {
        return this.Revisions.FirstOrDefault(revision => revision.Number == number)
            ?? throw new InvalidOperationException(
                $"Revision {number} of response {this.Id} is not loaded. Include Revisions in the query.");
    }
}
