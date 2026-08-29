using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;

namespace WhatYouSay.Tests;

public class PointReactionTests : DatabaseTest
{
    [Fact]
    public async Task A_responder_cannot_react_twice_with_the_same_kind()
    {
        var point = await this.SeededPointAsync();

        mDb.PointReactions.Add(new PointReaction
        {
            PointId = point.Id,
            ResponderTokenHash = "same-person",
            Kind = ReactionKind.Agree
        });

        await mDb.SaveChangesAsync(Cancellation);

        mDb.PointReactions.Add(new PointReaction
        {
            PointId = point.Id,
            ResponderTokenHash = "same-person",
            Kind = ReactionKind.Agree
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => mDb.SaveChangesAsync(Cancellation));
    }

    [Fact]
    public async Task Agree_and_important_can_coexist_for_one_responder()
    {
        var point = await this.SeededPointAsync();

        mDb.PointReactions.AddRange(
            new PointReaction { PointId = point.Id, ResponderTokenHash = "p", Kind = ReactionKind.Agree },
            new PointReaction { PointId = point.Id, ResponderTokenHash = "p", Kind = ReactionKind.Important });

        await mDb.SaveChangesAsync(Cancellation);

        Assert.Equal(2, await mDb.PointReactions.CountAsync(Cancellation));
    }

    [Fact]
    public async Task Different_responders_can_react_the_same_way()
    {
        var point = await this.SeededPointAsync();

        mDb.PointReactions.AddRange(
            new PointReaction { PointId = point.Id, ResponderTokenHash = "one", Kind = ReactionKind.Agree },
            new PointReaction { PointId = point.Id, ResponderTokenHash = "two", Kind = ReactionKind.Agree });

        await mDb.SaveChangesAsync(Cancellation);

        Assert.Equal(2, await mDb.PointReactions.CountAsync(Cancellation));
    }
}
