using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

/// <summary>How one node stands with the group, and where the current viewer sits on it.</summary>
public record NodeReactionTally
{
    public required int Agree { get; init; }

    public required int Important { get; init; }

    public required int Misrepresents { get; init; }

    public required IReadOnlySet<ReactionKind> Mine { get; init; }

    public string? MyNote { get; init; }
}

/// <summary>
/// One "this misrepresents me", carrying the response its author wrote. Reading the two side
/// by side is the whole value of the flag: the objection says the summary got them wrong, and
/// only their own words say what right would have been.
/// </summary>
public record Objection
{
    public required int NodeId { get; init; }

    public required string NodeText { get; init; }

    /// <summary>Null when the objector flagged the node without saying why.</summary>
    public required string? Note { get; init; }

    public required string ResponseBody { get; init; }

    /// <summary>Null on an anonymous survey, or when the responder gave no name.</summary>
    public required string? Author { get; init; }
}

public class ReactionService(WhatYouSayContext db)
{
    /// <summary>Adds the reaction, or takes it back if it was already there.</summary>
    public async Task ToggleAsync(
        Survey survey,
        int nodeId,
        string responderToken,
        ReactionKind kind,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        activity?.SetTag("reaction.kind", kind.ToString());

        var hash = await this.AuthoriseAsync(survey, nodeId, responderToken, activity, cancellationToken);
        var existing = await this.FindAsync(nodeId, hash, kind, cancellationToken);

        if (existing is null)
        {
            this.Add(survey, nodeId, hash, kind, null);
        }
        else
        {
            db.NodeReactions.Remove(existing);
            WhatYouSayTelemetry.ReactionRemoved(survey, kind);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Raises an objection, or rewords one already raised.</summary>
    public async Task SetObjectionAsync(
        Survey survey,
        int nodeId,
        string responderToken,
        string? note,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        var hash = await this.AuthoriseAsync(survey, nodeId, responderToken, activity, cancellationToken);
        var existing = await this.FindAsync(nodeId, hash, ReactionKind.Misrepresents, cancellationToken);
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (existing is null)
        {
            this.Add(survey, nodeId, hash, ReactionKind.Misrepresents, trimmed);
        }
        else
        {
            existing.Note = trimmed;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task WithdrawAsync(
        Survey survey,
        int nodeId,
        string responderToken,
        ReactionKind kind,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        var hash = await this.AuthoriseAsync(survey, nodeId, responderToken, activity, cancellationToken);
        var existing = await this.FindAsync(nodeId, hash, kind, cancellationToken);

        if (existing is null)
        {
            return;
        }

        db.NodeReactions.Remove(existing);
        WhatYouSayTelemetry.ReactionRemoved(survey, kind);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Counts for every node on a summary, plus what this responder has already said. A null
    /// token is a viewer who did not respond: counts only.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, NodeReactionTally>> TallyAsync(
        Guid summaryId,
        string? responderToken,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        var reactions = await db.NodeReactions
            .Where(r => r.Node.SummaryId == summaryId)
            .ToListAsync(cancellationToken);

        var mineHash = responderToken is null ? null : Secrets.HashToken(responderToken);

        return reactions
            .GroupBy(r => r.NodeId)
            .ToDictionary(
                group => group.Key,
                group => Tally(group, mineHash));
    }

    /// <summary>
    /// Every objection raised against a summary, in reading order, for the admin editing it.
    /// </summary>
    /// <remarks>
    /// The join back to the objector's own response is the reaction cookie doing double duty:
    /// it is the permission check on the way in and the link to their words on the way out.
    /// It discloses nothing new — a Required survey carries the name on the response anyway,
    /// and an anonymous one stays a nameless response.
    /// </remarks>
    public async Task<IReadOnlyList<Objection>> ListObjectionsAsync(
        Survey survey,
        Guid summaryId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        var query =
            from reaction in db.NodeReactions
            join response in db.Responses
                on reaction.ResponderTokenHash equals response.AuthTokenHash
            where reaction.Node.SummaryId == summaryId
                && reaction.Kind == ReactionKind.Misrepresents
                && response.SurveyId == survey.Id
                && !response.IsDeleted
            orderby reaction.Node.Ordinal, reaction.NodeId
            select new Objection()
            {
                NodeId = reaction.NodeId,
                NodeText = reaction.Node.Text,
                Note = reaction.Note,
                ResponseBody = response.Body,
                Author = response.Author,
            };

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<bool> CanReactAsync(
        Guid surveyId,
        string? responderToken,
        CancellationToken cancellationToken = default
    )
    {
        if (responderToken is null)
        {
            return false;
        }

        return await this.RespondedAsync(surveyId, Secrets.HashToken(responderToken), cancellationToken);
    }

    private static NodeReactionTally Tally(IEnumerable<NodeReaction> reactions, string? mineHash)
    {
        var all = reactions.ToList();
        var mine = all.Where(r => r.ResponderTokenHash == mineHash).ToList();

        return new NodeReactionTally()
        {
            Agree = all.Count(r => r.Kind == ReactionKind.Agree),
            Important = all.Count(r => r.Kind == ReactionKind.Important),
            Misrepresents = all.Count(r => r.Kind == ReactionKind.Misrepresents),
            Mine = mineHash is null ? new HashSet<ReactionKind>() : [.. mine.Select(r => r.Kind)],
            MyNote = mine.FirstOrDefault(r => r.Kind == ReactionKind.Misrepresents)?.Note,
        };
    }

    private void Add(Survey survey, int nodeId, string hash, ReactionKind kind, string? note)
    {
        db.NodeReactions.Add(new NodeReaction()
        {
            NodeId = nodeId,
            ResponderTokenHash = hash,
            Kind = kind,
            Note = note,
            CreatedAt = survey.IsAnonymous ? null : DateTimeOffset.UtcNow,
        });

        WhatYouSayTelemetry.ReactionAdded(survey, kind);
    }

    private Task<NodeReaction?> FindAsync(
        int nodeId,
        string hash,
        ReactionKind kind,
        CancellationToken cancellationToken
    )
    {
        return db.NodeReactions.FirstOrDefaultAsync(
            r => r.NodeId == nodeId && r.ResponderTokenHash == hash && r.Kind == kind,
            cancellationToken);
    }

    private async Task<string> AuthoriseAsync(
        Survey survey,
        int nodeId,
        string responderToken,
        System.Diagnostics.Activity? activity,
        CancellationToken cancellationToken
    )
    {
        var hash = Secrets.HashToken(responderToken);

        if (!await this.RespondedAsync(survey.Id, hash, cancellationToken))
        {
            activity.RecordFailure("not_a_responder");

            throw new InvalidOperationException(
                "Only people who responded to this survey can react to its summary.");
        }

        var belongs = await db.SummaryNodes.AnyAsync(
            n => n.Id == nodeId && n.Summary.SurveyId == survey.Id,
            cancellationToken);

        if (!belongs)
        {
            activity.RecordFailure("unknown_node");

            throw new InvalidOperationException($"Node {nodeId} is not on a summary of this survey.");
        }

        return hash;
    }

    private async Task<bool> RespondedAsync(Guid surveyId, string hash, CancellationToken cancellationToken)
    {
        return await db.Responses.AnyAsync(
            r => r.SurveyId == surveyId && r.AuthTokenHash == hash && !r.IsDeleted,
            cancellationToken);
    }
}
