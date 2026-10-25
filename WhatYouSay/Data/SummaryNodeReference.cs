using System.ComponentModel.DataAnnotations.Schema;

namespace WhatYouSay.Data;

/// <summary>A span of one revision of a response, cited in support of a node.</summary>
public class SummaryNodeReference
{
    public int Id { get; set; }

    public int NodeId { get; set; }

    public SummaryNode Node { get; set; } = null!;

    public Guid ResponseId { get; set; }

    public Response Response { get; set; } = null!;

    /// <summary>
    /// With <see cref="ResponseId"/>, the key of <see cref="Revision"/>.
    /// Because the foreign key spans both columns, a reference can't name another response's
    /// revision.
    /// </summary>
    public int RevisionNumber { get; set; }

    public ResponseRevision Revision { get; set; } = null!;

    public required string Quote { get; set; }

    /// <summary>
    /// An offset into the body of <see cref="Revision"/>, not the response's current wording.
    /// An int rather than a uint, because uint maps badly through EF and SQLite.
    /// </summary>
    public int StartIndex { get; set; }

    public int EndIndex { get; set; }

    /// <summary>
    /// Whether the response still reads as it did when it was quoted.
    /// Requires <see cref="Response"/> to be loaded.
    /// </summary>
    [NotMapped]
    public bool IsOnCurrentRevision =>
        this.RevisionNumber == this.Response.RevisionNumber;
}
