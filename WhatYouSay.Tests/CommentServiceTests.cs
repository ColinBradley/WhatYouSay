using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Data.Seed;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class CommentServiceTests : DatabaseTest
{
    [TestMethod]
    public async Task A_comment_from_a_responder_carries_their_own_words_beside_it()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, summary, node, token) = await this.SeededAsync();

        await this.Service().AddAsync(topic, node.Id, token, "That is not what I meant.", "Ash", this.Cancellation);

        var comment = (await this.Service().ListAsync(topic, summary.Id, null, false, this.Cancellation)).Single();

        Assert.AreEqual("That is not what I meant.", comment.Body);
        Assert.AreEqual("Ash", comment.Author);
        Assert.AreEqual("CI is slow and it costs me an hour a day.", comment.ResponseBody);
    }

    [TestMethod]
    public async Task A_comment_from_someone_who_never_responded_has_no_words_beside_it()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, summary, node, _) = await this.SeededAsync();

        await this.Service().AddAsync(
            topic, node.Id, Secrets.NewToken(), "Passing through.", "Sam", this.Cancellation);

        var comment = (await this.Service().ListAsync(topic, summary.Id, null, false, this.Cancellation)).Single();

        Assert.IsNull(comment.ResponseBody);
        Assert.AreEqual("Sam", comment.Author);
    }

    [TestMethod]
    public async Task An_empty_comment_is_refused()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, node, token) = await this.SeededAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Service().AddAsync(topic, node.Id, token, "   ", null, this.Cancellation));
    }

    [TestMethod]
    public async Task A_hidden_comment_is_gone_for_everyone_but_its_author_and_the_admin()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, summary, node, token) = await this.SeededAsync();
        var service = this.Service();

        var id = await service.AddAsync(topic, node.Id, token, "Never mind.", "Ash", this.Cancellation);

        await service.SetHiddenAsync(topic, id, token, true, asAdmin: false, this.Cancellation);

        Assert.IsEmpty(await service.ListAsync(topic, summary.Id, null, false, this.Cancellation));
        Assert.IsNotEmpty(await service.ListAsync(topic, summary.Id, token, false, this.Cancellation));
        Assert.IsNotEmpty(await service.ListAsync(topic, summary.Id, null, true, this.Cancellation));
    }

    [TestMethod]
    public async Task Somebody_else_cannot_hide_your_comment()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, node, token) = await this.SeededAsync();
        var service = this.Service();

        var id = await service.AddAsync(topic, node.Id, token, "Stands.", null, this.Cancellation);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.SetHiddenAsync(topic, id, Secrets.NewToken(), true, false, this.Cancellation));
    }

    [TestMethod]
    public async Task Anonymous_topics_record_no_comment_names_or_times()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, node, token) = await this.SeededAsync(ResponseIdentity.Anonymous);

        await this.Service().AddAsync(topic, node.Id, token, "Said in confidence.", "Ash", this.Cancellation);

        var stored = await mDb.NodeComments.SingleAsync(this.Cancellation);

        Assert.IsNull(stored.Author);
        Assert.IsNull(stored.CreatedAt);
    }

    [TestMethod]
    public async Task Deleting_a_node_takes_its_comments_with_it()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, node, token) = await this.SeededAsync();

        await this.Service().AddAsync(topic, node.Id, token, "Goes with it.", null, this.Cancellation);

        mDb.SummaryNodes.Remove(await mDb.SummaryNodes.SingleAsync(n => n.Id == node.Id, this.Cancellation));
        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.AreEqual(0, await mDb.NodeComments.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task The_seeded_retro_has_comments_to_look_at()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, this.Cancellation);

        var retro = await mDb.Topics
            .Include(t => t.Summaries)
            .SingleAsync(t => t.Code == "spr47ab", this.Cancellation);

        var comments = await this.Service().ListAsync(
            retro, retro.Summaries.Single().Id, null, false, this.Cancellation);

        // Without these the admin editor's comment panel never renders in development.
        Assert.IsNotEmpty(comments);
        Assert.IsTrue(comments.All(c => c.ResponseBody is { Length: > 0 }));
    }

    private CommentService Service()
    {
        return new CommentService(mDb);
    }

    private async Task<(Topic Topic, Summary Summary, SummaryNode Node, string Token)> SeededAsync(
        ResponseIdentity identity = ResponseIdentity.Required
    )
    {
        var topic = NewTopic(identity);
        topic.IsAcceptingResponses = false;

        var token = Secrets.NewToken();

        topic.Responses.Add(new Response()
        {
            Id = Guid.NewGuid(),
            Body = "CI is slow and it costs me an hour a day.",
            Author = identity == ResponseIdentity.Anonymous ? null : "Ash",
            AuthTokenHash = Secrets.HashToken(token),
            IsFrozen = true,
        });

        var summary = new Summary() { Body = "Overview", IsDraft = false, IsPublic = true };
        var node = new SummaryNode() { Text = "CI is slow" };

        summary.Nodes.Add(node);
        topic.Summaries.Add(summary);

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        return (topic, summary, node, token);
    }
}
