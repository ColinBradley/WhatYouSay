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
            Draft(Point("CI is slow enough to change behaviour", (responses[0], "22 minutes"))),
            "agent",
            null,
            this.Cancellation);

        var reference = summary.Topics.Single().Points.Single().References.Single();

        Assert.AreEqual("22 minutes", AnnaSaid[reference.StartIndex..reference.EndIndex]);
        Assert.IsTrue(summary.IsDraft);
        Assert.AreEqual("agent", summary.CreatedBy);
    }

    [TestMethod]
    public async Task A_fabricated_quote_is_rejected()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, responses) = await this.ClosedSurveyAsync();

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                survey,
                Draft(Point("CI takes 45 minutes", (responses[0], "45 minutes"))),
                "agent",
                null,
                this.Cancellation));

        Assert.AreEqual("quote_not_found", rejection.Reason);

        Assert.IsTrue(rejection.Message.Contains("45 minutes", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task A_point_citing_nothing_is_rejected()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, _) = await this.ClosedSurveyAsync();

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                survey,
                Draft(new PointDraft { Description = "Morale is low", References = [] }),
                "agent",
                null,
                this.Cancellation));

        Assert.AreEqual("point_without_citation", rejection.Reason);
    }

    [TestMethod]
    public async Task Citing_a_response_from_another_survey_is_rejected()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, _) = await this.ClosedSurveyAsync();

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                survey,
                Draft(Point("Something", (Guid.NewGuid(), "22 minutes"))),
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
                Draft(Point("On-call hurt", (responses[1], "fourteen pages"))),
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
                Draft(Point("CI is slow", (responses[0], "22 minutes"))),
                "agent",
                null,
                this.Cancellation));

        Assert.AreEqual("survey_open", rejection.Reason);
    }

    [TestMethod]
    public async Task A_topic_with_no_points_is_rejected()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, _) = await this.ClosedSurveyAsync();

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                survey,
                new SummaryDraft()
                {
                    Body = "Overview",
                    Topics = [new TopicDraft { Name = "Tooling", Points = [] }],
                },
                "agent",
                null,
                this.Cancellation));

        Assert.AreEqual("empty_topic", rejection.Reason);
    }

    [TestMethod]
    public async Task A_rejected_draft_writes_nothing_at_all()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, responses) = await this.ClosedSurveyAsync();

        // The good topic must not survive the bad one.
        var draft = new SummaryDraft()
        {
            Body = "Overview",
            Topics =
            [
                new TopicDraft()
                {
                    Name = "Tooling",
                    Points = [Point("CI is slow", (responses[0], "22 minutes"))],
                },
                new TopicDraft()
                {
                    Name = "On-call",
                    Points = [Point("Pager noise", (responses[1], "invented text"))],
                },
            ],
        };

        await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(survey, draft, "agent", null, this.Cancellation));

        Assert.AreEqual(0, await mDb.Summaries.CountAsync(this.Cancellation));
        Assert.AreEqual(0, await mDb.SummaryTopics.CountAsync(this.Cancellation));
        Assert.AreEqual(0, await mDb.References.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task A_published_summary_cannot_be_rewritten_by_the_agent()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, responses) = await this.ClosedSurveyAsync();
        var service = this.Service();

        var draft = Draft(Point("CI is slow", (responses[0], "22 minutes")));
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
            Draft(Point("CI is slow", (responses[0], "22 minutes"))),
            "agent",
            null,
            this.Cancellation);

        var second = await service.SaveDraftAsync(
            survey,
            Draft(Point("On-call was noisy", (responses[1], "fourteen pages"))),
            "agent",
            first.Id,
            this.Cancellation);

        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(1, await mDb.Summaries.CountAsync(this.Cancellation));

        // The old topic tree is gone rather than merged with the new one.
        Assert.AreEqual(1, await mDb.SummaryTopics.CountAsync(this.Cancellation));
        Assert.AreEqual("On-call was noisy", (await mDb.SummaryTopicPoints.SingleAsync(this.Cancellation)).Description);
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
            Topics =
            [
                new TopicDraft()
                {
                    Name = "Tooling",
                    Points =
                    [
                        Point("Invented", (responses[0], "45 minutes")),
                        new PointDraft { Description = "Uncited", References = [] },
                    ],
                },
                new TopicDraft { Name = "Empty", Points = [] },
            ],
        };

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(survey, draft, "agent", null, this.Cancellation));

        Assert.AreEqual(3, rejection.Failures.Count);

        CollectionAssert.AreEquivalent(
            new[] { "quote_not_found", "point_without_citation", "empty_topic" },
            rejection.Failures.Select(f => f.Reason).ToArray());

        CollectionAssert.AreEquivalent(
            new[]
            {
                "/topics/0/points/0/references/0/quote",
                "/topics/0/points/1/references",
                "/topics/1/points",
            },
            rejection.Failures.Select(f => f.Path).ToArray());
    }

    [TestMethod]
    public async Task A_near_miss_quote_comes_back_with_the_text_it_should_have_been()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, responses) = await this.ClosedSurveyAsync();

        var rejection = await Assert.ThrowsExactlyAsync<SummaryGroundingException>(
            () => this.Service().SaveDraftAsync(
                survey,
                Draft(Point("CI is slow", (responses[0], "A full run is 22 minutes and it failed often."))),
                "agent",
                null,
                this.Cancellation));

        var failure = rejection.Failures.Single();

        // Nearest is exact response text, so it can be pasted straight back as the fix.
        Assert.AreEqual("A full run is 22 minutes and it fails often.", failure.Nearest);
        Assert.IsTrue(rejection.Message.Contains("A full run is", StringComparison.Ordinal));
    }

    private SummaryService Service()
    {
        return new SummaryService(mDb);
    }

    private static SummaryDraft Draft(PointDraft point)
    {
        return new SummaryDraft()
        {
            Body = "Overview",
            Topics = [new TopicDraft { Name = "Tooling", Points = [point] }],
        };
    }

    private static PointDraft Point(string description, params (Guid Response, string Quote)[] citations)
    {
        return new PointDraft()
        {
            Description = description,
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
