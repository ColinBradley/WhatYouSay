using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

public record TopicListing
{
    public required Topic Topic { get; init; }

    public required int ResponseCount { get; init; }

    public required bool HasVisibleSummary { get; init; }
}

public class TopicService
{
    private readonly WhatYouSayContext mDb;

    public TopicService(WhatYouSayContext db)
    {
        mDb = db;
    }

    public async Task<Topic?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await mDb.Topics.FirstOrDefaultAsync(s => s.Code == code, cancellationToken);
    }

    public async Task<IReadOnlyList<TopicListing>> ListPubliclyListedAsync(
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await mDb.Topics
            .Where(s => s.IsPubliclyListed)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new TopicListing()
            {
                Topic = s,
                ResponseCount = s.Responses.Count(r => !r.IsDeleted),
                HasVisibleSummary = s.Summaries.Any(x => !x.IsDraft && x.IsPublic),
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountResponsesAsync(Guid topicId, CancellationToken cancellationToken = default)
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await mDb.Responses.CountAsync(
            r => r.TopicId == topicId && !r.IsDeleted,
            cancellationToken);
    }
}
