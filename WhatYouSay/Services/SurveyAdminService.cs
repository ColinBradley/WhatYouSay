using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

/// <summary>A survey and the one-time secrets that only exist at creation.</summary>
public record CreatedSurvey
{
    public required Survey Survey { get; init; }

    /// <summary>Shown once on the confirmation screen; only its hash is stored.</summary>
    public required string SummariserToken { get; init; }
}

public class SurveyAdminService(WhatYouSayContext db)
{
    public async Task<CreatedSurvey> CreateAsync(
        string title,
        string prompt,
        string adminPassword,
        ResponseIdentity identity,
        bool isPubliclyListed,
        bool areResponsesPublic,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        var token = Secrets.NewToken();

        var survey = new Survey()
        {
            Id = Guid.CreateVersion7(),
            Code = await this.UniqueCodeAsync(cancellationToken),
            Title = title.Trim(),
            Description = prompt.Trim(),
            AdminPasswordHash = Secrets.HashPassword(adminPassword),
            SummariserTokenHash = Secrets.HashToken(token),
            ResponseIdentity = identity,
            IsPubliclyListed = isPubliclyListed,
            AreResponsesPublic = areResponsesPublic,
            IsAcceptingResponses = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Surveys.Add(survey);
        await db.SaveChangesAsync(cancellationToken);

        WhatYouSayTelemetry.SurveyCreated(survey);

        return new CreatedSurvey { Survey = survey, SummariserToken = token };
    }

    public bool CheckPassword(Survey survey, string password)
    {
        return Secrets.VerifyPassword(survey.AdminPasswordHash, password);
    }

    public async Task SetAcceptingResponsesAsync(
        Survey survey,
        bool accepting,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        if (accepting && !survey.CanReopen)
        {
            activity.RecordFailure("summary_exists");

            throw new InvalidOperationException(
                "A summary has been generated, so this survey stays closed. Run a new survey instead.");
        }

        survey.IsAcceptingResponses = accepting;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateSettingsAsync(
        Survey survey,
        bool isPubliclyListed,
        bool areResponsesPublic,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        survey.IsPubliclyListed = isPubliclyListed;
        survey.AreResponsesPublic = areResponsesPublic;

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Returns the new token; the old one stops working immediately.</summary>
    public async Task<string> RegenerateSummariserTokenAsync(
        Survey survey,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        var token = Secrets.NewToken();
        survey.SummariserTokenHash = Secrets.HashToken(token);

        await db.SaveChangesAsync(cancellationToken);

        return token;
    }

    public async Task DeleteResponseAsync(
        Survey survey,
        Guid responseId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        var response = await db.Responses.FirstOrDefaultAsync(
            r => r.Id == responseId && r.SurveyId == survey.Id,
            cancellationToken);

        if (response is null)
        {
            return;
        }

        response.IsDeleted = true;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetSummaryVisibilityAsync(
        Survey survey,
        Guid summaryId,
        bool published,
        bool isPublic,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        var summary = await this.RequireSummaryAsync(survey, summaryId, cancellationToken);

        summary.IsDraft = !published;
        summary.IsPublic = isPublic;
        summary.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteSummaryAsync(
        Survey survey,
        Guid summaryId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        var summary = await this.RequireSummaryAsync(survey, summaryId, cancellationToken);

        db.Summaries.Remove(summary);

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Summary> RequireSummaryAsync(
        Survey survey,
        Guid summaryId,
        CancellationToken cancellationToken
    )
    {
        return await db.Summaries.FirstOrDefaultAsync(
                s => s.Id == summaryId && s.SurveyId == survey.Id,
                cancellationToken)
            ?? throw new InvalidOperationException($"No summary {summaryId} on this survey.");
    }

    private async Task<string> UniqueCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = Secrets.NewSurveyCode();

            if (!await db.Surveys.AnyAsync(s => s.Code == code, cancellationToken))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not find a free survey code.");
    }
}
