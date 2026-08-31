using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;

namespace WhatYouSay.Tests;

[TestClass]
public class PointReactionTests : DatabaseTest
{
    [TestMethod]
    public async Task A_responder_cannot_react_twice_with_the_same_kind()
    {
        using var activity = TestTelemetry.Source.Start();

        var point = await this.SeededPointAsync();

        mDb.PointReactions.Add(new PointReaction()
        {
            PointId = point.Id,
            ResponderTokenHash = "same-person",
            Kind = ReactionKind.Agree,
        });

        await mDb.SaveChangesAsync(this.Cancellation);

        mDb.PointReactions.Add(new PointReaction()
        {
            PointId = point.Id,
            ResponderTokenHash = "same-person",
            Kind = ReactionKind.Agree,
        });

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => mDb.SaveChangesAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task Agree_and_important_can_coexist_for_one_responder()
    {
        using var activity = TestTelemetry.Source.Start();

        var point = await this.SeededPointAsync();

        mDb.PointReactions.AddRange(
            new PointReaction { PointId = point.Id, ResponderTokenHash = "p", Kind = ReactionKind.Agree },
            new PointReaction { PointId = point.Id, ResponderTokenHash = "p", Kind = ReactionKind.Important });

        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.AreEqual(2, await mDb.PointReactions.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task Different_responders_can_react_the_same_way()
    {
        using var activity = TestTelemetry.Source.Start();

        var point = await this.SeededPointAsync();

        mDb.PointReactions.AddRange(
            new PointReaction { PointId = point.Id, ResponderTokenHash = "one", Kind = ReactionKind.Agree },
            new PointReaction { PointId = point.Id, ResponderTokenHash = "two", Kind = ReactionKind.Agree });

        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.AreEqual(2, await mDb.PointReactions.CountAsync(this.Cancellation));
    }
}
