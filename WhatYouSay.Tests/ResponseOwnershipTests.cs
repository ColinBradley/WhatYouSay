using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

/// <summary>One responder token owning several responses on a topic.</summary>
[TestClass]
public class ResponseOwnershipTests : DatabaseTest
{
    [TestMethod]
    public async Task One_token_can_post_several_responses()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, token, posted) = await this.ThreePostedAsync();

        var own = await this.Service().ListOwnAsync(topic, token, this.Cancellation);

        Assert.AreSequenceEqual(
            posted.Select(r => r.Id).Order().ToArray(),
            own.Select(r => r.Id).Order().ToArray());

        Assert.IsTrue(posted.All(r => r.AuthTokenHash == Secrets.HashToken(token)));
    }

    [TestMethod]
    public async Task Editing_one_response_leaves_the_others_alone()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, token, posted) = await this.ThreePostedAsync();
        var service = this.Service();

        var target = await service.FindOwnAsync(topic.Id, posted[1].Id, token, this.Cancellation);

        await service.EditAsync(topic, target!, "Changed my mind.", "Anna", this.Cancellation);

        await using var fresh = this.NewContext();

        var own = await new ResponseService(fresh).ListOwnAsync(topic, token, this.Cancellation);

        Assert.HasCount(3, own);
        Assert.AreEqual("Changed my mind.", own.Single(r => r.Id == posted[1].Id).Current.Body);
        Assert.IsTrue(own.Where(r => r.Id != posted[1].Id).All(r => !r.IsEdited));
    }

    [TestMethod]
    public async Task Withdrawing_one_response_keeps_ownership_of_the_others()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, token, posted) = await this.ThreePostedAsync();
        var service = this.Service();

        await service.WithdrawAsync(topic, posted[0], this.Cancellation);

        var own = await service.ListOwnAsync(topic, token, this.Cancellation);

        Assert.HasCount(2, own);
        Assert.DoesNotContain(r => r.Id == posted[0].Id, own);
        Assert.IsNull(await service.FindOwnAsync(topic.Id, posted[0].Id, token, this.Cancellation));
        Assert.IsNotNull(await service.FindOwnAsync(topic.Id, posted[1].Id, token, this.Cancellation));
    }

    [TestMethod]
    public async Task Withdrawing_a_response_keeps_the_tokens_reactions_and_lets_it_post_again()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, token, posted) = await this.ThreePostedAsync();
        var summary = new Summary() { Body = "Overview", IsDraft = false, IsPublic = true };
        var node = new SummaryNode() { Text = "CI is slow" };

        summary.Nodes.Add(node);
        topic.Summaries.Add(summary);
        await mDb.SaveChangesAsync(this.Cancellation);

        var reactions = new ReactionService(mDb);
        var service = this.Service();

        await reactions.ToggleAsync(topic, node.Id, token, ReactionKind.Agree, this.Cancellation);
        await service.WithdrawAsync(topic, posted[0], this.Cancellation);
        await service.SubmitAsync(topic, token, "One more thing.", "Anna", this.Cancellation);

        var tally = (await reactions.TallyAsync(summary.Id, token, this.Cancellation))[node.Id];

        Assert.Contains(ReactionKind.Agree, tally.Mine);
        Assert.HasCount(3, await service.ListOwnAsync(topic, token, this.Cancellation));
    }

    [TestMethod]
    public async Task Another_token_cannot_reach_someone_elses_responses()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, posted) = await this.ThreePostedAsync();
        var service = this.Service();
        var stranger = Secrets.NewToken();

        Assert.IsEmpty(await service.ListOwnAsync(topic, stranger, this.Cancellation));

        foreach (var response in posted)
        {
            Assert.IsNull(await service.FindOwnAsync(topic.Id, response.Id, stranger, this.Cancellation));
        }
    }

    [TestMethod]
    public async Task A_token_owns_nothing_on_another_topic()
    {
        using var activity = TestTelemetry.Source.Start();

        var (_, token, posted) = await this.ThreePostedAsync();
        var other = await this.OpenTopicAsync();
        var service = this.Service();

        Assert.IsEmpty(await service.ListOwnAsync(other, token, this.Cancellation));
        Assert.IsNull(await service.FindOwnAsync(other.Id, posted[0].Id, token, this.Cancellation));
    }

    private ResponseService Service()
    {
        return new ResponseService(mDb);
    }

    private async Task<Topic> OpenTopicAsync()
    {
        var topic = NewTopic(ResponseIdentity.Optional);

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        return topic;
    }

    /// <summary>An open topic with three responses posted under one token, in posting order.</summary>
    private async Task<(Topic Topic, string Token, IReadOnlyList<Response> Posted)> ThreePostedAsync()
    {
        var topic = await this.OpenTopicAsync();
        var token = Secrets.NewToken();
        var service = this.Service();

        IReadOnlyList<Response> posted =
        [
            await service.SubmitAsync(topic, token, "CI is slow.", "Anna", this.Cancellation),
            await service.SubmitAsync(topic, token, "Reviews take a day.", "Anna", this.Cancellation),
            await service.SubmitAsync(topic, token, "Pairing worked.", "Anna", this.Cancellation),
        ];

        return (topic, token, posted);
    }
}
