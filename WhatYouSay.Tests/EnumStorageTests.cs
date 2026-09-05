using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;

namespace WhatYouSay.Tests;

[TestClass]
public class EnumStorageTests : DatabaseTest
{
    [TestMethod]
    public async Task Response_identity_is_stored_as_a_string()
    {
        using var activity = TestTelemetry.Source.Start();

        mDb.Topics.Add(NewTopic(ResponseIdentity.Anonymous));
        await mDb.SaveChangesAsync(this.Cancellation);

        var stored = await this.ScalarAsync("SELECT ResponseIdentity FROM Topics");

        // A sqlite3 session should show "Anonymous", not "2", and reordering the enum
        // members must never silently reinterpret existing rows.
        Assert.AreEqual("Anonymous", stored);
    }

    [TestMethod]
    public async Task Reaction_kind_is_stored_as_a_string()
    {
        using var activity = TestTelemetry.Source.Start();

        var leaf = await this.SeededLeafAsync();

        mDb.NodeReactions.Add(new NodeReaction()
        {
            NodeId = leaf.Id,
            ResponderTokenHash = "hash",
            Kind = ReactionKind.Misrepresents,
        });

        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.AreEqual("Misrepresents", await this.ScalarAsync("SELECT Kind FROM NodeReactions"));
    }
}
