using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

/// <summary>
/// A summariser write archives the nodes it leaves out, because an ordinary revision is no
/// reason to delete the reactions and comments people left on them.
/// </summary>
[TestClass]
public class ArchivedNodeTests : DatabaseTest
{
    private const string AnnaSaid = "CI is slow. A full run is 22 minutes and it fails often.";

    private const string TomSaid = "On-call was brutal, fourteen pages in a week.";

    [TestMethod]
    public async Task A_node_left_out_of_the_live_tree_keeps_its_feedback_for_the_admin()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, anna, tom, live) = await this.LiveSummaryAsync();
        var heading = live.Nodes.Single(n => n.Text == "Tooling");
        var dropped = live.Nodes.Single(n => n.Text == "CI is slow");
        var reader = Secrets.NewToken();

        await new CommentService(mDb).AddAsync(topic, dropped.Id, reader, "Not quite what I meant.", "Anna", this.Cancellation);
        await new ReactionService(mDb).ToggleAsync(topic, dropped.Id, reader, ReactionKind.Agree, this.Cancellation);

        await this.ReviseAsync(topic, live, Carry(heading.Id, Cites("On-call hurts", tom, "fourteen pages")));

        await using var fresh = this.NewContext();

        var current = (await new SummaryService(fresh).FindAsync(live.Id, this.Cancellation))!;
        var archived = (await new SummaryService(fresh).ListArchivedAsync(topic, live.Id, this.Cancellation)).Single();

        Assert.IsFalse(current.Nodes.Any(n => n.Id == dropped.Id));
        Assert.IsFalse(SummaryTree.Flatten(current.Roots).Any(n => n.Id == dropped.Id));
        Assert.AreEqual(dropped.Id, archived.Id);
        Assert.IsNotNull(archived.ArchivedAt);
        Assert.AreEqual("Tooling", archived.Parent!.Text);
        Assert.AreEqual("22 minutes", archived.References.Single().Quote);
        Assert.AreEqual("Not quite what I meant.", archived.Comments.Single().Body);
        Assert.AreEqual(ReactionKind.Agree, archived.Reactions.Single().Kind);
    }

    [TestMethod]
    public async Task An_archived_node_takes_no_new_reactions_or_comments()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, tom, live) = await this.LiveSummaryAsync();
        var heading = live.Nodes.Single(n => n.Text == "Tooling");
        var dropped = live.Nodes.Single(n => n.Text == "CI is slow");
        var reader = Secrets.NewToken();

        await this.ReviseAsync(topic, live, Carry(heading.Id, Cites("On-call hurts", tom, "fourteen pages")));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => new ReactionService(mDb).ToggleAsync(topic, dropped.Id, reader, ReactionKind.Agree, this.Cancellation));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => new CommentService(mDb).AddAsync(topic, dropped.Id, reader, "Too late", null, this.Cancellation));
    }

    [TestMethod]
    public async Task An_archived_node_cannot_be_carried_forward()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, tom, live) = await this.LiveSummaryAsync();
        var heading = live.Nodes.Single(n => n.Text == "Tooling");
        var dropped = live.Nodes.Single(n => n.Text == "CI is slow");

        await this.ReviseAsync(topic, live, Carry(heading.Id, Cites("On-call hurts", tom, "fourteen pages")));

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.ReviseAsync(topic, live, Carry(heading.Id, new NodeDraft() { Id = dropped.Id })));

        Assert.AreEqual("unknown_node", rejection.Reason);
    }

    /// <summary>
    /// The summary handed back holds only the current tree, and later saves through the
    /// same context leave the archived node exactly as it was archived.
    /// </summary>
    [TestMethod]
    public async Task Archiving_survives_further_saves_through_the_same_context()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, tom, live) = await this.LiveSummaryAsync();
        var heading = live.Nodes.Single(n => n.Text == "Tooling");
        var dropped = live.Nodes.Single(n => n.Text == "CI is slow");

        var revised = await this.ReviseAsync(
            topic, live, Carry(heading.Id, Cites("On-call hurts", tom, "fourteen pages")));

        Assert.IsFalse(revised.Nodes.Any(n => n.Id == dropped.Id));
        Assert.HasCount(1, revised.Roots.Single().Children);

        await new SummaryEditService(mDb).SetBodyAsync(topic, live.Id, "Edited after", this.Cancellation);
        await new SummaryEditService(mDb).AddNodeAsync(topic, live.Id, heading.Id, "Added after", this.Cancellation);

        await using var fresh = this.NewContext();

        var stored = await fresh.SummaryNodes.SingleAsync(n => n.Id == dropped.Id, this.Cancellation);

        Assert.IsNotNull(stored.ArchivedAt);
        Assert.AreEqual(heading.Id, stored.ParentId);
        Assert.AreEqual(1, await fresh.References.CountAsync(r => r.NodeId == dropped.Id, this.Cancellation));
    }

    /// <summary>A person deleting a node is a deliberate act, so what was archived under it goes too.</summary>
    [TestMethod]
    public async Task Deleting_a_node_by_hand_takes_the_archived_nodes_under_it()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, tom, live) = await this.LiveSummaryAsync();
        var heading = live.Nodes.Single(n => n.Text == "Tooling");
        var dropped = live.Nodes.Single(n => n.Text == "CI is slow");

        await this.ReviseAsync(topic, live, Carry(heading.Id, Cites("On-call hurts", tom, "fourteen pages")));
        await new SummaryEditService(mDb).DeleteNodeAsync(topic, live.Id, heading.Id, this.Cancellation);

        await using var fresh = this.NewContext();

        Assert.IsFalse(await fresh.SummaryNodes.AnyAsync(n => n.Id == dropped.Id, this.Cancellation));
    }

    private static SummaryDraft Draft(params NodeDraft[] nodes)
    {
        return new SummaryDraft() { Body = "Overview", Nodes = nodes };
    }

    private static NodeDraft Carry(int id, params NodeDraft[] children)
    {
        return new NodeDraft() { Id = id, Children = children };
    }

    private static NodeDraft Cites(string text, Guid response, string quote)
    {
        return new NodeDraft()
        {
            Text = text,
            References = [new ReferenceDraft { ResponseId = response, Quote = quote }],
        };
    }

    /// <summary>Replaces the live summary's tree, based on the revision it is at now.</summary>
    private async Task<Summary> ReviseAsync(Topic topic, Summary live, params NodeDraft[] nodes)
    {
        await using var fresh = this.NewContext();

        var revision = (await fresh.Summaries.SingleAsync(s => s.Id == live.Id, this.Cancellation)).Revision;

        return await new SummaryService(mDb).SaveDraftAsync(
            topic, Draft(nodes) with { BasedOnRevision = revision }, "agent", live.Id, this.Cancellation);
    }

    /// <summary>A live topic whose live summary is "Tooling" over a cited "CI is slow".</summary>
    private async Task<(Topic Topic, Guid Anna, Guid Tom, Summary Live)> LiveSummaryAsync()
    {
        var topic = NewTopic(ResponseIdentity.Required);
        topic.SummaryPolicy = SummaryPolicy.Live;

        var anna = Revised(new Response() { Id = Guid.NewGuid(), Author = "Anna", AuthTokenHash = "a" }, AnnaSaid);
        var tom = Revised(new Response() { Id = Guid.NewGuid(), Author = "Tom", AuthTokenHash = "t" }, TomSaid);

        topic.Responses.Add(anna);
        topic.Responses.Add(tom);
        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        var live = await new SummaryService(mDb).SaveDraftAsync(
            topic,
            Draft(new NodeDraft() { Text = "Tooling", Children = [Cites("CI is slow", anna.Id, "22 minutes")] }),
            "agent",
            null,
            this.Cancellation);

        return (topic, anna.Id, tom.Id, live);
    }
}
