using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

/// <summary>
/// How one node or response stands with the group, and where the current viewer sits on it.
/// </summary>
public record ReactionTally
{
    /// <summary>The tally of something nobody has reacted to.</summary>
    public static ReactionTally None { get; } = new()
    {
        Counts = new Dictionary<ReactionKind, int>(),
        Mine = new HashSet<ReactionKind>(),
    };

    public required IReadOnlyDictionary<ReactionKind, int> Counts { get; init; }

    public required IReadOnlySet<ReactionKind> Mine { get; init; }

    public int CountOf(ReactionKind kind) =>
        this.Counts.TryGetValue(kind, out var count) ? count : 0;
}

public class ReactionService
{
    /// <summary>SQLite's extended result code for a broken unique constraint.</summary>
    private const int SqliteConstraintUnique = 2067;

    private readonly WhatYouSayContext mDb;

    public ReactionService(WhatYouSayContext db)
    {
        mDb = db;
    }

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

        await CommentService.RequireNodeAsync(mDb, topic, nodeId, activity, cancellationToken);

        var hash = Secrets.HashToken(reactorToken);
        var existing = await mDb.NodeReactions.FirstOrDefaultAsync(
            r => r.NodeId == nodeId && r.ReactorTokenHash == hash && r.Kind == kind,
            cancellationToken);

        if (existing is null)
        {
            mDb.NodeReactions.Add(new NodeReaction()
            {
                NodeId = nodeId,
                ReactorTokenHash = hash,
                Kind = kind,
                CreatedAt = topic.RecordsTiming ? DateTimeOffset.UtcNow : null,
            });

            WhatYouSayTelemetry.ReactionAdded(topic, ReactionTarget.Node, kind);
        }
        else
        {
            mDb.NodeReactions.Remove(existing);
            WhatYouSayTelemetry.ReactionRemoved(topic, ReactionTarget.Node, kind);
        }

        await mDb.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Adds the reaction to a response, or takes it back if it was already there.
    /// Throws <see cref="InvalidOperationException"/> unless the response is live and belongs
    /// to the topic, so a withdrawn or deleted response takes no new reactions.
    /// </summary>
    /// <remarks>
    /// Two toggles of the same reaction by the same reactor can both read the state before
    /// either saves. The one that saves second finds its change already made and stops, so a
    /// double click counts once and never stores a duplicate.
    /// </remarks>
    public async Task ToggleOnResponseAsync(
        Topic topic,
        Guid responseId,
        string reactorToken,
        ReactionKind kind,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        activity?.SetTag("reaction.kind", kind.ToString());

        var live = await mDb.Responses.AnyAsync(
            r => r.Id == responseId && r.TopicId == topic.Id && !r.IsDeleted,
            cancellationToken);

        if (!live)
        {
            activity.RecordFailure("unknown_response");

            throw new InvalidOperationException(
                "That response has been withdrawn or is not on this topic, so it can't be reacted to.");
        }

        var hash = Secrets.HashToken(reactorToken);
        var existing = await mDb.ResponseReactions.FirstOrDefaultAsync(
            r => r.ResponseId == responseId && r.ReactorTokenHash == hash && r.Kind == kind,
            cancellationToken);

        if (existing is null)
        {
            var added = new ResponseReaction()
            {
                ResponseId = responseId,
                ReactorTokenHash = hash,
                Kind = kind,
                CreatedAt = topic.RecordsTiming ? DateTimeOffset.UtcNow : null,
            };

            mDb.ResponseReactions.Add(added);

            try
            {
                await mDb.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException failure)
                when (failure.InnerException is SqliteException { SqliteExtendedErrorCode: SqliteConstraintUnique })
            {
                mDb.Entry(added).State = EntityState.Detached;

                return;
            }

            WhatYouSayTelemetry.ReactionAdded(topic, ReactionTarget.Response, kind);
        }
        else
        {
            mDb.ResponseReactions.Remove(existing);

            try
            {
                await mDb.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                mDb.Entry(existing).State = EntityState.Detached;

                return;
            }

            WhatYouSayTelemetry.ReactionRemoved(topic, ReactionTarget.Response, kind);
        }
    }

    /// <summary>
    /// Counts for every node on a summary, plus what this viewer has already clicked. A null
    /// token is someone who has never reacted here: counts only.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, ReactionTally>> TallyAsync(
        Guid summaryId,
        string? reactorToken,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        var reactions = await mDb.NodeReactions
            .Where(r => r.Node.SummaryId == summaryId)
            .Select(r => new { r.NodeId, r.Kind, r.ReactorTokenHash })
            .ToListAsync(cancellationToken);

        var mineHash = reactorToken is null ? null : Secrets.HashToken(reactorToken);

        return reactions
            .GroupBy(r => r.NodeId)
            .ToDictionary(
                group => group.Key,
                group => Tally([.. group.Select(r => (r.Kind, r.ReactorTokenHash))], mineHash));
    }

    /// <summary>
    /// Counts for every live response on the topic, plus what this viewer has already clicked.
    /// A response with no reactions has no entry, and neither has a withdrawn one.
    /// A null token is someone who has never reacted here: counts only.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, ReactionTally>> TallyResponsesAsync(
        Topic topic,
        string? reactorToken,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        var reactions = await mDb.ResponseReactions
            .Where(r => r.Response.TopicId == topic.Id && !r.Response.IsDeleted)
            .Select(r => new { r.ResponseId, r.Kind, r.ReactorTokenHash })
            .ToListAsync(cancellationToken);

        var mineHash = reactorToken is null ? null : Secrets.HashToken(reactorToken);

        return reactions
            .GroupBy(r => r.ResponseId)
            .ToDictionary(
                group => group.Key,
                group => Tally([.. group.Select(r => (r.Kind, r.ReactorTokenHash))], mineHash));
    }

    private static ReactionTally Tally(
        IReadOnlyList<(ReactionKind Kind, string ReactorTokenHash)> reactions,
        string? mineHash
    )
    {
        return new ReactionTally()
        {
            Counts = reactions
                .GroupBy(r => r.Kind)
                .ToDictionary(group => group.Key, group => group.Count()),
            Mine = mineHash is null
                ? new HashSet<ReactionKind>()
                : [.. reactions.Where(r => r.ReactorTokenHash == mineHash).Select(r => r.Kind)],
        };
    }
}
