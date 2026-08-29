namespace WhatYouSay.Data;

/// <summary>
/// Identity int PK on purpose: insertion order is key order is display order, so there is
/// no ordering concept for a human to manage.
/// </summary>
public class SummaryTopic
{
    public int Id { get; set; }

    public Guid SummaryId { get; set; }

    public Summary Summary { get; set; } = null!;

    public required string Name { get; set; }

    public string? Description { get; set; }

    public List<SummaryTopicPoint> Points { get; set; } = [];
}
