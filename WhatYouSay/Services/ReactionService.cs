using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

/// <summary>How one node stands with the group, and where the current viewer sits on it.</summary>
public record NodeReactionTally
{
    public required IReadOnlyDictionary<ReactionKind, int> Counts { get; init; }

    public required IReadOnlySet<ReactionKind> Mine { get; init; }

    public int CountOf(ReactionKind kind) =>
        this.Counts.TryGetValue(kind, out var count) ? count : 0;
}

public class ReactionService(WhatYouSayContext db)
{
    /// <summary>Adds the reaction, or takes it back if it was already there.</summary>
    public async Task ToggleAsync(
        Topic topic,
        int nodeId,
        string reactorToken,
        ReactionKind kind,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        activity?.SetTag("reaction.kind", kind.ToString());

        await CommentService.RequireNodeAsync(db, topic, nodeId, activity, cancellationToken);

        var hash = Secrets.HashToken(reactorToken);
        var existing = await db.NodeReactions.FirstOrDefaultAsync(
            r => r.NodeId == nodeId && r.ReactorTokenHash == hash && r.Kind == kind,
            cancellationToken);

        if (existing is null)
        {
            db.NodeReactions.Add(new NodeReaction()
            {
                NodeId = nodeId,
                ReactorTokenHash = hash,
                Kind = kind,
                CreatedAt = topic.IsAnonymous ? null : DateTimeOffset.UtcNow,
            });

            WhatYouSayTelemetry.ReactionAdded(topic, kind);
        }
        else
        {
            db.NodeReactions.Remove(existing);
            WhatYouSayTelemetry.ReactionRemoved(topic, kind);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Counts for every node on a summary, plus what this viewer has already clicked. A null
    /// token is someone who has never reacted here: counts only.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, NodeReactionTally>> TallyAsync(
        Guid summaryId,
        string? reactorToken,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        var reactions = await db.NodeReactions
            .Where(r => r.Node.SummaryId == summaryId)
            .ToListAsync(cancellationToken);

        var mineHash = reactorToken is null ? null : Secrets.HashToken(reactorToken);

        return reactions
            .GroupBy(r => r.NodeId)
            .ToDictionary(group => group.Key, group => Tally(group, mineHash));
    }

    private static NodeReactionTally Tally(IEnumerable<NodeReaction> reactions, string? mineHash)
    {
        var all = reactions.ToList();

        return new NodeReactionTally()
        {
            Counts = all
                .GroupBy(r => r.Kind)
                .ToDictionary(group => group.Key, group => group.Count()),
            Mine = mineHash is null
                ? new HashSet<ReactionKind>()
                : [.. all.Where(r => r.ReactorTokenHash == mineHash).Select(r => r.Kind)],
        };
    }
}
