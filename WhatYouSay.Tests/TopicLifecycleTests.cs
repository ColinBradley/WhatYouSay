using WhatYouSay.Data;

namespace WhatYouSay.Tests;

[TestClass]
public class TopicLifecycleTests : DatabaseTest
{
    [TestMethod]
    public async Task A_topic_cannot_reopen_once_a_summary_exists()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = NewTopic(ResponseIdentity.Required);
        topic.IsAcceptingResponses = false;

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.IsTrue(topic.CanReopen);

        topic.Summaries.Add(new Summary { Body = "overview", CreatedBy = "agent" });
        await mDb.SaveChangesAsync(this.Cancellation);

        // Responses may only change while nothing references them.
        Assert.IsFalse(topic.CanReopen);
    }

    [TestMethod]
    public void An_open_topic_is_not_reopenable_because_it_was_never_closed()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = NewTopic(ResponseIdentity.Required);

        Assert.IsTrue(topic.IsAcceptingResponses);
        Assert.IsFalse(topic.CanReopen);
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
}
