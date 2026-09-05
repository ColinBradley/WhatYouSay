using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

/// <summary>
/// A revision names what it keeps rather than rebuilding the tree, so id and text together
/// say what the agent is doing with each node. The point of the bare-id form is that a node
/// nobody can cite survives an agent pass, which is what these check.
/// </summary>
[TestClass]
public class SummaryWriteContractTests : DatabaseTest
{
    private const string AnnaSaid = "CI is slow. A full run is 22 minutes and it fails often.";

    [TestMethod]
    public async Task A_carried_node_keeps_its_id_its_words_and_its_citations()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response, summary) = await this.DraftedAsync();
        var leaf = summary.Nodes.Single(n => n.Text == "CI is slow");

        var revised = await this.Service().SaveDraftAsync(
            topic,
            new SummaryDraft()
            {
                Body = "Overview",
                Nodes = [Heading("Tooling", Carry(leaf.Id))],
            },
            "agent",
            summary.Id,
            this.Cancellation);

        var kept = revised.Nodes.Single(n => n.Id == leaf.Id);

        Assert.AreEqual("CI is slow", kept.Text);
        Assert.AreEqual("22 minutes", kept.References.Single().Quote);
    }

    [TestMethod]
    public async Task A_node_the_agent_cannot_cite_survives_being_carried_forward()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response, summary) = await this.DraftedAsync();

        // The shape a person leaves behind: a leaf asserting something with no quote under
        // it. Submitted as text it would be refused, so carrying it is the only way through.
        var byHand = new SummaryNode() { Text = "Morale is flat", Ordinal = 1 };

        summary.Nodes.Add(byHand);
        await mDb.SaveChangesAsync(this.Cancellation);

        var leaf = summary.Nodes.Single(n => n.Text == "CI is slow");

        var revised = await this.Service().SaveDraftAsync(
            topic,
            new SummaryDraft()
            {
                Body = "Overview",
                Nodes = [Heading("Tooling", Carry(leaf.Id)), Carry(byHand.Id)],
            },
            "agent",
            summary.Id,
            this.Cancellation);

        Assert.AreEqual("Morale is flat", revised.Nodes.Single(n => n.Id == byHand.Id).Text);
    }

    [TestMethod]
    public async Task Revising_a_node_asserts_it_afresh_and_so_needs_a_quote()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response, summary) = await this.DraftedAsync();
        var leaf = summary.Nodes.Single(n => n.Text == "CI is slow");

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                topic,
                new SummaryDraft()
                {
                    Body = "Overview",
                    Nodes = [Heading("Tooling", new NodeDraft { Id = leaf.Id, Text = "CI is intolerable" })],
                },
                "agent",
                summary.Id,
                this.Cancellation));

        Assert.AreEqual("branch_without_citation", rejection.Reason);
        Assert.AreEqual("CI is slow", (await this.FreshAsync(leaf.Id)).Text);
    }

    [TestMethod]
    public async Task A_carried_node_that_is_cited_grounds_what_hangs_under_it()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response, summary) = await this.DraftedAsync();
        var cited = summary.Nodes.Single(n => n.Text == "CI is slow");

        var revised = await this.Service().SaveDraftAsync(
            topic,
            new SummaryDraft()
            {
                Body = "Overview",
                Nodes = [Carry(cited.Id) with { Children = [Node("Builds queue behind it")] }],
            },
            "agent",
            summary.Id,
            this.Cancellation);

        Assert.IsTrue(revised.Nodes.Any(n => n.Text == "Builds queue behind it"));
    }

    [TestMethod]
    public async Task A_carried_node_with_no_citation_of_its_own_grounds_nothing()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response, summary) = await this.DraftedAsync();
        var heading = summary.Nodes.Single(n => n.Text == "Tooling");

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                topic,
                new SummaryDraft()
                {
                    Body = "Overview",
                    Nodes = [Carry(heading.Id) with { Children = [Node("Builds queue behind it")] }],
                },
                "agent",
                summary.Id,
                this.Cancellation));

        Assert.AreEqual("branch_without_citation", rejection.Reason);
    }

    [TestMethod]
    public async Task A_stored_id_left_out_of_the_payload_is_deleted()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response, summary) = await this.DraftedAsync();
        var leaf = summary.Nodes.Single(n => n.Text == "CI is slow");

        var revised = await this.Service().SaveDraftAsync(
            topic,
            new SummaryDraft()
            {
                Body = "Overview",
                Nodes = [Cites(response, "Reviews lag", "22 minutes")],
            },
            "agent",
            summary.Id,
            this.Cancellation);

        Assert.AreEqual(1, revised.Nodes.Count);
        Assert.IsFalse(await mDb.SummaryNodes.AnyAsync(n => n.Id == leaf.Id, this.Cancellation));
    }

    /// <summary>
    /// The case the deletion pass is ordered for: a kept node moved out from under a deleted
    /// one must not cascade away with its old parent.
    /// </summary>
    [TestMethod]
    public async Task A_child_reparented_off_a_deleted_node_survives_it()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response, summary) = await this.DraftedAsync();
        var heading = summary.Nodes.Single(n => n.Text == "Tooling");
        var leaf = summary.Nodes.Single(n => n.Text == "CI is slow");

        var revised = await this.Service().SaveDraftAsync(
            topic,
            new SummaryDraft() { Body = "Overview", Nodes = [Carry(leaf.Id)] },
            "agent",
            summary.Id,
            this.Cancellation);

        var survivor = await this.FreshAsync(leaf.Id);

        Assert.IsNull(survivor.ParentId);
        Assert.AreEqual("CI is slow", survivor.Text);
        Assert.IsFalse(await mDb.SummaryNodes.AnyAsync(n => n.Id == heading.Id, this.Cancellation));
    }

    [TestMethod]
    public async Task An_id_from_another_summary_is_refused()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response, summary) = await this.DraftedAsync();
        var other = await this.Service().SaveDraftAsync(
            topic,
            new SummaryDraft() { Body = "Other", Nodes = [Cites(response, "Separate", "22 minutes")] },
            "agent",
            null,
            this.Cancellation);

        var stranger = other.Nodes.Single();

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                topic,
                new SummaryDraft() { Body = "Overview", Nodes = [Carry(stranger.Id)] },
                "agent",
                summary.Id,
                this.Cancellation));

        Assert.AreEqual("unknown_node", rejection.Reason);
    }

    [TestMethod]
    public async Task A_node_claimed_twice_is_refused()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response, summary) = await this.DraftedAsync();
        var leaf = summary.Nodes.Single(n => n.Text == "CI is slow");

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                topic,
                new SummaryDraft() { Body = "Overview", Nodes = [Carry(leaf.Id), Carry(leaf.Id)] },
                "agent",
                summary.Id,
                this.Cancellation));

        Assert.AreEqual("duplicate_node_id", rejection.Reason);
    }

    [TestMethod]
    public async Task A_node_with_neither_id_nor_text_is_refused()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response, summary) = await this.DraftedAsync();

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                topic,
                new SummaryDraft() { Body = "Overview", Nodes = [new NodeDraft()] },
                "agent",
                summary.Id,
                this.Cancellation));

        Assert.AreEqual("node_without_text", rejection.Reason);
    }

    [TestMethod]
    public async Task Citations_without_text_are_refused_as_ambiguous()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, response, summary) = await this.DraftedAsync();
        var leaf = summary.Nodes.Single(n => n.Text == "CI is slow");

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                topic,
                new SummaryDraft()
                {
                    Body = "Overview",
                    Nodes =
                    [
                        Carry(leaf.Id) with
                        {
                            References = [new ReferenceDraft { ResponseId = response, Quote = "22 minutes" }],
                        },
                    ],
                },
                "agent",
                summary.Id,
                this.Cancellation));

        Assert.AreEqual("references_without_text", rejection.Reason);
    }

    [TestMethod]
    public async Task An_id_on_a_brand_new_summary_names_nothing_and_is_refused()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, _, _) = await this.DraftedAsync();

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                topic,
                new SummaryDraft() { Body = "Overview", Nodes = [Carry(1)] },
                "agent",
                null,
                this.Cancellation));

        Assert.AreEqual("unknown_node", rejection.Reason);
    }

    private static NodeDraft Carry(int id) =>
        new() { Id = id };

    private static NodeDraft Node(string text) =>
        new() { Text = text };

    private static NodeDraft Heading(string text, params NodeDraft[] children) =>
        Node(text) with { Children = children };

    private static NodeDraft Cites(Guid response, string text, string quote)
    {
        return Node(text) with
        {
            References = [new ReferenceDraft { ResponseId = response, Quote = quote }],
        };
    }

    private SummaryService Service() =>
        new(mDb);

    private async Task<SummaryNode> FreshAsync(int nodeId)
    {
        await using var fresh = this.NewContext();

        return await fresh.SummaryNodes.SingleAsync(n => n.Id == nodeId, this.Cancellation);
    }

    /// <summary>An agent draft of "Tooling" over a cited "CI is slow", the smallest revisable tree.</summary>
    private async Task<(Topic Topic, Guid Response, Summary Summary)> DraftedAsync()
    {
        var topic = NewTopic(ResponseIdentity.Required);
        topic.IsAcceptingResponses = false;

        var anna = new Response()
        {
            Id = Guid.NewGuid(),
            Body = AnnaSaid,
            Author = "Anna",
            AuthTokenHash = "a",
            IsFrozen = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        topic.Responses.Add(anna);
        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        var summary = await this.Service().SaveDraftAsync(
            topic,
            new SummaryDraft()
            {
                Body = "Overview",
                Nodes = [Heading("Tooling", Cites(anna.Id, "CI is slow", "22 minutes"))],
            },
            "agent",
            null,
            this.Cancellation);

        return (topic, anna.Id, summary);
    }
}
