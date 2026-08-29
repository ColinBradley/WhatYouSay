using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

public record SurveyListing
{
    public required Survey Survey { get; init; }

    public required int ResponseCount { get; init; }

    public required bool HasVisibleSummary { get; init; }
}

public class SurveyService(WhatYouSayContext db)
{
    public async Task<Survey?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await db.Surveys.FirstOrDefaultAsync(s => s.Code == code, cancellationToken);
    }

    public async Task<IReadOnlyList<SurveyListing>> ListPubliclyListedAsync(
        CancellationToken cancellationToken = default)
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await db.Surveys
            .Where(s => s.IsPubliclyListed)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SurveyListing
            {
                Survey = s,
                ResponseCount = s.Responses.Count(r => !r.IsDeleted),
                HasVisibleSummary = s.Summaries.Any(x => !x.IsDraft && x.IsPublic)
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountResponsesAsync(Guid surveyId, CancellationToken cancellationToken = default)
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await db.Responses.CountAsync(
            r => r.SurveyId == surveyId && !r.IsDeleted,
            cancellationToken);
    }
}
