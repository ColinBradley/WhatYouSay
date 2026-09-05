using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class SurveyAdminServiceTests : DatabaseTest
{
    [TestMethod]
    public async Task Creating_a_survey_returns_the_summariser_token_once_and_stores_only_its_hash()
    {
        using var activity = TestTelemetry.Source.Start();

        var created = await this.Service().CreateAsync(
            "Sprint 48 retro", "How did it go?", "hunter2",
            ResponseIdentity.Required, true, false, this.Cancellation);

        var stored = await mDb.Surveys.SingleAsync(this.Cancellation);

        Assert.AreEqual(Secrets.HashToken(created.SummariserToken), stored.SummariserTokenHash);
        Assert.AreNotEqual(created.SummariserToken, stored.SummariserTokenHash);
        Assert.IsTrue(stored.IsAcceptingResponses);
    }

    [TestMethod]
    public async Task The_admin_password_is_hashed_not_stored()
    {
        using var activity = TestTelemetry.Source.Start();

        var service = this.Service();
        var created = await service.CreateAsync(
            "Retro", "How did it go?", "hunter2",
            ResponseIdentity.Required, false, false, this.Cancellation);

        Assert.IsFalse(created.Survey.AdminPasswordHash.Contains("hunter2", StringComparison.Ordinal));
        Assert.IsTrue(service.CheckPassword(created.Survey, "hunter2"));
        Assert.IsFalse(service.CheckPassword(created.Survey, "Hunter2"));
    }

    [TestMethod]
    public async Task Survey_codes_are_unique()
    {
        using var activity = TestTelemetry.Source.Start();

        var service = this.Service();
        var codes = new HashSet<string>();

        for (var i = 0; i < 20; i++)
        {
            var created = await service.CreateAsync(
                $"Survey {i}", "Prompt", "pw",
                ResponseIdentity.Optional, false, false, this.Cancellation
            );

            Assert.IsTrue(codes.Add(created.Survey.Code));
        }
    }

    [TestMethod]
    public async Task Regenerating_the_summariser_token_invalidates_the_old_one()
    {
        using var activity = TestTelemetry.Source.Start();

        var service = this.Service();
        var created = await service.CreateAsync(
            "Retro", "Prompt", "pw", ResponseIdentity.Required, false, false, this.Cancellation);

        var replacement = await service.RegenerateSummariserTokenAsync(created.Survey, this.Cancellation);

        Assert.AreNotEqual(created.SummariserToken, replacement);
        Assert.AreEqual(Secrets.HashToken(replacement), created.Survey.SummariserTokenHash);
    }

    [TestMethod]
    public async Task A_survey_can_be_closed_and_reopened_until_a_summary_exists()
    {
        using var activity = TestTelemetry.Source.Start();

        var service = this.Service();
        var created = await service.CreateAsync(
            "Retro", "Prompt", "pw", ResponseIdentity.Required, false, false, this.Cancellation);

        var survey = created.Survey;

        await service.SetAcceptingResponsesAsync(survey, false, this.Cancellation);
        Assert.IsFalse(survey.IsAcceptingResponses);

        await service.SetAcceptingResponsesAsync(survey, true, this.Cancellation);
        Assert.IsTrue(survey.IsAcceptingResponses);

        await service.SetAcceptingResponsesAsync(survey, false, this.Cancellation);
        survey.Summaries.Add(new Summary { Body = "Overview" });
        await mDb.SaveChangesAsync(this.Cancellation);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.SetAcceptingResponsesAsync(survey, true, this.Cancellation)
        );
    }

    [TestMethod]
    public async Task Deleting_a_response_hides_it_without_removing_the_row()
    {
        using var activity = TestTelemetry.Source.Start();

        var service = this.Service();
        var created = await service.CreateAsync(
            "Retro", "Prompt", "pw", ResponseIdentity.Required, false, false, this.Cancellation
        );

        var response = new Response()
        {
            Id = Guid.NewGuid(),
            SurveyId = created.Survey.Id,
            Body = "Something",
            AuthTokenHash = "hash",
        };

        mDb.Responses.Add(response);
        await mDb.SaveChangesAsync(this.Cancellation);

        await service.DeleteResponseAsync(created.Survey, response.Id, this.Cancellation);

        Assert.AreEqual(1, await mDb.Responses.CountAsync(this.Cancellation));
        Assert.IsTrue((await mDb.Responses.SingleAsync(this.Cancellation)).IsDeleted);
    }

    [TestMethod]
    public async Task A_response_on_another_survey_cannot_be_deleted()
    {
        using var activity = TestTelemetry.Source.Start();

        var service = this.Service();
        var mine = await service.CreateAsync(
            "Mine", "Prompt", "pw", ResponseIdentity.Required, false, false, this.Cancellation
        );
        var theirs = await service.CreateAsync(
            "Theirs", "Prompt", "pw", ResponseIdentity.Required, false, false, this.Cancellation
        );

        var response = new Response()
        {
            Id = Guid.NewGuid(),
            SurveyId = theirs.Survey.Id,
            Body = "Something",
            AuthTokenHash = "hash",
        };

        mDb.Responses.Add(response);
        await mDb.SaveChangesAsync(this.Cancellation);

        await service.DeleteResponseAsync(mine.Survey, response.Id, this.Cancellation);

        Assert.IsFalse((await mDb.Responses.SingleAsync(this.Cancellation)).IsDeleted);
    }

    [TestMethod]
    public async Task Publishing_a_summary_makes_it_visible()
    {
        using var activity = TestTelemetry.Source.Start();

        var service = this.Service();
        var created = await service.CreateAsync(
            "Retro", "Prompt", "pw", ResponseIdentity.Required, false, false, this.Cancellation);

        var summary = new Summary { Body = "Overview" };
        created.Survey.Summaries.Add(summary);
        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.IsFalse(summary.IsVisibleToPublic);

        await service.SetSummaryVisibilityAsync(
            created.Survey, summary.Id, published: true, this.Cancellation
        );

        Assert.IsTrue(summary.IsVisibleToPublic);
    }

    private SurveyAdminService Service()
    {
        return new SurveyAdminService(mDb);
    }
}
