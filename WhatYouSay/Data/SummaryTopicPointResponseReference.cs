namespace WhatYouSay.Data;

/// <summary>
/// A span of a response cited in support of a point. The quote is snapshotted rather than
/// derived, because it is the key the app validates the agent against on write.
/// </summary>
public class SummaryTopicPointResponseReference
{
    public int Id { get; set; }

    public int PointId { get; set; }

    public SummaryTopicPoint Point { get; set; } = null!;

    public Guid ResponseId { get; set; }

    public Response Response { get; set; } = null!;

    public required string Quote { get; set; }

    /// <summary>int rather than uint: uint maps badly through EF and SQLite.</summary>
    public int StartIndex { get; set; }

    public int EndIndex { get; set; }

    /// <summary>0..1, how strongly this quote supports the point.</summary>
    public double? Intensity { get; set; }
}
