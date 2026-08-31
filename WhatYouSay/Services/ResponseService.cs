using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

public class ResponseService(WhatYouSayContext db)
{
    /// <summary>
    /// Stores a response and returns the plaintext token for the responder's cookie. Only
    /// the hash is kept, so this is the one moment the token is knowable.
    /// </summary>
    public async Task<string> SubmitAsync(
        Survey survey,
        string body,
        string? author,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        if (!survey.IsAcceptingResponses)
        {
            activity.RecordFailure("survey_closed");

            throw new InvalidOperationException("This survey is no longer accepting responses.");
        }

        var token = Secrets.NewToken();

        db.Responses.Add(new Response()
        {
            // Deliberately v4 and not CreateVersion7. A v7 Guid embeds a Unix timestamp,
            // so it would both restore submission order and leak roughly when someone
            // answered — undoing the whole point of not recording CreatedAt.
            Id = Guid.NewGuid(),
            SurveyId = survey.Id,
            Body = body.Trim(),
            Author = survey.IsAnonymous ? null : NullIfBlank(author),
            AuthTokenHash = Secrets.HashToken(token),
            CreatedAt = survey.IsAnonymous ? null : DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);

        WhatYouSayTelemetry.ResponseSubmitted(survey);

        return token;
    }

    public async Task<Response?> FindOwnAsync(
        Guid surveyId,
        string token,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        var hash = Secrets.HashToken(token);

        return await db.Responses.FirstOrDefaultAsync(
            r => r.SurveyId == surveyId && r.AuthTokenHash == hash && !r.IsDeleted,
            cancellationToken);
    }

    /// <summary>Editable only while the survey is open, so summary quotes cannot rot.</summary>
    public async Task EditAsync(
        Survey survey,
        Response response,
        string body,
        string? author,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        if (!survey.IsAcceptingResponses)
        {
            activity.RecordFailure("survey_closed");

            throw new InvalidOperationException("This survey is closed, so responses are frozen.");
        }

        response.Body = body.Trim();
        response.Author = survey.IsAnonymous ? null : NullIfBlank(author);
        response.UpdatedAt = survey.IsAnonymous ? null : DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        WhatYouSayTelemetry.ResponseEdited(survey);
    }

    public async Task WithdrawAsync(
        Survey survey,
        Response response,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        if (!survey.IsAcceptingResponses)
        {
            activity.RecordFailure("survey_closed");

            throw new InvalidOperationException("This survey is closed, so responses are frozen.");
        }

        response.IsDeleted = true;

        await db.SaveChangesAsync(cancellationToken);

        WhatYouSayTelemetry.ResponseWithdrawn(survey);
    }

    public async Task<IReadOnlyList<Response>> ListAsync(
        Survey survey,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        var query = db.Responses.Where(r => r.SurveyId == survey.Id && !r.IsDeleted);

        // Anonymous surveys have no timestamps to order by, so they fall back to the
        // random Guid. Insertion order would otherwise leak through the SQLite rowid.
        query = survey.IsAnonymous
            ? query.OrderBy(r => r.Id)
            : query.OrderBy(r => r.CreatedAt);

        return await query.ToListAsync(cancellationToken);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
