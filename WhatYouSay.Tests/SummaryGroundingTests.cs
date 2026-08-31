using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class SummaryGroundingTests : DatabaseTest
{
    private const string AnnaSaid = "CI is slow. A full run is 22 minutes and it fails often.";

    private const string TomSaid = "On-call was brutal, fourteen pages in a week.";

    [TestMethod]
    public async Task A_grounded_draft_is_accepted_and_its_offsets_are_computed()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, responses) = await this.ClosedSurveyAsync();

        var summary = await this.Service().SaveDraftAsync(
            survey,
            Draft(Cites("CI is slow enough to change behaviour", (responses[0], "22 minutes"))),
            "agent",
            null,
            this.Cancellation);

        var reference = summary.Nodes.SelectMany(n => n.References).Single();

        Assert.AreEqual("22 minutes", AnnaSaid[reference.StartIndex..reference.EndIndex]);
        Assert.IsTrue(summary.IsDraft);
        Assert.AreEqual("agent", summary.CreatedBy);
    }

    [TestMethod]
    public async Task A_tree_is_stored_flat_and_reads_back_nested()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, responses) = await this.ClosedSurveyAsync();

        var cited = Cites("CI is slow", (responses[0], "22 minutes")) with
        {
            Children = [Node("Which is why people batch commits")],
        };

        var saved = await this.Service().SaveDraftAsync(
            survey,
            Draft(cited),
            "agent",
            null,
            this.Cancellation);

        mDb.ChangeTracker.Clear();

        var reloaded = (await this.Service().FindAsync(saved.Id, this.Cancellation))!;
        var root = reloaded.Roots.Single();

        Assert.HasCount(3, reloaded.Nodes);
        Assert.AreEqual("Tooling", root.Text);
        Assert.AreEqual("CI is slow", root.Children.Single().Text);
        Assert.AreEqual("Which is why people batch commits", root.Children.Single().Children.Single().Text);
        Assert.AreEqual(3, SummaryTree.Depth(reloaded.Roots));
    }

    [TestMethod]
    public async Task Support_inherits_down_a_branch()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, responses) = await this.ClosedSurveyAsync();

        // The leaf cites nothing of its own, which is legal because the node above it does.
        // Making it re-cite would only copy one quote twice.
        var cited = Cites("CI is slow", (responses[0], "22 minutes")) with
        {
            Children = [Node("Enough that a green build is not a gate")],
        };

        var summary = await this.Service().SaveDraftAsync(
            survey,
            Draft(cited),
            "agent",
            null,
            this.Cancellation);

        Assert.HasCount(3, summary.Nodes);
    }

    [TestMethod]
    public async Task A_leaf_nothing_supports_is_rejected()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, _) = await this.ClosedSurveyAsync();

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                survey,
                Draft(Node("Morale is low")),
                "agent",
                null,
                this.Cancellation));

        Assert.AreEqual("branch_without_citation", rejection.Reason);
        Assert.AreEqual("/nodes/0/children/0/references", rejection.Failures.Single().Path);
    }

    [TestMethod]
    public async Task A_heading_needs_no_citation_of_its_own()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, responses) = await this.ClosedSurveyAsync();

        // Nothing marks the root as a heading. It passes because the requirement lands on
        // what hangs below it, which is cited.
        var summary = await this.Service().SaveDraftAsync(
            survey,
            Draft(Cites("CI is slow", (responses[0], "22 minutes"))),
            "agent",
            null,
            this.Cancellation);

        Assert.IsEmpty(summary.Roots.Single().References);
    }

    [TestMethod]
    public async Task A_childless_node_with_no_citation_is_rejected_whatever_it_meant_to_be()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, _) = await this.ClosedSurveyAsync();

        // Read as a heading this is an empty section; read as a finding it is uncited. The
        // untyped rule cannot tell the two apart, and rejects both.
        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                survey,
                new SummaryDraft()
                {
                    Body = "Overview",
                    Nodes = [Node("Tooling")],
                },
                "agent",
                null,
                this.Cancellation));

        Assert.AreEqual("branch_without_citation", rejection.Reason);
        Assert.AreEqual("/nodes/0/references", rejection.Failures.Single().Path);
    }

    [TestMethod]
    public async Task A_fabricated_quote_is_rejected()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, responses) = await this.ClosedSurveyAsync();

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                survey,
                Draft(Cites("CI takes 45 minutes", (responses[0], "45 minutes"))),
                "agent",
                null,
                this.Cancellation));

        Assert.AreEqual("quote_not_found", rejection.Reason);

        Assert.Contains("45 minutes", rejection.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task Citing_a_response_from_another_survey_is_rejected()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, _) = await this.ClosedSurveyAsync();

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                survey,
                Draft(Cites("Something", (Guid.NewGuid(), "22 minutes"))),
                "agent",
                null,
                this.Cancellation));

        Assert.AreEqual("unknown_response", rejection.Reason);
    }

    [TestMethod]
    public async Task Citing_a_withdrawn_response_is_rejected()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, responses) = await this.ClosedSurveyAsync();

        var withdrawn = await mDb.Responses.SingleAsync(r => r.Id == responses[1], this.Cancellation);
        withdrawn.IsDeleted = true;
        await mDb.SaveChangesAsync(this.Cancellation);

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                survey,
                Draft(Cites("On-call hurt", (responses[1], "fourteen pages"))),
                "agent",
                null,
                this.Cancellation));

        Assert.AreEqual("unknown_response", rejection.Reason);
    }

    [TestMethod]
    public async Task An_open_survey_cannot_be_summarised()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, responses) = await this.ClosedSurveyAsync();
        survey.IsAcceptingResponses = true;
        await mDb.SaveChangesAsync(this.Cancellation);

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                survey,
                Draft(Cites("CI is slow", (responses[0], "22 minutes"))),
                "agent",
                null,
                this.Cancellation));

        Assert.AreEqual("survey_open", rejection.Reason);
    }

    [TestMethod]
    public async Task A_rejected_draft_writes_nothing_at_all()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, responses) = await this.ClosedSurveyAsync();

        // The good branch must not survive the bad one.
        var draft = new SummaryDraft()
        {
            Body = "Overview",
            Nodes =
            [
                Heading("Tooling", Cites("CI is slow", (responses[0], "22 minutes"))),
                Heading("On-call", Cites("Pager noise", (responses[1], "invented text"))),
            ],
        };

        await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(survey, draft, "agent", null, this.Cancellation));

        Assert.AreEqual(0, await mDb.Summaries.CountAsync(this.Cancellation));
        Assert.AreEqual(0, await mDb.SummaryNodes.CountAsync(this.Cancellation));
        Assert.AreEqual(0, await mDb.References.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task A_published_summary_cannot_be_rewritten_by_the_agent()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, responses) = await this.ClosedSurveyAsync();
        var service = this.Service();

        var draft = Draft(Cites("CI is slow", (responses[0], "22 minutes")));
        var summary = await service.SaveDraftAsync(survey, draft, "agent", null, this.Cancellation);

        summary.IsDraft = false;
        await mDb.SaveChangesAsync(this.Cancellation);

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => service.SaveDraftAsync(survey, draft, "agent", summary.Id, this.Cancellation));

        Assert.AreEqual("summary_published", rejection.Reason);
    }

    [TestMethod]
    public async Task A_draft_can_be_replaced_wholesale()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, responses) = await this.ClosedSurveyAsync();
        var service = this.Service();

        var first = await service.SaveDraftAsync(
            survey,
            Draft(Cites("CI is slow", (responses[0], "22 minutes"))),
            "agent",
            null,
            this.Cancellation);

        var second = await service.SaveDraftAsync(
            survey,
            Draft(Cites("On-call was noisy", (responses[1], "fourteen pages"))),
            "agent",
            first.Id,
            this.Cancellation);

        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(1, await mDb.Summaries.CountAsync(this.Cancellation));

        // The old tree is gone rather than merged with the new one.
        Assert.AreEqual(2, await mDb.SummaryNodes.CountAsync(this.Cancellation));

        Assert.AreEqual(
            "On-call was noisy",
            await mDb.SummaryNodes
                .Where(n => n.ParentId != null)
                .Select(n => n.Text)
                .SingleAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task Every_problem_in_a_draft_comes_back_from_one_attempt()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, responses) = await this.ClosedSurveyAsync();

        // One retry should be able to fix the lot, so validation does not stop at the first.
        var draft = new SummaryDraft()
        {
            Body = "Overview",
            Nodes =
            [
                Heading(
                    "Tooling",
                    Cites("Invented", (responses[0], "45 minutes")),
                    Node("Uncited")),
                Node("Empty"),
            ],
        };

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(survey, draft, "agent", null, this.Cancellation));

        Assert.HasCount(3, rejection.Failures);

        Assert.AreSequenceEqual(
            ["quote_not_found", "branch_without_citation", "branch_without_citation"],
            rejection.Failures.Select(f => f.Reason).ToArray(),
            SequenceOrder.InAnyOrder);

        Assert.AreSequenceEqual(
            [
                "/nodes/0/children/0/references/0/quote",
                "/nodes/0/children/1/references",
                "/nodes/1/references",
            ],
            rejection.Failures.Select(f => f.Path).ToArray(),
            SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task A_near_miss_quote_comes_back_with_the_text_it_should_have_been()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, responses) = await this.ClosedSurveyAsync();

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                survey,
                Draft(Cites("CI is slow", (responses[0], "A full run is 22 minutes and it failed often."))),
                "agent",
                null,
                this.Cancellation));

        var failure = rejection.Failures.Single();

        // Nearest is exact response text, so it can be pasted straight back as the fix.
        Assert.AreEqual("A full run is 22 minutes and it fails often.", failure.Nearest);
        Assert.Contains("A full run is", rejection.Message, StringComparison.Ordinal);
    }

    private SummaryService Service()
    {
        return new SummaryService(mDb);
    }

    private static SummaryDraft Draft(NodeDraft child)
    {
        return new SummaryDraft()
        {
            Body = "Overview",
            Nodes = [Heading("Tooling", child)],
        };
    }

    /// <summary>A node reads as a heading only by having children; nothing on it says so.</summary>
    private static NodeDraft Heading(string text, params NodeDraft[] children)
    {
        return Node(text) with { Children = children };
    }

    private static NodeDraft Node(string text)
    {
        return new NodeDraft() { Text = text };
    }

    private static NodeDraft Cites(string text, params (Guid Response, string Quote)[] citations)
    {
        return Node(text) with
        {
            References =
            [
                .. citations.Select(c => new ReferenceDraft { ResponseId = c.Response, Quote = c.Quote }),
            ],
        };
    }

    private async Task<(Survey Survey, Guid[] Responses)> ClosedSurveyAsync()
    {
        var survey = NewSurvey(ResponseIdentity.Required);
        survey.IsAcceptingResponses = false;

        var anna = new Response()
        {
            Id = Guid.NewGuid(),
            Body = AnnaSaid,
            Author = "Anna",
            AuthTokenHash = "a",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var tom = new Response()
        {
            Id = Guid.NewGuid(),
            Body = TomSaid,
            Author = "Tom",
            AuthTokenHash = "t",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        survey.Responses.Add(anna);
        survey.Responses.Add(tom);

        mDb.Surveys.Add(survey);
        await mDb.SaveChangesAsync(this.Cancellation);

        return (survey, [anna.Id, tom.Id]);
    }
}
