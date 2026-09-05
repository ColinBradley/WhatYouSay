using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;
using WhatYouSay.Data.Seed;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class SummaryServiceTests : DatabaseTest
{
    [TestMethod]
    public async Task Only_published_summaries_are_visible()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = NewTopic(ResponseIdentity.Required);

        topic.Summaries.Add(Summary("draft", isDraft: true, isPublic: true));
        topic.Summaries.Add(Summary("internal", isDraft: false, isPublic: false));

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.IsNull(await this.Service().FindLatestVisibleAsync(topic.Id, this.Cancellation));
        Assert.IsEmpty(await this.Service().ListVisibleAsync(topic.Id, this.Cancellation));
    }

    [TestMethod]
    public async Task An_unpublished_version_is_still_reachable_by_id()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = NewTopic(ResponseIdentity.Required);
        var draft = Summary("draft", isDraft: true, isPublic: false);

        topic.Summaries.Add(draft);

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        // Visibility is the caller's decision, not the loader's: the summary page can then
        // show a draft to an admin without a second way of loading one.
        Assert.IsNotNull(await this.Service().FindAsync(draft.Id, this.Cancellation));
        Assert.HasCount(1, await this.Service().ListAllAsync(topic.Id, this.Cancellation));
    }

    [TestMethod]
    public async Task The_newest_published_version_wins()
    {
        using var activity = TestTelemetry.Source.Start();

        var topic = NewTopic(ResponseIdentity.Required);

        topic.Summaries.Add(Summary("older", isDraft: false, isPublic: true, daysAgo: 5));
        topic.Summaries.Add(Summary("newer", isDraft: false, isPublic: true, daysAgo: 1));

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        var latest = await this.Service().FindLatestVisibleAsync(topic.Id, this.Cancellation);

        Assert.IsNotNull(latest);
        Assert.AreEqual("newer", latest.Body);
        Assert.HasCount(2, await this.Service().ListVisibleAsync(topic.Id, this.Cancellation));
    }

    [TestMethod]
    public async Task References_to_withdrawn_responses_disappear_but_the_node_survives()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, this.Cancellation);

        var topic = await mDb.Topics.SingleAsync(s => s.Code == "spr47ab", this.Cancellation);
        var summary = (await this.Service().FindLatestVisibleAsync(topic.Id, this.Cancellation))!;

        var node = summary.Nodes.First(n => n.References.Count > 0);
        var citedResponseId = node.References[0].ResponseId;
        var nodeId = node.Id;
        var before = node.References.Count;

        var cited = await mDb.Responses.SingleAsync(r => r.Id == citedResponseId, this.Cancellation);
        cited.IsDeleted = true;
        await mDb.SaveChangesAsync(this.Cancellation);

        mDb.ChangeTracker.Clear();

        var reloaded = (await this.Service().FindLatestVisibleAsync(topic.Id, this.Cancellation))!;
        var reloadedNode = reloaded.Nodes.Single(n => n.Id == nodeId);

        Assert.HasCount(before - 1, reloadedNode.References);
        Assert.DoesNotContain(r => r.ResponseId == citedResponseId, reloadedNode.References);
    }

    [TestMethod]
    public async Task Every_seeded_quote_actually_occurs_where_it_claims_to()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, this.Cancellation);

        var topic = await mDb.Topics.SingleAsync(s => s.Code == "spr47ab", this.Cancellation);
        var summary = (await this.Service().FindLatestVisibleAsync(topic.Id, this.Cancellation))!;

        var references = summary.Nodes.SelectMany(n => n.References).ToList();

        Assert.IsNotEmpty(references);

        foreach (var reference in references)
        {
            Assert.IsTrue(
                QuoteLocator.Matches(
                    reference.Response.Body, reference.Quote, reference.StartIndex, reference.EndIndex),
                $"Quote does not sit at its recorded offsets: \"{reference.Quote}\"");
        }
    }

    [TestMethod]
    public async Task Every_seeded_branch_resolves_to_a_citation()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, this.Cancellation);

        var topic = await mDb.Topics.SingleAsync(s => s.Code == "spr47ab", this.Cancellation);
        var summary = (await this.Service().FindLatestVisibleAsync(topic.Id, this.Cancellation))!;

        Assert.IsNotEmpty(summary.Nodes);

        foreach (var root in summary.Roots)
        {
            Grounded(root, supported: false);
        }

        static void Grounded(SummaryNode node, bool supported)
        {
            var grounded = supported || node.References.Count > 0;

            if (node.Children.Count == 0)
            {
                Assert.IsTrue(grounded, $"Nothing cites \"{node.Text}\" or anything above it.");

                return;
            }

            foreach (var child in node.Children)
            {
                Grounded(child, grounded);
            }
        }
    }

    [TestMethod]
    public async Task The_seeded_tree_goes_deeper_than_two_levels()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, this.Cancellation);

        var topic = await mDb.Topics.SingleAsync(s => s.Code == "spr47ab", this.Cancellation);
        var summary = (await this.Service().FindLatestVisibleAsync(topic.Id, this.Cancellation))!;

        // Two levels is the topic-and-point shape the tree replaced, so the seed has to
        // exercise something the old model could not express.
        Assert.IsGreaterThan(2, SummaryTree.Depth(summary.Roots));
    }

    private SummaryService Service() =>
        new(mDb);

    private static Summary Summary(string body, bool isDraft, bool isPublic, int daysAgo = 0) =>
        new()
    {
        Id = Guid.CreateVersion7(),
        Body = body,
        IsDraft = isDraft,
        IsPublic = isPublic,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
    };
}
