namespace WhatYouSay.Data;

/// <summary>A span of a response cited in support of a node.</summary>
public class SummaryNodeReference
{
    public int Id { get; set; }

    public int NodeId { get; set; }

    public SummaryNode Node { get; set; } = null!;

    public Guid ResponseId { get; set; }

    public Response Response { get; set; } = null!;

    public required string Quote { get; set; }

    /// <summary>int rather than uint: uint maps badly through EF and SQLite.</summary>
    public int StartIndex { get; set; }

    public int EndIndex { get; set; }
}
