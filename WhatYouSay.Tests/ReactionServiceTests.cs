using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class ReactionServiceTests : DatabaseTest
{
    [TestMethod]
    public async Task Reacting_twice_takes_the_reaction_back()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, node, token) = await this.SummarisedTopicAsync();
        var service = this.Service();

        await service.ToggleAsync(topic, node, token, ReactionKind.Agree, this.Cancellation);
        Assert.AreEqual(1, await mDb.NodeReactions.CountAsync(this.Cancellation));

        await service.ToggleAsync(topic, node, token, ReactionKind.Agree, this.Cancellation);
        Assert.AreEqual(0, await mDb.NodeReactions.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task Someone_who_did_not_respond_cannot_react()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, node, _) = await this.SummarisedTopicAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Service().ToggleAsync(
                topic, node, Secrets.NewToken(), ReactionKind.Agree, this.Cancellation));

        Assert.AreEqual(0, await mDb.NodeReactions.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task A_withdrawn_responder_loses_the_right_to_react()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, node, token) = await this.SummarisedTopicAsync();

        var response = await mDb.Responses.SingleAsync(this.Cancellation);
        response.IsDeleted = true;
        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.IsFalse(await this.Service().CanReactAsync(topic.Id, token, this.Cancellation));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Service().ToggleAsync(topic, node, token, ReactionKind.Agree, this.Cancellation));
    }

    [TestMethod]
    public async Task A_node_on_another_topic_cannot_be_reacted_to()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, token) = await this.SummarisedTopicAsync();
        var (_, otherNode, _) = await this.SummarisedTopicAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Service().ToggleAsync(topic, otherNode, token, ReactionKind.Agree, this.Cancellation));
    }

    [TestMethod]
    public async Task An_objection_carries_a_note_and_can_be_reworded()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, node, token) = await this.SummarisedTopicAsync();
        var service = this.Service();

        await service.SetObjectionAsync(topic, node, token, "That is not what I meant", this.Cancellation);
        await service.SetObjectionAsync(topic, node, token, "Closer, but still wrong", this.Cancellation);

        var stored = await mDb.NodeReactions.SingleAsync(this.Cancellation);

        // Rewording edits the objection rather than withdrawing and re-raising it.
        Assert.AreEqual(ReactionKind.Misrepresents, stored.Kind);
        Assert.AreEqual("Closer, but still wrong", stored.Note);
    }

    [TestMethod]
    public async Task An_objection_is_withdrawn_explicitly_rather_than_by_resubmitting()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, node, token) = await this.SummarisedTopicAsync();
        var service = this.Service();

        await service.SetObjectionAsync(topic, node, token, "Wrong", this.Cancellation);
        await service.WithdrawAsync(topic, node, token, ReactionKind.Misrepresents, this.Cancellation);

        Assert.AreEqual(0, await mDb.NodeReactions.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task Anonymous_topics_record_no_reaction_timestamps()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, node, token) = await this.SummarisedTopicAsync(ResponseIdentity.Anonymous);

        await this.Service().ToggleAsync(topic, node, token, ReactionKind.Agree, this.Cancellation);

        var stored = await mDb.NodeReactions.SingleAsync(this.Cancellation);

        Assert.IsNull(stored.CreatedAt);
    }

    [TestMethod]
    public async Task The_tally_separates_the_group_from_the_viewer()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, node, mine) = await this.SummarisedTopicAsync();
        var theirs = await this.AddResponderAsync(topic);
        var service = this.Service();

        await service.ToggleAsync(topic, node, mine, ReactionKind.Agree, this.Cancellation);
        await service.ToggleAsync(topic, node, theirs, ReactionKind.Agree, this.Cancellation);
        await service.ToggleAsync(topic, node, theirs, ReactionKind.Important, this.Cancellation);

        var summaryId = await mDb.Summaries.Select(s => s.Id).SingleAsync(this.Cancellation);
        var tally = (await service.TallyAsync(summaryId, mine, this.Cancellation))[node];

        Assert.AreEqual(2, tally.Agree);
        Assert.AreEqual(1, tally.Important);
        Assert.Contains(ReactionKind.Agree, tally.Mine);
        Assert.DoesNotContain(ReactionKind.Important, tally.Mine);
    }

    [TestMethod]
    public async Task A_viewer_who_did_not_respond_sees_counts_but_nothing_of_their_own()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, node, token) = await this.SummarisedTopicAsync();
        var service = this.Service();

        await service.ToggleAsync(topic, node, token, ReactionKind.Agree, this.Cancellation);

        var summaryId = await mDb.Summaries.Select(s => s.Id).SingleAsync(this.Cancellation);
        var tally = (await service.TallyAsync(summaryId, null, this.Cancellation))[node];

        Assert.AreEqual(1, tally.Agree);
        Assert.IsEmpty(tally.Mine);
        Assert.IsNull(tally.MyNote);
    }

    private ReactionService Service()
    {
        return new ReactionService(mDb);
    }

    private async Task<string> AddResponderAsync(Topic topic)
    {
        var token = Secrets.NewToken();

        mDb.Responses.Add(new Response()
        {
            Id = Guid.NewGuid(),
            TopicId = topic.Id,
            Body = "Another answer",
            AuthTokenHash = Secrets.HashToken(token),
        });

        await mDb.SaveChangesAsync(this.Cancellation);

        return token;
    }

    private async Task<(Topic Topic, int NodeId, string Token)> SummarisedTopicAsync(
        ResponseIdentity identity = ResponseIdentity.Required
    )
    {
        var topic = NewTopic(identity);
        topic.IsAcceptingResponses = false;

        var token = Secrets.NewToken();

        topic.Responses.Add(new Response()
        {
            Id = Guid.NewGuid(),
            Body = "CI is slow",
            Author = identity == ResponseIdentity.Anonymous ? null : "Anna",
            AuthTokenHash = Secrets.HashToken(token),
        });

        var heading = new SummaryNode { Text = "Tooling" };
        var leaf = new SummaryNode { Text = "CI is slow", Parent = heading };
        var summary = new Summary { Id = Guid.CreateVersion7(), Body = "Overview", IsDraft = false, IsPublic = true };

        summary.Nodes.Add(heading);
        summary.Nodes.Add(leaf);
        topic.Summaries.Add(summary);

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        return (topic, leaf.Id, token);
    }
}
