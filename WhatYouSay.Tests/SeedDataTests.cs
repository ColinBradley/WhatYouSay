using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WhatYouSay.Data;

namespace WhatYouSay.Tests;

public class SeedDataTests : DatabaseTest
{
    [Fact]
    public async Task Anonymous_surveys_record_no_identifying_metadata()
    {
        await SeedData.EnsureSeededAsync(mDb, NullLogger.Instance, Cancellation);

        var anonymous = await mDb.Surveys
            .Include(s => s.Responses)
            .SingleAsync(s => s.ResponseIdentity == ResponseIdentity.Anonymous, Cancellation);

        // The point of anonymous mode is that the data does not exist, not that it is
        // merely hidden at render time. Nothing downstream can leak what was never stored.
        Assert.NotEmpty(anonymous.Responses);
        Assert.All(anonymous.Responses, r => Assert.Null(r.CreatedAt));
        Assert.All(anonymous.Responses, r => Assert.Null(r.UpdatedAt));
        Assert.All(anonymous.Responses, r => Assert.Null(r.Author));
    }

    [Fact]
    public async Task Named_surveys_do_record_timestamps_and_authors()
    {
        await SeedData.EnsureSeededAsync(mDb, NullLogger.Instance, Cancellation);

        var retro = await mDb.Surveys
            .Include(s => s.Responses)
            .SingleAsync(s => s.Code == "spr47ab", Cancellation);

        Assert.All(retro.Responses, r => Assert.NotNull(r.CreatedAt));
        Assert.All(retro.Responses, r => Assert.False(string.IsNullOrWhiteSpace(r.Author)));
    }

    [Fact]
    public async Task Seed_covers_four_deliberately_different_shapes()
    {
        await SeedData.EnsureSeededAsync(mDb, NullLogger.Instance, Cancellation);

        var surveys = await mDb.Surveys.Include(s => s.Responses).ToListAsync(Cancellation);

        Assert.Equal(4, surveys.Count);

        // Small, medium and large, so summarisation gets stressed at more than one scale.
        Assert.Contains(surveys, s => s.Responses.Count <= 5);
        Assert.Contains(surveys, s => s.Responses.Count is > 10 and < 20);
        Assert.Contains(surveys, s => s.Responses.Count > 50);

        // Summarising requires a closed survey, so at least one must be ready to go.
        Assert.Contains(surveys, s => !s.IsAcceptingResponses);
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate()
    {
        await SeedData.EnsureSeededAsync(mDb, NullLogger.Instance, Cancellation);
        await SeedData.EnsureSeededAsync(mDb, NullLogger.Instance, Cancellation);

        Assert.Equal(4, await mDb.Surveys.CountAsync(Cancellation));
    }
}
