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
    public async Task Closing_freezes_nothing()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _) = await this.OpenTopicAsync();

        await this.Admin().SetAcceptingResponsesAsync(topic, false, this.Cancellation);

        Assert.IsFalse(topic.IsAcceptingResponses);
        Assert.IsFalse(await mDb.Responses.AnyAsync(r => r.IsFrozen, this.Cancellation));
    }

    [TestMethod]
    public async Task A_closed_topic_still_lets_people_edit_and_withdraw_what_they_wrote()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, token) = await this.OpenTopicAsync();
        var responses = new ResponseService(mDb);

        await this.Admin().SetAcceptingResponsesAsync(topic, false, this.Cancellation);

        var own = (await responses.ListOwnAsync(topic, token, this.Cancellation)).Single();

        await responses.EditAsync(topic, own, "Reworded after closing", "Anna", this.Cancellation);

        Assert.AreEqual("Reworded after closing", own.Current.Body);

        await responses.WithdrawAsync(topic, own, this.Cancellation);

        Assert.IsTrue(own.IsDeleted);
    }

    /// <summary>Freezing holds until the admin lifts it, whether or not the topic is open.</summary>
    [TestMethod]
    public async Task A_freeze_holds_until_the_admin_allows_editing_again()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, token) = await this.OpenTopicAsync();
        var admin = this.Admin();
        var responses = new ResponseService(mDb);

        await admin.SetResponsesFrozenAsync(topic, true, this.Cancellation);

        var own = (await responses.ListOwnAsync(topic, token, this.Cancellation)).Single();

        Assert.IsTrue(own.IsFrozen);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => responses.EditAsync(topic, own, "Rewritten", "Anna", this.Cancellation));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => responses.WithdrawAsync(topic, own, this.Cancellation));

        // Closing and reopening is a separate question and lifts nothing.
        await admin.SetAcceptingResponsesAsync(topic, false, this.Cancellation);
        await admin.SetAcceptingResponsesAsync(topic, true, this.Cancellation);

        Assert.IsTrue(own.IsFrozen);

        await admin.SetResponsesFrozenAsync(topic, false, this.Cancellation);

        await responses.EditAsync(topic, own, "Rewritten", "Anna", this.Cancellation);

        Assert.AreEqual("Rewritten", own.Current.Body);
    }

    [TestMethod]
    public async Task A_response_arriving_after_a_freeze_starts_editable()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _) = await this.OpenTopicAsync();
        var responses = new ResponseService(mDb);

        await this.Admin().SetResponsesFrozenAsync(topic, true, this.Cancellation);

        var later = await responses.SubmitAsync(topic, Secrets.NewToken(), "Arrived later", "Sam", this.Cancellation);

        Assert.IsFalse(later.IsFrozen);

        await responses.EditAsync(topic, later, "Reworded", "Sam", this.Cancellation);
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

        await this.Admin().UpdateSettingsAsync(topic, false, false, true, true, this.Cancellation);

        Assert.AreEqual(ResponseIdentity.Anonymous, topic.ResponseIdentity);

        await new ResponseService(mDb).SubmitAsync(topic, Secrets.NewToken(), "Said in confidence", null, this.Cancellation);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Admin().UpdateSettingsAsync(topic, false, false, false, true, this.Cancellation));

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
    /// The seeder freezes the responses of the topics it closes, so the admin's freeze
    /// controls have something to undo.
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

        topic.Responses.Add(Revised(new Response()
        {
            Id = Guid.NewGuid(),
            Author = "Anna",
            AuthTokenHash = Secrets.HashToken(token),
        }, "CI is slow"));

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        return (topic, token);
    }
}
