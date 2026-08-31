using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class SummaryServiceTests : DatabaseTest
{
    [TestMethod]
    public async Task Only_published_summaries_are_visible()
    {
        using var activity = TestTelemetry.Source.Start();

        var survey = NewSurvey(ResponseIdentity.Required);

        survey.Summaries.Add(Summary("draft", isDraft: true, isPublic: true));
        survey.Summaries.Add(Summary("internal", isDraft: false, isPublic: false));

        mDb.Surveys.Add(survey);
        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.IsNull(await this.Service().FindLatestVisibleAsync(survey.Id, this.Cancellation));
        Assert.IsEmpty(await this.Service().ListVisibleAsync(survey.Id, this.Cancellation));
    }

    [TestMethod]
    public async Task The_newest_published_version_wins()
    {
        using var activity = TestTelemetry.Source.Start();

        var survey = NewSurvey(ResponseIdentity.Required);

        survey.Summaries.Add(Summary("older", isDraft: false, isPublic: true, daysAgo: 5));
        survey.Summaries.Add(Summary("newer", isDraft: false, isPublic: true, daysAgo: 1));

        mDb.Surveys.Add(survey);
        await mDb.SaveChangesAsync(this.Cancellation);

        var latest = await this.Service().FindLatestVisibleAsync(survey.Id, this.Cancellation);

        Assert.IsNotNull(latest);
        Assert.AreEqual("newer", latest.Body);
        Assert.HasCount(2, await this.Service().ListVisibleAsync(survey.Id, this.Cancellation));
    }

    [TestMethod]
    public async Task References_to_withdrawn_responses_disappear_but_the_point_survives()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, NullLogger.Instance, this.Cancellation);

        var survey = await mDb.Surveys.SingleAsync(s => s.Code == "spr47ab", this.Cancellation);
        var summary = (await this.Service().FindLatestVisibleAsync(survey.Id, this.Cancellation))!;

        var point = summary.Topics.SelectMany(t => t.Points).First(p => p.References.Count > 0);
        var citedResponseId = point.References[0].ResponseId;
        var pointId = point.Id;
        var before = point.References.Count;

        var cited = await mDb.Responses.SingleAsync(r => r.Id == citedResponseId, this.Cancellation);
        cited.IsDeleted = true;
        await mDb.SaveChangesAsync(this.Cancellation);

        mDb.ChangeTracker.Clear();

        var reloaded = (await this.Service().FindLatestVisibleAsync(survey.Id, this.Cancellation))!;
        var reloadedPoint = reloaded.Topics.SelectMany(t => t.Points).Single(p => p.Id == pointId);

        Assert.HasCount(before - 1, reloadedPoint.References);
        Assert.DoesNotContain(r => r.ResponseId == citedResponseId, reloadedPoint.References);
    }

    [TestMethod]
    public async Task Every_seeded_quote_actually_occurs_where_it_claims_to()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, NullLogger.Instance, this.Cancellation);

        var survey = await mDb.Surveys.SingleAsync(s => s.Code == "spr47ab", this.Cancellation);
        var summary = (await this.Service().FindLatestVisibleAsync(survey.Id, this.Cancellation))!;

        var references = summary.Topics.SelectMany(t => t.Points).SelectMany(p => p.References).ToList();

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
    public async Task Every_seeded_point_cites_at_least_one_response()
    {
        using var activity = TestTelemetry.Source.Start();

        await SeedData.EnsureSeededAsync(mDb, NullLogger.Instance, this.Cancellation);

        var survey = await mDb.Surveys.SingleAsync(s => s.Code == "spr47ab", this.Cancellation);
        var summary = (await this.Service().FindLatestVisibleAsync(survey.Id, this.Cancellation))!;

        var points = summary.Topics.SelectMany(t => t.Points).ToList();

        Assert.IsNotEmpty(points);
        Assert.IsTrue(points.All(p => p.References.Count > 0));
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
