using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class ResponseReactionTests : DatabaseTest
{
    [TestMethod]
    public async Task Reacting_twice_takes_the_reaction_back()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.RespondedTopicAsync();
        var service = this.Service();

        // A reactor with no response of their own, as anyone reading the topic may be.
        var reactor = Secrets.NewToken();

        await service.ToggleOnResponseAsync(topic, response.Id, reactor, ReactionKind.Agree, this.Cancellation);
        Assert.AreEqual(1, await mDb.ResponseReactions.CountAsync(this.Cancellation));

        await service.ToggleOnResponseAsync(topic, response.Id, reactor, ReactionKind.Agree, this.Cancellation);
        Assert.AreEqual(0, await mDb.ResponseReactions.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task One_reactor_can_use_several_kinds()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.RespondedTopicAsync();
        var service = this.Service();
        var reactor = Secrets.NewToken();

        await service.ToggleOnResponseAsync(topic, response.Id, reactor, ReactionKind.Agree, this.Cancellation);
        await service.ToggleOnResponseAsync(topic, response.Id, reactor, ReactionKind.Question, this.Cancellation);

        var tally = (await service.TallyResponsesAsync(topic, reactor, this.Cancellation))[response.Id];

        Assert.AreEqual(1, tally.CountOf(ReactionKind.Agree));
        Assert.AreEqual(1, tally.CountOf(ReactionKind.Question));
        Assert.Contains(ReactionKind.Agree, tally.Mine);
        Assert.Contains(ReactionKind.Question, tally.Mine);
    }

    [TestMethod]
    public async Task Separate_cookies_are_counted_separately()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.RespondedTopicAsync();
        var service = this.Service();
        var mine = Secrets.NewToken();
        var theirs = Secrets.NewToken();

        await service.ToggleOnResponseAsync(topic, response.Id, mine, ReactionKind.Agree, this.Cancellation);
        await service.ToggleOnResponseAsync(topic, response.Id, theirs, ReactionKind.Agree, this.Cancellation);
        await service.ToggleOnResponseAsync(topic, response.Id, theirs, ReactionKind.Celebrate, this.Cancellation);

        var tally = (await service.TallyResponsesAsync(topic, mine, this.Cancellation))[response.Id];

        Assert.AreEqual(2, tally.CountOf(ReactionKind.Agree));
        Assert.AreEqual(1, tally.CountOf(ReactionKind.Celebrate));
        Assert.Contains(ReactionKind.Agree, tally.Mine);
        Assert.DoesNotContain(ReactionKind.Celebrate, tally.Mine);

        var anonymous = (await service.TallyResponsesAsync(topic, null, this.Cancellation))[response.Id];

        Assert.AreEqual(2, anonymous.CountOf(ReactionKind.Agree));
        Assert.IsEmpty(anonymous.Mine);
    }

    [TestMethod]
    public async Task A_response_on_another_topic_cannot_be_reacted_to()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _) = await this.RespondedTopicAsync();
        var (otherTopic, other) = await this.RespondedTopicAsync();
        var service = this.Service();
        var reactor = Secrets.NewToken();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.ToggleOnResponseAsync(topic, other.Id, reactor, ReactionKind.Agree, this.Cancellation));

        Assert.AreEqual(0, await mDb.ResponseReactions.CountAsync(this.Cancellation));

        // A reaction made properly on the other topic stays out of this topic's tally.
        await service.ToggleOnResponseAsync(otherTopic, other.Id, reactor, ReactionKind.Agree, this.Cancellation);

        Assert.IsEmpty(await service.TallyResponsesAsync(topic, reactor, this.Cancellation));
    }

    [TestMethod]
    [DataRow(true, DisplayName = "Withdrawn by the responder")]
    [DataRow(false, DisplayName = "Deleted by the admin")]
    public async Task A_withdrawn_response_takes_no_new_reactions(bool byResponder)
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.RespondedTopicAsync();

        await this.RemoveAsync(topic, response, byResponder);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Service().ToggleOnResponseAsync(
                topic, response.Id, Secrets.NewToken(), ReactionKind.Agree, this.Cancellation));

        Assert.AreEqual(0, await mDb.ResponseReactions.CountAsync(this.Cancellation));
    }

    [TestMethod]
    [DataRow(true, DisplayName = "Withdrawn by the responder")]
    [DataRow(false, DisplayName = "Deleted by the admin")]
    public async Task A_withdrawn_response_shows_no_tally(bool byResponder)
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.RespondedTopicAsync();
        var reactor = Secrets.NewToken();

        await this.Service().ToggleOnResponseAsync(topic, response.Id, reactor, ReactionKind.Agree, this.Cancellation);
        await this.RemoveAsync(topic, response, byResponder);

        await using var fresh = this.NewContext();

        var tallies = await new ReactionService(fresh).TallyResponsesAsync(topic, reactor, this.Cancellation);

        Assert.IsFalse(tallies.ContainsKey(response.Id));
    }

    [TestMethod]
    public async Task Reactions_survive_an_edit()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.RespondedTopicAsync();
        var reactor = Secrets.NewToken();

        await this.Service().ToggleOnResponseAsync(topic, response.Id, reactor, ReactionKind.Important, this.Cancellation);
        await new ResponseService(mDb).EditAsync(topic, response, "CI is slow, and flaky too.", "Anna", this.Cancellation);

        await using var fresh = this.NewContext();

        var tally = (await new ReactionService(fresh).TallyResponsesAsync(topic, reactor, this.Cancellation))[response.Id];
        var stored = await fresh.Responses.SingleAsync(r => r.Id == response.Id, this.Cancellation);

        Assert.AreEqual(2, stored.RevisionNumber);
        Assert.AreEqual(1, tally.CountOf(ReactionKind.Important));
        Assert.Contains(ReactionKind.Important, tally.Mine);
    }

    [TestMethod]
    public async Task A_duplicate_click_that_saves_second_is_counted_once()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.RespondedTopicAsync();
        var reactor = Secrets.NewToken();
        var first = this.Service();

        // The first click saves between the second click's read and its save.
        var race = new BeforeFirstSave(
            () => first.ToggleOnResponseAsync(topic, response.Id, reactor, ReactionKind.Agree, this.Cancellation));

        await using var racing = this.NewContext(race);

        await new ReactionService(racing).ToggleOnResponseAsync(
            topic, response.Id, reactor, ReactionKind.Agree, this.Cancellation);

        Assert.IsTrue(race.HasRun);

        Assert.AreEqual(1, await mDb.ResponseReactions.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task A_duplicate_take_back_that_saves_second_removes_once()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.RespondedTopicAsync();
        var reactor = Secrets.NewToken();
        var first = this.Service();

        await first.ToggleOnResponseAsync(topic, response.Id, reactor, ReactionKind.Agree, this.Cancellation);

        // The first take-back saves between the second one's read and its save.
        var race = new BeforeFirstSave(
            () => first.ToggleOnResponseAsync(topic, response.Id, reactor, ReactionKind.Agree, this.Cancellation));

        await using var racing = this.NewContext(race);

        await new ReactionService(racing).ToggleOnResponseAsync(
            topic, response.Id, reactor, ReactionKind.Agree, this.Cancellation);

        Assert.IsTrue(race.HasRun);

        Assert.AreEqual(0, await mDb.ResponseReactions.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task The_database_refuses_a_duplicate_reaction()
    {
        using var activity = TestTelemetry.Source.Start();

        var (_, response) = await this.RespondedTopicAsync();

        mDb.ResponseReactions.Add(new ResponseReaction()
        {
            ResponseId = response.Id,
            ReactorTokenHash = "same-person",
            Kind = ReactionKind.Agree,
        });

        await mDb.SaveChangesAsync(this.Cancellation);

        mDb.ResponseReactions.Add(new ResponseReaction()
        {
            ResponseId = response.Id,
            ReactorTokenHash = "same-person",
            Kind = ReactionKind.Agree,
        });

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => mDb.SaveChangesAsync(this.Cancellation));
    }

    [TestMethod]
    [DataRow(ResponseTiming.Recorded)]
    [DataRow(ResponseTiming.NotRecorded)]
    public async Task Reaction_times_follow_the_topic_timing_setting(ResponseTiming timing)
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response) = await this.RespondedTopicAsync(timing);

        await this.Service().ToggleOnResponseAsync(
            topic, response.Id, Secrets.NewToken(), ReactionKind.Agree, this.Cancellation);

        var stored = await mDb.ResponseReactions.SingleAsync(this.Cancellation);

        Assert.AreEqual(timing == ResponseTiming.Recorded, stored.CreatedAt is not null);
    }

    private ReactionService Service()
    {
        return new ReactionService(mDb);
    }

    /// <summary>A topic with one response, submitted as the respond page would.</summary>
    private async Task<(Topic Topic, Response Response)> RespondedTopicAsync(
        ResponseTiming timing = ResponseTiming.Recorded
    )
    {
        var topic = NewTopic(ResponseIdentity.Required, timing);
        topic.AreResponsesPublic = true;

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        var response = await new ResponseService(mDb).SubmitAsync(
            topic, Secrets.NewToken(), "CI is slow", "Anna", this.Cancellation);

        return (topic, response);
    }

    private async Task RemoveAsync(Topic topic, Response response, bool byResponder)
    {
        if (byResponder)
        {
            await new ResponseService(mDb).WithdrawAsync(topic, response, this.Cancellation);
        }
        else
        {
            await new TopicAdminService(mDb).DeleteResponseAsync(topic, response.Id, this.Cancellation);
        }
    }

    /// <summary>
    /// Runs another operation just before the context's first save, as a request racing it
    /// would.
    /// </summary>
    private sealed class BeforeFirstSave : SaveChangesInterceptor
    {
        private readonly Func<Task> mRacer;

        public BeforeFirstSave(Func<Task> racer)
        {
            mRacer = racer;
        }

        public bool HasRun { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (!this.HasRun)
            {
                this.HasRun = true;

                await mRacer();
            }

            return result;
        }
    }
}
