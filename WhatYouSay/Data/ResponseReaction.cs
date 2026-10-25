namespace WhatYouSay.Data;

/// <summary>
/// One reactor's reaction of one kind to a response.
/// It belongs to the response rather than to a revision, so editing the response keeps it.
/// </summary>
public class ResponseReaction
{
    public int Id { get; set; }

    public Guid ResponseId { get; set; }

    public Response Response { get; set; } = null!;

    /// <summary>SHA-256 of the responder token in the reactor's cookie for this topic.</summary>
    public required string ReactorTokenHash { get; set; }

    public ReactionKind Kind { get; set; }

    /// <summary>Null when the topic records no timing.</summary>
    public DateTimeOffset? CreatedAt { get; set; }
}
