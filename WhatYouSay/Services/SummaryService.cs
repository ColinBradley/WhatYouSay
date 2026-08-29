using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

public class SummaryService(WhatYouSayContext db)
{
    /// <summary>
    /// The newest summary a non-admin is allowed to see: blessed by a human and published.
    /// </summary>
    public async Task<Summary?> FindLatestVisibleAsync(
        Guid surveyId,
        CancellationToken cancellationToken = default)
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await this.Detailed()
            .Where(s => s.SurveyId == surveyId && !s.IsDraft && s.IsPublic)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Summary?> FindAsync(Guid summaryId, CancellationToken cancellationToken = default)
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await this.Detailed().FirstOrDefaultAsync(s => s.Id == summaryId, cancellationToken);
    }

    public async Task<IReadOnlyList<Summary>> ListVisibleAsync(
        Guid surveyId,
        CancellationToken cancellationToken = default)
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await db.Summaries
            .Where(s => s.SurveyId == surveyId && !s.IsDraft && s.IsPublic)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Loads the whole tree. Deleted responses are filtered out of references here rather
    /// than at render time, so no caller can accidentally surface a withdrawn response.
    /// </summary>
    private IQueryable<Summary> Detailed()
    {
        return db.Summaries
            .Include(s => s.Topics)
                .ThenInclude(t => t.Points)
                    .ThenInclude(p => p.References.Where(r => !r.Response.IsDeleted))
                        .ThenInclude(r => r.Response)
            .AsSplitQuery();
    }
}
