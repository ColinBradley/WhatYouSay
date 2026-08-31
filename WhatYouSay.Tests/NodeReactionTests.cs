using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;

namespace WhatYouSay.Tests;

[TestClass]
public class NodeReactionTests : DatabaseTest
{
    [TestMethod]
    public async Task A_responder_cannot_react_twice_with_the_same_kind()
    {
        using var activity = TestTelemetry.Source.Start();

        var leaf = await this.SeededLeafAsync();

        mDb.NodeReactions.Add(new NodeReaction()
        {
            NodeId = leaf.Id,
            ResponderTokenHash = "same-person",
            Kind = ReactionKind.Agree,
        });

        await mDb.SaveChangesAsync(this.Cancellation);

        mDb.NodeReactions.Add(new NodeReaction()
        {
            NodeId = leaf.Id,
            ResponderTokenHash = "same-person",
            Kind = ReactionKind.Agree,
        });

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => mDb.SaveChangesAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task Agree_and_important_can_coexist_for_one_responder()
    {
        using var activity = TestTelemetry.Source.Start();

        var leaf = await this.SeededLeafAsync();

        mDb.NodeReactions.AddRange(
            new NodeReaction { NodeId = leaf.Id, ResponderTokenHash = "p", Kind = ReactionKind.Agree },
            new NodeReaction { NodeId = leaf.Id, ResponderTokenHash = "p", Kind = ReactionKind.Important });

        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.AreEqual(2, await mDb.NodeReactions.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task Different_responders_can_react_the_same_way()
    {
        using var activity = TestTelemetry.Source.Start();

        var leaf = await this.SeededLeafAsync();

        mDb.NodeReactions.AddRange(
            new NodeReaction { NodeId = leaf.Id, ResponderTokenHash = "one", Kind = ReactionKind.Agree },
            new NodeReaction { NodeId = leaf.Id, ResponderTokenHash = "two", Kind = ReactionKind.Agree });

        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.AreEqual(2, await mDb.NodeReactions.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task Deleting_a_node_takes_its_subtree_with_it()
    {
        using var activity = TestTelemetry.Source.Start();

        var leaf = await this.SeededLeafAsync();
        var heading = await mDb.SummaryNodes.SingleAsync(n => n.Id == leaf.ParentId, this.Cancellation);

        mDb.SummaryNodes.Remove(heading);
        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.AreEqual(0, await mDb.SummaryNodes.CountAsync(this.Cancellation));
    }
}
