using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;
using WhatYouSay.Data.Seed;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

/// <summary>
/// The admin's view of "this misrepresents me". The join back to the objector's own response
/// is the point of the flag, so it is what these assert.
/// </summary>
[TestClass]
public class ObjectionTests : DatabaseTest
{
    [TestMethod]
    public async Task An_objection_arrives_with_the_words_it_says_were_misread()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, summary, response, node) = await this.SeededAsync();

        mDb.NodeReactions.Add(new NodeReaction()
        {
            NodeId = node.Id,
            ResponderTokenHash = response.AuthTokenHash,
            Kind = ReactionKind.Misrepresents,
            Note = "That is not what I meant.",
        });

        await mDb.SaveChangesAsync(this.Cancellation);

        var objection = (await new ReactionService(mDb)
            .ListObjectionsAsync(topic, summary.Id, this.Cancellation))
            .Single();

        Assert.AreEqual(node.Text, objection.NodeText);
        Assert.AreEqual("That is not what I meant.", objection.Note);
        Assert.AreEqual(response.Body, objection.ResponseBody);
        Assert.AreEqual(response.Author, objection.Author);
    }

    [TestMethod]
    public async Task Agree_and_important_are_not_objections()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, summary, response, node) = await this.SeededAsync();

        mDb.NodeReactions.Add(new NodeReaction()
        {
            NodeId = node.Id,
            ResponderTokenHash = response.AuthTokenHash,
            Kind = ReactionKind.Agree,
        });

        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.IsEmpty(await new ReactionService(mDb)
            .ListObjectionsAsync(topic, summary.Id, this.Cancellation));
    }

    [TestMethod]
    public async Task An_objection_from_a_withdrawn_response_is_not_shown()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, summary, response, node) = await this.SeededAsync();

        mDb.NodeReactions.Add(new NodeReaction()
        {
            NodeId = node.Id,
            ResponderTokenHash = response.AuthTokenHash,
            Kind = ReactionKind.Misrepresents,
            Note = "Withdrawn since.",
        });

        response.IsDeleted = true;
        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.IsEmpty(await new ReactionService(mDb)
            .ListObjectionsAsync(topic, summary.Id, this.Cancellation));
    }

    [TestMethod]
    public async Task The_seeded_retro_has_objections_to_look_at()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, this.Cancellation);

        var retro = await mDb.Topics
            .Include(s => s.Summaries)
            .SingleAsync(s => s.Code == "spr47ab", this.Cancellation);

        var objections = await new ReactionService(mDb).ListObjectionsAsync(
            retro, retro.Summaries.Single().Id, this.Cancellation);

        // Without these the admin editor's objection panel never renders in development.
        Assert.IsNotEmpty(objections);
        Assert.IsTrue(objections.Any(o => o.Note is not null));
        Assert.IsTrue(objections.All(o => o.ResponseBody.Length > 0));
    }

    private async Task<(Topic Topic, Summary Summary, Response Response, SummaryNode Node)> SeededAsync()
    {
        var topic = NewTopic(ResponseIdentity.Required);
        topic.IsAcceptingResponses = false;

        var response = new Response()
        {
            Id = Guid.CreateVersion7(),
            Body = "CI is slow and it costs me an hour a day.",
            Author = "Ash",
            AuthTokenHash = Guid.NewGuid().ToString("n"),
        };

        var summary = new Summary() { Body = "Overview" };
        var node = new SummaryNode() { Text = "CI is slow" };

        summary.Nodes.Add(node);
        topic.Responses.Add(response);
        topic.Summaries.Add(summary);

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        return (topic, summary, response, node);
    }
}
