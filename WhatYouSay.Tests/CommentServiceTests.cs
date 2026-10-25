using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Data.Seed;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class CommentServiceTests : DatabaseTest
{
    /// <summary>When the seeded response was submitted, on a topic that records timing.</summary>
    private static readonly DateTimeOffset sSubmitted = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task A_comment_from_a_responder_carries_their_own_words_beside_it()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, summary, node, token) = await this.SeededAsync();

        await this.Service().AddAsync(topic, node.Id, token, "That is not what I meant.", "Ash", this.Cancellation);

        var comment = (await this.Service().ListAsync(topic, summary.Id, null, false, this.Cancellation)).Single();

        Assert.AreEqual("That is not what I meant.", comment.Body);
        Assert.AreEqual("Ash", comment.Author);
        Assert.AreEqual("CI is slow and it costs me an hour a day.", comment.CommenterResponses.Single().Body);
    }

    [TestMethod]
    public async Task A_comment_from_someone_who_never_responded_has_no_words_beside_it()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, summary, node, _) = await this.SeededAsync();

        await this.Service().AddAsync(
            topic, node.Id, Secrets.NewToken(), "Passing through.", "Sam", this.Cancellation);

        var comment = (await this.Service().ListAsync(topic, summary.Id, null, false, this.Cancellation)).Single();

        Assert.IsEmpty(comment.CommenterResponses);
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
    public async Task Anonymous_untimed_topics_record_no_comment_names_or_times()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, node, token) = await this.SeededAsync(ResponseIdentity.Anonymous, ResponseTiming.NotRecorded);

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
    public async Task A_comment_lists_every_live_response_its_author_gave_once()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, summary, node, token) = await this.SeededAsync();
        var hash = Secrets.HashToken(token);
        var first = topic.Responses.Single();

        var later = Revised(NewResponse(topic, hash), "Reviews are slow too.", sSubmitted.AddHours(2));
        var withdrawn = Revised(NewResponse(topic, hash), "Never mind.", sSubmitted.AddHours(1));

        withdrawn.IsDeleted = true;
        mDb.Responses.AddRange(later, withdrawn);
        await mDb.SaveChangesAsync(this.Cancellation);

        var service = this.Service();

        await service.AddAsync(topic, node.Id, token, "Not quite.", "Ash", this.Cancellation);
        await service.AddAsync(topic, node.Id, token, "Also this.", "Ash", this.Cancellation);

        var comments = await service.ListAsync(topic, summary.Id, null, true, this.Cancellation);

        // Two comments, each listing both live responses, rather than a row per response.
        Assert.HasCount(2, comments);

        foreach (var comment in comments)
        {
            Assert.AreSequenceEqual(
                new[] { later.Id, first.Id },
                comment.CommenterResponses.Select(r => r.ResponseId).ToArray());

            Assert.AreEqual("Reviews are slow too.", comment.CommenterResponses[0].Body);
        }
    }

    [TestMethod]
    public async Task A_commenters_responses_on_another_topic_are_not_listed()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, summary, node, token) = await this.SeededAsync();
        var (elsewhere, _, _, _) = await this.SeededAsync();

        mDb.Responses.Add(Revised(NewResponse(elsewhere, Secrets.HashToken(token)), "Said elsewhere."));
        await mDb.SaveChangesAsync(this.Cancellation);

        await this.Service().AddAsync(topic, node.Id, token, "Not quite.", null, this.Cancellation);

        var comment = (await this.Service().ListAsync(topic, summary.Id, null, false, this.Cancellation)).Single();

        Assert.HasCount(1, comment.CommenterResponses);
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
        Assert.IsTrue(comments.All(c => c.CommenterResponses.Count > 0));
    }

    private CommentService Service()
    {
        return new CommentService(mDb);
    }

    private static Response NewResponse(Topic topic, string tokenHash)
    {
        return new Response()
        {
            Id = Guid.NewGuid(),
            TopicId = topic.Id,
            Author = "Ash",
            AuthTokenHash = tokenHash,
        };
    }

    private async Task<(Topic Topic, Summary Summary, SummaryNode Node, string Token)> SeededAsync(
        ResponseIdentity identity = ResponseIdentity.Required,
        ResponseTiming timing = ResponseTiming.Recorded
    )
    {
        var topic = NewTopic(identity, timing);
        topic.IsAcceptingResponses = false;

        var token = Secrets.NewToken();

        topic.Responses.Add(Revised(new Response()
        {
            Id = Guid.NewGuid(),
            Author = identity == ResponseIdentity.Anonymous ? null : "Ash",
            AuthTokenHash = Secrets.HashToken(token),
            IsFrozen = true,
        }, "CI is slow and it costs me an hour a day.", topic.RecordsTiming ? sSubmitted : null));

        var summary = new Summary() { Body = "Overview", IsDraft = false, IsPublic = true };
        var node = new SummaryNode() { Text = "CI is slow" };

        summary.Nodes.Add(node);
        topic.Summaries.Add(summary);

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        return (topic, summary, node, token);
    }
}
