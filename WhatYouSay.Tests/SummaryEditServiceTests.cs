using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class SummaryEditServiceTests : DatabaseTest
{
    [TestMethod]
    public async Task A_new_node_lands_at_the_end_of_its_sibling_group()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, summary) = await this.SeededAsync();

        await this.Service().AddNodeAsync(survey, summary.Id, null, "Third", this.Cancellation);

        var reloaded = await this.Service().LoadAsync(survey, summary.Id, this.Cancellation);

        Assert.AreEqual(
            "Meetings, Tooling, Third",
            string.Join(", ", reloaded!.Roots.Select(node => node.Text))
        );
    }

    [TestMethod]
    public async Task Moving_a_node_down_swaps_it_with_the_next_sibling()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, summary) = await this.SeededAsync();
        var first = summary.Roots.First();

        await this.Service().MoveAsync(survey, summary.Id, first.Id, NodeMove.Down, this.Cancellation);

        var reloaded = await this.Service().LoadAsync(survey, summary.Id, this.Cancellation);

        Assert.AreEqual(
            "Tooling, Meetings",
            string.Join(", ", reloaded!.Roots.Select(node => node.Text))
        );
    }

    [TestMethod]
    public async Task Ordering_survives_a_reload_rather_than_falling_back_to_key_order()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, summary) = await this.SeededAsync();
        var first = summary.Roots.First();

        await this.Service().MoveAsync(survey, summary.Id, first.Id, NodeMove.Down, this.Cancellation);

        // The whole point of Ordinal: a fresh context must not reorder by Id and undo it.
        var ordinals = await this.Service().LoadAsync(survey, summary.Id, this.Cancellation);
        var moved = ordinals!.Nodes.First(node => node.Id == first.Id);

        Assert.AreEqual(1, moved.Ordinal);
    }

    [TestMethod]
    public async Task A_move_with_nowhere_to_go_does_nothing()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, summary) = await this.SeededAsync();
        var first = summary.Roots.First();

        await this.Service().MoveAsync(survey, summary.Id, first.Id, NodeMove.Up, this.Cancellation);
        await this.Service().MoveAsync(survey, summary.Id, first.Id, NodeMove.Outdent, this.Cancellation);

        var reloaded = await this.Service().LoadAsync(survey, summary.Id, this.Cancellation);

        Assert.AreEqual(
            "Meetings, Tooling",
            string.Join(", ", reloaded!.Roots.Select(node => node.Text))
        );
    }

    [TestMethod]
    public async Task Indenting_makes_a_node_the_last_child_of_the_sibling_above_it()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, summary) = await this.SeededAsync();
        var second = summary.Roots.Last();

        await this.Service().MoveAsync(survey, summary.Id, second.Id, NodeMove.Indent, this.Cancellation);

        var reloaded = await this.Service().LoadAsync(survey, summary.Id, this.Cancellation);
        var root = reloaded!.Roots.Single();

        Assert.AreEqual("Meetings", root.Text);
        Assert.AreEqual("Too many of them, Tooling", string.Join(", ", root.Children.Select(c => c.Text)));
    }

    [TestMethod]
    public async Task Outdenting_lands_a_node_directly_after_its_old_parent()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, summary) = await this.SeededAsync();
        var child = summary.Roots.First().Children.Single();

        await this.Service().MoveAsync(survey, summary.Id, child.Id, NodeMove.Outdent, this.Cancellation);

        var reloaded = await this.Service().LoadAsync(survey, summary.Id, this.Cancellation);

        Assert.AreEqual(
            "Meetings, Too many of them, Tooling",
            string.Join(", ", reloaded!.Roots.Select(node => node.Text))
        );
    }

    [TestMethod]
    public async Task Deleting_a_node_takes_its_children_with_it()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, summary) = await this.SeededAsync();
        var first = summary.Roots.First();

        await this.Service().DeleteNodeAsync(survey, summary.Id, first.Id, this.Cancellation);

        var reloaded = await this.Service().LoadAsync(survey, summary.Id, this.Cancellation);

        Assert.HasCount(1, reloaded!.Nodes);
        Assert.AreEqual("Tooling", reloaded.Roots.Single().Text);
    }

    [TestMethod]
    public async Task Deleting_closes_the_gap_it_leaves_in_the_sibling_group()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, summary) = await this.SeededAsync();

        await this.Service().AddNodeAsync(survey, summary.Id, null, "Third", this.Cancellation);
        await this.Service().DeleteNodeAsync(
            survey, summary.Id, summary.Roots.First().Id, this.Cancellation);

        var reloaded = await this.Service().LoadAsync(survey, summary.Id, this.Cancellation);

        Assert.AreSequenceEqual([0, 1], reloaded!.Roots.Select(node => node.Ordinal).ToArray());
    }

    [TestMethod]
    public async Task A_hand_typed_quote_is_located_the_same_way_an_agent_s_is()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, summary) = await this.SeededAsync();
        var response = mDb.Responses.First();
        var leaf = summary.Roots.First().Children.Single();

        await this.Service().AddReferenceAsync(
            survey, summary.Id, leaf.Id, response.Id, "nineteen hours", this.Cancellation);

        var reloaded = await this.Service().LoadAsync(survey, summary.Id, this.Cancellation);
        var reference = reloaded!.Nodes.Single(node => node.Id == leaf.Id).References.Single();

        Assert.AreEqual("nineteen hours", response.Body[reference.StartIndex..reference.EndIndex]);
    }

    [TestMethod]
    public async Task A_quote_that_is_not_in_the_response_is_refused_with_the_nearest_text()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, summary) = await this.SeededAsync();
        var response = mDb.Responses.First();
        var leaf = summary.Roots.First().Children.Single();

        var failure = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().AddReferenceAsync(
                survey, summary.Id, leaf.Id, response.Id, "nineteen hours’ worth", this.Cancellation
            )
        );

        Assert.AreEqual("quote_not_found", failure.Reason);
        Assert.IsNotNull(failure.Failures.Single().Nearest);
    }

    [TestMethod]
    public async Task A_published_summary_refuses_every_edit()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, summary) = await this.SeededAsync();

        summary.IsDraft = false;
        summary.IsPublic = true;
        await mDb.SaveChangesAsync(this.Cancellation);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Service().SetBodyAsync(survey, summary.Id, "rewritten", this.Cancellation)
        );
    }

    [TestMethod]
    public async Task A_node_cannot_be_emptied()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, summary) = await this.SeededAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Service().SetTextAsync(
                survey, summary.Id, summary.Roots.First().Id, "   ", this.Cancellation
            )
        );
    }

    [TestMethod]
    public async Task An_edit_reattributes_the_version_to_a_human()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, summary) = await this.SeededAsync();

        Assert.AreEqual("agent", summary.CreatedBy);

        await this.Service().SetBodyAsync(survey, summary.Id, "rewritten", this.Cancellation);

        var reloaded = await this.Service().LoadAsync(survey, summary.Id, this.Cancellation);

        Assert.AreEqual("human", reloaded!.CreatedBy);
    }

    [TestMethod]
    public async Task A_summary_from_another_survey_is_not_reachable()
    {
        using var activity = TestTelemetry.Source.Start();

        var (_, summary) = await this.SeededAsync();
        var other = NewSurvey(ResponseIdentity.Required);

        mDb.Surveys.Add(other);
        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.IsNull(await this.Service().LoadAsync(other, summary.Id, this.Cancellation));
    }

    /// <summary>Two roots, one of which has a child, plus a response to quote.</summary>
    private async Task<(Survey Survey, Summary Summary)> SeededAsync()
    {
        var survey = NewSurvey(ResponseIdentity.Required);
        survey.IsAcceptingResponses = false;

        var response = new Response()
        {
            Id = Guid.CreateVersion7(),
            Body = "I counted nineteen hours of scheduled calls last week.",
            AuthTokenHash = Guid.NewGuid().ToString("n"),
            Author = "Sam",
        };

        var summary = new Summary()
        {
            Body = "Overview",
            CreatedBy = "agent",
        };

        var meetings = new SummaryNode() { Text = "Meetings", Ordinal = 0 };
        var detail = new SummaryNode() { Text = "Too many of them", Parent = meetings, Ordinal = 0 };
        var tooling = new SummaryNode() { Text = "Tooling", Ordinal = 1 };

        summary.Nodes.Add(meetings);
        summary.Nodes.Add(detail);
        summary.Nodes.Add(tooling);

        survey.Responses.Add(response);
        survey.Summaries.Add(summary);

        mDb.Surveys.Add(survey);
        await mDb.SaveChangesAsync(this.Cancellation);

        SummaryTree.Assemble(summary);

        return (survey, summary);
    }

    private SummaryEditService Service()
    {
        return new SummaryEditService(mDb);
    }
}
