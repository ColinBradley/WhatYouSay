using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

/// <summary>Everything the summariser reads about a topic, as it stood at one moment.</summary>
public record SummariserInput
{
    /// <summary>The topic, read with everything else, so its guidance and input revision match the rest. Untracked.</summary>
    public required Topic Topic { get; init; }

    /// <summary>Every live response in listing order, each with its revisions loaded.</summary>
    public required IReadOnlyList<Response> Responses { get; init; }

    /// <summary>Reaction counts by response id. A response nobody reacted to has no entry.</summary>
    public required IReadOnlyDictionary<Guid, ReactionTally> ResponseReactions { get; init; }

    /// <summary>The live summary with its current tree assembled. Null when the topic has none.</summary>
    public Summary? LiveSummary { get; init; }

    /// <summary>Reaction counts by node id on the live summary. Empty when there is no live summary.</summary>
    public required IReadOnlyDictionary<int, ReactionTally> NodeReactions { get; init; }

    /// <summary>
    /// Visible comments on the live summary's current tree, in reading order.
    /// Empty when there is no live summary.
    /// </summary>
    public required IReadOnlyList<NodeCommentView> Comments { get; init; }

    /// <summary>
    /// Whether a live write would be accepted now.
    /// It would when the topic is live and its live summary, if it has one, is not paused.
    /// </summary>
    public bool IsWritable =>
        IsOpen(this.Topic.SummaryPolicy, this.LiveSummary?.IsAgentEditable);

    /// <summary>
    /// Whether there is input the live summary was not generated from.
    /// Always false while there are no responses, because there is nothing to cite.
    /// </summary>
    public bool IsPending
    {
        get
        {
            if (this.Responses.Count == 0)
            {
                return false;
            }

            return this.LiveSummary?.AppliedInputRevision is not { } applied
                || applied < this.Topic.InputRevision;
        }
    }

    /// <summary>Whether a topic with this policy and live summary takes live writes. A null access means no live summary.</summary>
    internal static bool IsOpen(SummaryPolicy policy, bool? liveSummaryIsAgentEditable)
    {
        return policy == SummaryPolicy.Live && liveSummaryIsAgentEditable != false;
    }
}

/// <summary>A consistent read of a topic's summariser input, or only its version when the caller already holds it.</summary>
public record SummariserRead
{
    /// <summary>
    /// Identifies the state of everything the read covers.
    /// It is made of the input revision, and the live summary's id and revision, and whether a live write would be accepted.
    /// Two reads with the same version hold the same input.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>Null when the caller already holds <see cref="Version"/>.</summary>
    public SummariserInput? Input { get; init; }
}

/// <summary>Every live response on a topic with its reaction counts, and the input revision they belong to.</summary>
public record ResponseListing
{
    public required int InputRevision { get; init; }

    /// <summary>Every live response in listing order, each with its revisions loaded.</summary>
    public required IReadOnlyList<Response> Responses { get; init; }

    /// <summary>Reaction counts by response id. A response nobody reacted to has no entry.</summary>
    public required IReadOnlyDictionary<Guid, ReactionTally> Reactions { get; init; }
}

/// <summary>
/// Reads what the summariser works from. Each read runs its queries inside one SQLite
/// transaction, so it never mixes a response from before a change with a count from after it.
/// </summary>
/// <remarks>
/// Reads through the context's tracker, so use a context that has not already loaded the
/// responses or the live summary. A request's own context has not.
/// </remarks>
public class SummariserInputService
{
    private readonly WhatYouSayContext mDb;

    public SummariserInputService(WhatYouSayContext db)
    {
        mDb = db;
    }

    /// <summary>
    /// Reads the topic's whole summariser input. Returns only the version when it is one of
    /// <paramref name="knownVersions"/>, so an unchanged poll loads no responses.
    /// </summary>
    public async Task<SummariserRead> ReadAsync(
        Topic topic,
        IReadOnlyCollection<string> knownVersions,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        return await this.InOneReadAsync(
            async () =>
            {
                var current = await mDb.Topics
                    .AsNoTracking()
                    .SingleAsync(t => t.Id == topic.Id, cancellationToken);

                var live = await mDb.Summaries
                    .Where(s => s.TopicId == topic.Id && s.IsLive)
                    .Select(s => new { s.Id, s.Revision, s.IsAgentEditable })
                    .FirstOrDefaultAsync(cancellationToken);

                var open = SummariserInput.IsOpen(current.SummaryPolicy, live?.IsAgentEditable);
                var version = $"{current.InputRevision}.{live?.Id.ToString("N") ?? "none"}."
                    + $"{live?.Revision ?? 0}.{(open ? "open" : "closed")}";

                activity?.SetTag("summariser.input_revision", current.InputRevision);

                if (knownVersions.Contains(version))
                {
                    activity?.SetTag("summariser.unchanged", true);

                    return new SummariserRead() { Version = version };
                }

                var responses = await new ResponseService(mDb).ListAsync(topic, cancellationToken);
                var reactions = new ReactionService(mDb);
                var tallies = await reactions.TallyResponsesAsync(topic, null, cancellationToken);

                var summary = live is null
                    ? null
                    : await new SummaryService(mDb).FindAsync(live.Id, cancellationToken);

                var input = new SummariserInput()
                {
                    Topic = current,
                    Responses = responses,
                    ResponseReactions = tallies,
                    LiveSummary = summary,
                    NodeReactions = summary is null
                        ? new Dictionary<int, ReactionTally>()
                        : await reactions.TallyAsync(summary.Id, null, cancellationToken),
                    Comments = summary is null
                        ? []
                        : await new CommentService(mDb).ListAsync(topic, summary.Id, null, false, cancellationToken),
                };

                activity?.SetTag("response.count", responses.Count);

                return new SummariserRead() { Version = version, Input = input };
            },
            cancellationToken);
    }

    /// <summary>Lists every live response with its reaction counts and the input revision, in one consistent read.</summary>
    public async Task<ResponseListing> ListResponsesAsync(
        Topic topic,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        return await this.InOneReadAsync(
            async () => new ResponseListing()
            {
                InputRevision = await mDb.Topics
                    .Where(t => t.Id == topic.Id)
                    .Select(t => t.InputRevision)
                    .SingleAsync(cancellationToken),
                Responses = await new ResponseService(mDb).ListAsync(topic, cancellationToken),
                Reactions = await new ReactionService(mDb).TallyResponsesAsync(topic, null, cancellationToken),
            },
            cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="read"/> inside one deferred transaction, so every query in it sees
    /// the same committed state. Deferred, because SQLite's default immediate transaction also
    /// takes the write lock, and would hold up responders for the length of a read.
    /// </summary>
    private async Task<T> InOneReadAsync<T>(Func<Task<T>> read, CancellationToken cancellationToken)
    {
        await mDb.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            var connection = (SqliteConnection)mDb.Database.GetDbConnection();

            // Nothing is written, so disposing the transaction without committing ends it cleanly.
            await using var transaction = connection.BeginTransaction(deferred: true);

            await mDb.Database.UseTransactionAsync(transaction, cancellationToken);

            try
            {
                return await read();
            }
            finally
            {
                await mDb.Database.UseTransactionAsync(null, cancellationToken);
            }
        }
        finally
        {
            await mDb.Database.CloseConnectionAsync();
        }
    }
}
