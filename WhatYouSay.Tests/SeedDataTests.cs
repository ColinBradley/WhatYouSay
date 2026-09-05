using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;
using WhatYouSay.Data.Seed;

namespace WhatYouSay.Tests;

[TestClass]
public class SeedDataTests : DatabaseTest
{
    [TestMethod]
    public async Task Anonymous_topics_record_no_identifying_metadata()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, this.Cancellation);

        var anonymous = await mDb.Topics
            .Include(s => s.Responses)
            .SingleAsync(s => s.ResponseIdentity == ResponseIdentity.Anonymous, this.Cancellation);

        // Anonymous means the data does not exist, not that it is hidden at render time.
        Assert.IsNotEmpty(anonymous.Responses);
        Assert.IsTrue(anonymous.Responses.All(r => r.CreatedAt is null));
        Assert.IsTrue(anonymous.Responses.All(r => r.UpdatedAt is null));
        Assert.IsTrue(anonymous.Responses.All(r => r.Author is null));
    }

    [TestMethod]
    public async Task Named_topics_do_record_timestamps_and_authors()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, this.Cancellation);

        var retro = await mDb.Topics
            .Include(s => s.Responses)
            .SingleAsync(s => s.Code == "spr47ab", this.Cancellation);

        Assert.IsTrue(retro.Responses.All(r => r.CreatedAt is not null));
        Assert.IsTrue(retro.Responses.All(r => !string.IsNullOrWhiteSpace(r.Author)));
    }

    [TestMethod]
    public async Task Seed_covers_four_deliberately_different_shapes()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, this.Cancellation);

        var topics = await mDb.Topics.Include(s => s.Responses).ToListAsync(this.Cancellation);

        Assert.HasCount(4, topics);

        Assert.Contains(s => s.Responses.Count <= 5, topics);
        Assert.Contains(s => s.Responses.Count is > 10 and < 20, topics);
        Assert.Contains(s => s.Responses.Count > 50, topics);

        Assert.Contains(s => !s.IsAcceptingResponses, topics);
    }

    [TestMethod]
    public async Task Seeding_twice_does_not_duplicate()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, this.Cancellation);
        await SeedData.EnsureSeededAsync(mDb, this.Cancellation);

        Assert.AreEqual(4, await mDb.Topics.CountAsync(this.Cancellation));
    }
}
