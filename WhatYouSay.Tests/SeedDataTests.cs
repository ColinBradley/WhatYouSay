using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WhatYouSay.Data;

namespace WhatYouSay.Tests;

[TestClass]
public class SeedDataTests : DatabaseTest
{
    [TestMethod]
    public async Task Anonymous_surveys_record_no_identifying_metadata()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, NullLogger.Instance, this.Cancellation);

        var anonymous = await mDb.Surveys
            .Include(s => s.Responses)
            .SingleAsync(s => s.ResponseIdentity == ResponseIdentity.Anonymous, this.Cancellation);

        // The point of anonymous mode is that the data does not exist, not that it is
        // merely hidden at render time. Nothing downstream can leak what was never stored.
        Assert.IsNotEmpty(anonymous.Responses);
        Assert.IsTrue(anonymous.Responses.All(r => r.CreatedAt is null));
        Assert.IsTrue(anonymous.Responses.All(r => r.UpdatedAt is null));
        Assert.IsTrue(anonymous.Responses.All(r => r.Author is null));
    }

    [TestMethod]
    public async Task Named_surveys_do_record_timestamps_and_authors()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, NullLogger.Instance, this.Cancellation);

        var retro = await mDb.Surveys
            .Include(s => s.Responses)
            .SingleAsync(s => s.Code == "spr47ab", this.Cancellation);

        Assert.IsTrue(retro.Responses.All(r => r.CreatedAt is not null));
        Assert.IsTrue(retro.Responses.All(r => !string.IsNullOrWhiteSpace(r.Author)));
    }

    [TestMethod]
    public async Task Seed_covers_four_deliberately_different_shapes()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, NullLogger.Instance, this.Cancellation);

        var surveys = await mDb.Surveys.Include(s => s.Responses).ToListAsync(this.Cancellation);

        Assert.HasCount(4, surveys);

        // Small, medium and large, so summarisation gets stressed at more than one scale.
        Assert.Contains(s => s.Responses.Count <= 5, surveys);
        Assert.Contains(s => s.Responses.Count is > 10 and < 20, surveys);
        Assert.Contains(s => s.Responses.Count > 50, surveys);

        // Summarising requires a closed survey, so at least one must be ready to go.
        Assert.Contains(s => !s.IsAcceptingResponses, surveys);
    }

    [TestMethod]
    public async Task Seeding_twice_does_not_duplicate()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, NullLogger.Instance, this.Cancellation);
        await SeedData.EnsureSeededAsync(mDb, NullLogger.Instance, this.Cancellation);

        Assert.AreEqual(4, await mDb.Surveys.CountAsync(this.Cancellation));
    }
}
