namespace WhatYouSay.Data;

public class SummaryTopicPoint
{
    public int Id { get; set; }

    public int TopicId { get; set; }

    public SummaryTopic Topic { get; set; } = null!;

    public required string Description { get; set; }

    /// <summary>-1 (negative) to +1 (positive). Collected from day one, not rendered in v1.</summary>
    public double? Sentiment { get; set; }

    /// <summary>0 (pure opinion) to 1 (verifiable fact). Collected, not rendered in v1.</summary>
    public double? Objectivity { get; set; }

    /// <summary>Never empty: a point with no citation is a theme nobody raised.</summary>
    public List<SummaryTopicPointResponseReference> References { get; set; } = [];

    public List<PointReaction> Reactions { get; set; } = [];
}
