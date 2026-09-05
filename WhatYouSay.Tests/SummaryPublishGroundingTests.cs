using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

/// <summary>
/// The branch rule at the other end of the pipeline. An agent cannot submit an ungrounded
/// tree; a person can write one, so here the rule classifies rather than refuses and
/// publishing lets it through.
/// </summary>
[TestClass]
public class SummaryPublishGroundingTests : DatabaseTest
{
    [TestMethod]
    public async Task A_grounded_tree_publishes()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, summary) = await this.SeededAsync(citeTheLeaf: true);

        await this.Admin().SetSummaryVisibilityAsync(topic, summary.Id, true, this.Cancellation);

        Assert.IsTrue(summary.IsVisibleToPublic);
    }

    /// <summary>
    /// Publishing used to refuse this. It cannot any more: a page of meeting notes has
    /// nothing to cite, and refusing would make it unpublishable. What the rule still
    /// does is name the node, so the editor and the summary page can show what it rests
    /// on — which was always the part a reader needed.
    /// </summary>
    [TestMethod]
    public async Task A_branch_ending_in_nothing_anybody_wrote_publishes_and_is_marked()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, summary) = await this.SeededAsync(citeTheLeaf: false);

        await this.Admin().SetSummaryVisibilityAsync(topic, summary.Id, true, this.Cancellation);

        Assert.IsTrue(summary.IsVisibleToPublic);
        Assert.IsNotEmpty(SummaryGrounding.Ungrounded(summary));
    }

    [TestMethod]
    public async Task Support_still_inherits_down_a_branch()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, summary) = await this.SeededAsync(citeTheLeaf: false, citeTheHeading: true);

        await this.Admin().SetSummaryVisibilityAsync(topic, summary.Id, true, this.Cancellation);

        Assert.IsTrue(summary.IsVisibleToPublic);
    }

    [TestMethod]
    public async Task Withdrawing_the_only_cited_response_makes_the_branch_ungrounded()
    {
        using var activity = TestTelemetry.Source.Start();

        var (_, summary) = await this.SeededAsync(citeTheLeaf: true);

        Assert.IsEmpty(SummaryGrounding.Ungrounded(summary));

        // References to a withdrawn response are filtered out of every read, so the node
        // they supported has to stop counting as supported too.
        mDb.Responses.First().IsDeleted = true;
        await mDb.SaveChangesAsync(this.Cancellation);

        await using var fresh = this.NewContext();
        var reloaded = await new SummaryService(fresh).FindAsync(summary.Id, this.Cancellation);

        Assert.IsNotEmpty(SummaryGrounding.Ungrounded(reloaded!));
    }

    [TestMethod]
    public async Task Unpublishing_an_ungrounded_summary_still_works()
    {
        using var activity = TestTelemetry.Source.Start();

        var (topic, summary) = await this.SeededAsync(citeTheLeaf: false);

        summary.IsDraft = false;
        summary.IsPublic = true;
        await mDb.SaveChangesAsync(this.Cancellation);

        // Taking something back must never be blocked by what is wrong with it.
        await this.Admin().SetSummaryVisibilityAsync(topic, summary.Id, false, this.Cancellation);

        Assert.IsFalse(summary.IsVisibleToPublic);
    }

    private async Task<(Topic Topic, Summary Summary)> SeededAsync(
        bool citeTheLeaf,
        bool citeTheHeading = false
    )
    {
        var topic = NewTopic(ResponseIdentity.Required);
        topic.IsAcceptingResponses = false;

        var response = new Response()
        {
            Id = Guid.CreateVersion7(),
            Body = "CI is slow and the build takes twenty minutes.",
            AuthTokenHash = Guid.NewGuid().ToString("n"),
            IsFrozen = true,
        };

        var summary = new Summary() { Body = "Overview" };
        var heading = new SummaryNode() { Text = "Tooling" };
        var leaf = new SummaryNode() { Text = "CI is slow", Parent = heading };

        if (citeTheLeaf)
        {
            leaf.References.Add(Cite(response, "CI is slow"));
        }

        if (citeTheHeading)
        {
            heading.References.Add(Cite(response, "twenty minutes"));
        }

        summary.Nodes.Add(heading);
        summary.Nodes.Add(leaf);

        topic.Responses.Add(response);
        topic.Summaries.Add(summary);

        mDb.Topics.Add(topic);
        await mDb.SaveChangesAsync(this.Cancellation);

        return (topic, summary);
    }

    private static SummaryNodeReference Cite(Response response, string quote)
    {
        var at = response.Body.IndexOf(quote, StringComparison.Ordinal);

        return new SummaryNodeReference()
        {
            ResponseId = response.Id,
            Quote = quote,
            StartIndex = at,
            EndIndex = at + quote.Length,
        };
    }

    private TopicAdminService Admin()
    {
        return new TopicAdminService(mDb);
    }
}
