using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Data.Seed;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class TopicLifecycleTests : DatabaseTest
{
    [TestMethod]
    public async Task Closing_freezes_every_response_in_the_topic()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _) = await this.OpenTopicAsync();

        Assert.IsFalse(await mDb.Responses.AnyAsync(r => r.IsFrozen, this.Cancellation));

        await this.Admin().SetAcceptingResponsesAsync(topic, false, this.Cancellation);

        Assert.IsTrue(await mDb.Responses.AllAsync(r => r.IsFrozen, this.Cancellation));
    }

    /// <summary>
    /// The rot guarantee rides on the response rather than on a topic that can never reopen,
    /// so reopening threatens nothing: what was collected stays frozen for good.
    /// </summary>
    [TestMethod]
    public async Task Reopening_leaves_what_was_already_frozen_frozen()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, token) = await this.OpenTopicAsync();
        var admin = this.Admin();

        await admin.SetAcceptingResponsesAsync(topic, false, this.Cancellation);
        await admin.SetAcceptingResponsesAsync(topic, true, this.Cancellation);

        var first = await mDb.Responses.SingleAsync(this.Cancellation);

        Assert.IsTrue(first.IsFrozen);

        await new ResponseService(mDb).SubmitAsync(topic, "Arrived later", "Sam", this.Cancellation);

        var later = await mDb.Responses.SingleAsync(r => r.Body == "Arrived later", this.Cancellation);

        Assert.IsFalse(later.IsFrozen);

        // The frozen one is no longer the author's to change; the new one still is.
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => new ResponseService(mDb).EditAsync(topic, first, "Rewritten", "Anna", this.Cancellation));

        await new ResponseService(mDb).EditAsync(topic, later, "Reworded", "Sam", this.Cancellation);
    }

    [TestMethod]
    public async Task Reopening_is_allowed_even_once_a_summary_exists()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _) = await this.OpenTopicAsync();

        topic.Summaries.Add(new Summary { Body = "overview", CreatedBy = "agent" });
        await mDb.SaveChangesAsync(this.Cancellation);

        await this.Admin().SetAcceptingResponsesAsync(topic, false, this.Cancellation);
        await this.Admin().SetAcceptingResponsesAsync(topic, true, this.Cancellation);

        Assert.IsTrue(topic.IsAcceptingResponses);
    }

    [TestMethod]
    public async Task Identity_is_changeable_until_somebody_has_answered_under_it()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = NewTopic(ResponseIdentity.Required);

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        await this.Admin().UpdateSettingsAsync(topic, false, false, true, this.Cancellation);

        Assert.AreEqual(ResponseIdentity.Anonymous, topic.ResponseIdentity);

        await new ResponseService(mDb).SubmitAsync(topic, "Said in confidence", null, this.Cancellation);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Admin().UpdateSettingsAsync(topic, false, false, false, this.Cancellation));

        Assert.AreEqual(ResponseIdentity.Anonymous, topic.ResponseIdentity);
    }

    [TestMethod]
    public void A_summary_is_hidden_from_the_public_until_blessed_and_published()
    {
        using var activity = TestTelemetry.Source.Start();

        var draft = new Summary { Body = "overview", IsDraft = true, IsPublic = true };
        var privateFinal = new Summary { Body = "overview", IsDraft = false, IsPublic = false };
        var published = new Summary { Body = "overview", IsDraft = false, IsPublic = true };

        Assert.IsFalse(draft.IsVisibleToPublic);
        Assert.IsFalse(privateFinal.IsVisibleToPublic);
        Assert.IsTrue(published.IsVisibleToPublic);
    }

    /// <summary>
    /// The seeder closes topics by setting the flag rather than going through the service,
    /// so it has to freeze them itself. Without this the seeded retro cites text an agent
    /// would then be refused permission to cite.
    /// </summary>
    [TestMethod]
    public async Task Every_seeded_closed_topic_arrives_frozen()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, this.Cancellation);

        var thawed = await mDb.Responses
            .Where(r => !r.Topic.IsAcceptingResponses && !r.IsFrozen)
            .CountAsync(this.Cancellation);

        Assert.AreEqual(0, thawed);
    }

    private TopicAdminService Admin()
    {
        return new TopicAdminService(mDb);
    }

    private async Task<(Topic Topic, string Token)> OpenTopicAsync()
    {
        var topic = NewTopic(ResponseIdentity.Required);
        var token = Secrets.NewToken();

        topic.Responses.Add(new Response()
        {
            Id = Guid.NewGuid(),
            Body = "CI is slow",
            Author = "Anna",
            AuthTokenHash = Secrets.HashToken(token),
        });

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        return (topic, token);
    }
}
