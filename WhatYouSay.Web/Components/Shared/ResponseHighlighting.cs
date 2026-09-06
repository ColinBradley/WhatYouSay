using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>One run of a response body, either plain or quoted by at least one node.</summary>
public sealed record BodySegment
{
    public required string Text { get; init; }

    /// <summary>Empty on a plain run. Otherwise every reference selecting exactly this run.</summary>
    public IReadOnlyList<int> ReferenceIds { get; init; } = [];

    /// <summary>
    /// References whose span this run swallowed. Named on the same run, so a citation for an
    /// overlapping quote still lands on something visible.
    /// </summary>
    public IReadOnlyList<int> CoveredReferenceIds { get; init; } = [];
}

public static class ResponseHighlighting
{
    /// <summary>
    /// Every reference this run answers to, as a space-separated list for a `~=` match. One
    /// run can be selected by several references, and can swallow others that overlap it;
    /// all of them have to land here or a citation points at nothing.
    /// </summary>
    public static string Refs(BodySegment segment)
    {
        return string.Join(' ', segment.ReferenceIds.Concat(segment.CoveredReferenceIds));
    }

    /// <summary>
    /// Cuts a response body into plain and quoted runs. Offsets are trusted only when they
    /// still select the stored quote — a reference whose offsets have drifted is dropped from
    /// the highlighting rather than used to slice the body at the wrong place.
    /// </summary>
    public static IReadOnlyList<BodySegment> Segment(
        string body,
        IEnumerable<SummaryNodeReference> references
    )
    {
        var usable = references
            .Where(r => QuoteLocator.Matches(body, r.Quote, r.StartIndex, r.EndIndex))
            .OrderBy(r => r.StartIndex)
            .ThenByDescending(r => r.EndIndex)
            .ToList();

        if (usable.Count == 0)
        {
            return [new BodySegment { Text = body }];
        }

        var segments = new List<BodySegment>();
        var cursor = 0;

        for (var i = 0; i < usable.Count; i++)
        {
            var reference = usable[i];

            if (reference.StartIndex < cursor)
            {
                continue;
            }

            // Everything selecting the identical span shares one mark; anything merely
            // overlapping it gets an empty anchor so its link still lands in the right place.
            var same = new List<int>() { reference.Id, };
            var covered = new List<int>();

            for (var j = i + 1; j < usable.Count && usable[j].StartIndex < reference.EndIndex; j++)
            {
                if (usable[j].StartIndex == reference.StartIndex
                    && usable[j].EndIndex == reference.EndIndex)
                {
                    same.Add(usable[j].Id);
                }
                else
                {
                    covered.Add(usable[j].Id);
                }
            }

            if (reference.StartIndex > cursor)
            {
                segments.Add(new BodySegment { Text = body[cursor..reference.StartIndex] });
            }

            segments.Add(new BodySegment()
            {
                Text = body[reference.StartIndex..reference.EndIndex],
                ReferenceIds = same,
                CoveredReferenceIds = covered,
            });

            cursor = reference.EndIndex;
        }

        if (cursor < body.Length)
        {
            segments.Add(new BodySegment { Text = body[cursor..] });
        }

        return segments;
    }
}
