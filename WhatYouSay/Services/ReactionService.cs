using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

/// <summary>How one point stands with the group, and where the current viewer sits on it.</summary>
public record PointReactionTally
{
    public required int Agree { get; init; }

    public required int Important { get; init; }

    public required int Misrepresents { get; init; }

    public required IReadOnlySet<ReactionKind> Mine { get; init; }

    public string? MyNote { get; init; }
}

public class ReactionService(WhatYouSayContext db)
{
    /// <summary>Adds the reaction, or takes it back if it was already there.</summary>
    public async Task ToggleAsync(
        Survey survey,
        int pointId,
        string responderToken,
        ReactionKind kind,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        activity?.SetTag("reaction.kind", kind.ToString());

        var hash = await this.AuthoriseAsync(survey, pointId, responderToken, activity, cancellationToken);
        var existing = await this.FindAsync(pointId, hash, kind, cancellationToken);

        if (existing is null)
        {
            this.Add(survey, pointId, hash, kind, null);
        }
        else
        {
            db.PointReactions.Remove(existing);
            WhatYouSayTelemetry.ReactionRemoved(survey, kind);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Raises an objection, or rewords one already raised.</summary>
    public async Task SetObjectionAsync(
        Survey survey,
        int pointId,
        string responderToken,
        string? note,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        var hash = await this.AuthoriseAsync(survey, pointId, responderToken, activity, cancellationToken);
        var existing = await this.FindAsync(pointId, hash, ReactionKind.Misrepresents, cancellationToken);
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (existing is null)
        {
            this.Add(survey, pointId, hash, ReactionKind.Misrepresents, trimmed);
        }
        else
        {
            existing.Note = trimmed;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task WithdrawAsync(
        Survey survey,
        int pointId,
        string responderToken,
        ReactionKind kind,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        var hash = await this.AuthoriseAsync(survey, pointId, responderToken, activity, cancellationToken);
        var existing = await this.FindAsync(pointId, hash, kind, cancellationToken);

        if (existing is null)
        {
            return;
        }

        db.PointReactions.Remove(existing);
        WhatYouSayTelemetry.ReactionRemoved(survey, kind);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Counts for every point on a summary, plus what this responder has already said.
    /// A null token is a viewer who did not respond: counts only.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, PointReactionTally>> TallyAsync(
        Guid summaryId,
        string? responderToken,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        var reactions = await db.PointReactions
            .Where(r => r.Point.Topic.SummaryId == summaryId)
            .ToListAsync(cancellationToken);

        var mineHash = responderToken is null ? null : Secrets.HashToken(responderToken);

        return reactions
            .GroupBy(r => r.PointId)
            .ToDictionary(
                group => group.Key,
                group => Tally(group, mineHash));
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

    private static PointReactionTally Tally(IEnumerable<PointReaction> reactions, string? mineHash)
    {
        var all = reactions.ToList();
        var mine = all.Where(r => r.ResponderTokenHash == mineHash).ToList();

        return new PointReactionTally()
        {
            Agree = all.Count(r => r.Kind == ReactionKind.Agree),
            Important = all.Count(r => r.Kind == ReactionKind.Important),
            Misrepresents = all.Count(r => r.Kind == ReactionKind.Misrepresents),
            Mine = mineHash is null ? new HashSet<ReactionKind>() : [.. mine.Select(r => r.Kind)],
            MyNote = mine.FirstOrDefault(r => r.Kind == ReactionKind.Misrepresents)?.Note,
        };
    }

    private void Add(Survey survey, int pointId, string hash, ReactionKind kind, string? note)
    {
        db.PointReactions.Add(new PointReaction()
        {
            PointId = pointId,
            ResponderTokenHash = hash,
            Kind = kind,
            Note = note,
            CreatedAt = survey.IsAnonymous ? null : DateTimeOffset.UtcNow,
        });

        WhatYouSayTelemetry.ReactionAdded(survey, kind);
    }

    private Task<PointReaction?> FindAsync(
        int pointId,
        string hash,
        ReactionKind kind,
        CancellationToken cancellationToken
    )
    {
        return db.PointReactions.FirstOrDefaultAsync(
            r => r.PointId == pointId && r.ResponderTokenHash == hash && r.Kind == kind,
            cancellationToken);
    }

    private async Task<string> AuthoriseAsync(
        Survey survey,
        int pointId,
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

        if (!await this.PointBelongsAsync(survey.Id, pointId, cancellationToken))
        {
            activity.RecordFailure("unknown_point");

            throw new InvalidOperationException($"Point {pointId} is not on a summary of this survey.");
        }

        return hash;
    }

    private async Task<bool> RespondedAsync(Guid surveyId, string hash, CancellationToken cancellationToken)
    {
        return await db.Responses.AnyAsync(
            r => r.SurveyId == surveyId && r.AuthTokenHash == hash && !r.IsDeleted,
            cancellationToken);
    }

    private async Task<bool> PointBelongsAsync(Guid surveyId, int pointId, CancellationToken cancellationToken)
    {
        return await db.SummaryTopicPoints.AnyAsync(
            p => p.Id == pointId && p.Topic.Summary.SurveyId == surveyId,
            cancellationToken);
    }
}
