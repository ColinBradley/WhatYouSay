using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

/// <summary>
/// Every write to a summary's overview or tree moves its revision on. A write based on an
/// older revision is refused whole, so nobody's change is lost to a write that never saw it.
/// </summary>
[TestClass]
public class SummaryRevisionTests : DatabaseTest
{
    private const string AnnaSaid = "CI is slow. A full run is 22 minutes and it fails often.";

    [TestMethod]
    public async Task Every_human_edit_moves_the_revision_on()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, live) = await this.LiveSummaryAsync();
        var edits = new SummaryEditService(mDb);

        var nodeId = await edits.AddNodeAsync(topic, live.Id, null, "Added by hand", this.Cancellation);
        await edits.SetTextAsync(topic, live.Id, nodeId, "Reworded by hand", this.Cancellation);
        await edits.MoveAsync(topic, live.Id, nodeId, NodeMove.Up, this.Cancellation);
        await edits.DeleteNodeAsync(topic, live.Id, nodeId, this.Cancellation);
        await edits.SetBodyAsync(topic, live.Id, "Rewritten", this.Cancellation);

        await using var fresh = this.NewContext();

        Assert.AreEqual(6, (await fresh.Summaries.SingleAsync(this.Cancellation)).Revision);
    }

    [TestMethod]
    public async Task Visibility_and_access_changes_leave_the_revision_alone()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, live) = await this.LiveSummaryAsync();
        var admin = new TopicAdminService(mDb);

        await admin.SetAgentEditableAsync(topic, live.Id, false, this.Cancellation);
        await admin.SetSummaryVisibilityAsync(topic, live.Id, true, this.Cancellation);
        await admin.SetSummaryPolicyAsync(topic, SummaryPolicy.Reviewed, this.Cancellation);

        Assert.AreEqual(1, live.Revision);
    }

    /// <summary>The agent read revision 1, a person then edited, and the agent's write arrives late.</summary>
    [TestMethod]
    public async Task An_agent_write_based_on_an_older_revision_cannot_overwrite_a_human_change()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, live) = await this.LiveSummaryAsync();
        var leaf = live.Nodes.Single(n => n.Text == "CI is slow");

        await new SummaryEditService(mDb).SetTextAsync(topic, live.Id, leaf.Id, "Builds are slow", this.Cancellation);

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => new SummaryService(mDb).SaveDraftAsync(
                topic,
                Draft(Cites("Something else entirely", anna, "fails often")) with { BasedOnRevision = 1 },
                "agent",
                live.Id,
                this.Cancellation));

        await using var fresh = this.NewContext();

        var stored = (await new SummaryService(fresh).FindAsync(live.Id, this.Cancellation))!;

        Assert.AreEqual("stale_revision", rejection.Reason);
        Assert.AreEqual(2, stored.Revision);
        Assert.AreEqual("Builds are slow", stored.Nodes.Single(n => n.Id == leaf.Id).Text);
        Assert.HasCount(1, stored.Nodes);
    }

    [TestMethod]
    public async Task An_agent_write_based_on_the_current_revision_is_accepted_after_a_human_change()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, live) = await this.LiveSummaryAsync();

        await new SummaryEditService(mDb).SetBodyAsync(topic, live.Id, "Edited by hand", this.Cancellation);

        var saved = await new SummaryService(mDb).SaveDraftAsync(
            topic,
            Draft(Cites("CI fails often", anna, "fails often")) with { BasedOnRevision = 2 },
            "agent",
            live.Id,
            this.Cancellation);

        Assert.AreEqual(3, saved.Revision);
    }

    [TestMethod]
    public async Task Revising_the_live_summary_requires_the_revision_it_was_based_on()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, live) = await this.LiveSummaryAsync();

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => new SummaryService(mDb).SaveDraftAsync(
                topic, Draft(Cites("CI fails often", anna, "fails often")), "agent", live.Id, this.Cancellation));

        Assert.AreEqual("revision_required", rejection.Reason);
    }

    /// <summary>
    /// Two writers read revision 1 and both pass the up-front check. The database only lets
    /// the first one land.
    /// </summary>
    [TestMethod]
    public async Task A_write_that_loses_a_race_is_refused_whole()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, live) = await this.LiveSummaryAsync();

        // mDb still holds revision 1, so the next write here starts from it.
        await using (var other = this.NewContext())
        {
            var otherTopic = await other.Topics.SingleAsync(this.Cancellation);

            await new SummaryEditService(other).SetBodyAsync(otherTopic, live.Id, "Landed first", this.Cancellation);
        }

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => new SummaryService(mDb).SaveDraftAsync(
                topic,
                Draft(Cites("Something else entirely", anna, "fails often")) with { BasedOnRevision = 1 },
                "agent",
                live.Id,
                this.Cancellation));

        await using var fresh = this.NewContext();

        var stored = (await new SummaryService(fresh).FindAsync(live.Id, this.Cancellation))!;

        Assert.AreEqual("stale_revision", rejection.Reason);
        Assert.AreEqual("Landed first", stored.Body);
        Assert.AreEqual("CI is slow", stored.Nodes.Single().Text);
        Assert.IsFalse(await fresh.SummaryNodes.AnyAsync(n => n.ArchivedAt != null, this.Cancellation));
    }

    [TestMethod]
    public async Task A_human_edit_that_loses_a_race_is_refused()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, live) = await this.LiveSummaryAsync();

        await using (var other = this.NewContext())
        {
            var otherTopic = await other.Topics.SingleAsync(this.Cancellation);

            await new SummaryEditService(other).SetBodyAsync(otherTopic, live.Id, "Landed first", this.Cancellation);
        }

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => new SummaryEditService(mDb).SetBodyAsync(topic, live.Id, "Arrived second", this.Cancellation));

        await using var fresh = this.NewContext();

        Assert.AreEqual("Landed first", (await fresh.Summaries.SingleAsync(this.Cancellation)).Body);
    }

    private static SummaryDraft Draft(params NodeDraft[] nodes)
    {
        return new SummaryDraft() { Body = "Overview", Nodes = nodes };
    }

    private static NodeDraft Cites(string text, Guid response, string quote)
    {
        return new NodeDraft()
        {
            Text = text,
            References = [new ReferenceDraft { ResponseId = response, Quote = quote }],
        };
    }

    /// <summary>A live topic whose live summary is one cited node, at revision 1.</summary>
    private async Task<(Topic Topic, Guid Anna, Summary Live)> LiveSummaryAsync()
    {
        var topic = NewTopic(ResponseIdentity.Required);
        topic.SummaryPolicy = SummaryPolicy.Live;

        var anna = Revised(new Response() { Id = Guid.NewGuid(), Author = "Anna", AuthTokenHash = "a" }, AnnaSaid);

        topic.Responses.Add(anna);
        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        var live = await new SummaryService(mDb).SaveDraftAsync(
            topic, Draft(Cites("CI is slow", anna.Id, "22 minutes")), "agent", null, this.Cancellation);

        return (topic, anna.Id, live);
    }
}
