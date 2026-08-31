using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class ReactionServiceTests : DatabaseTest
{
    [TestMethod]
    public async Task Reacting_twice_takes_the_reaction_back()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, point, token) = await this.SummarisedSurveyAsync();
        var service = this.Service();

        await service.ToggleAsync(survey, point, token, ReactionKind.Agree, this.Cancellation);
        Assert.AreEqual(1, await mDb.PointReactions.CountAsync(this.Cancellation));

        await service.ToggleAsync(survey, point, token, ReactionKind.Agree, this.Cancellation);
        Assert.AreEqual(0, await mDb.PointReactions.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task Someone_who_did_not_respond_cannot_react()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, point, _) = await this.SummarisedSurveyAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Service().ToggleAsync(
                survey, point, Secrets.NewToken(), ReactionKind.Agree, this.Cancellation));

        Assert.AreEqual(0, await mDb.PointReactions.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task A_withdrawn_responder_loses_the_right_to_react()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, point, token) = await this.SummarisedSurveyAsync();

        var response = await mDb.Responses.SingleAsync(this.Cancellation);
        response.IsDeleted = true;
        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.IsFalse(await this.Service().CanReactAsync(survey.Id, token, this.Cancellation));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Service().ToggleAsync(survey, point, token, ReactionKind.Agree, this.Cancellation));
    }

    [TestMethod]
    public async Task A_point_on_another_survey_cannot_be_reacted_to()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, _, token) = await this.SummarisedSurveyAsync();
        var (_, otherPoint, _) = await this.SummarisedSurveyAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Service().ToggleAsync(survey, otherPoint, token, ReactionKind.Agree, this.Cancellation));
    }

    [TestMethod]
    public async Task An_objection_carries_a_note_and_can_be_reworded()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, point, token) = await this.SummarisedSurveyAsync();
        var service = this.Service();

        await service.SetObjectionAsync(survey, point, token, "That is not what I meant", this.Cancellation);
        await service.SetObjectionAsync(survey, point, token, "Closer, but still wrong", this.Cancellation);

        var stored = await mDb.PointReactions.SingleAsync(this.Cancellation);

        // Rewording edits the objection rather than withdrawing and re-raising it.
        Assert.AreEqual(ReactionKind.Misrepresents, stored.Kind);
        Assert.AreEqual("Closer, but still wrong", stored.Note);
    }

    [TestMethod]
    public async Task An_objection_is_withdrawn_explicitly_rather_than_by_resubmitting()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, point, token) = await this.SummarisedSurveyAsync();
        var service = this.Service();

        await service.SetObjectionAsync(survey, point, token, "Wrong", this.Cancellation);
        await service.WithdrawAsync(survey, point, token, ReactionKind.Misrepresents, this.Cancellation);

        Assert.AreEqual(0, await mDb.PointReactions.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task Anonymous_surveys_record_no_reaction_timestamps()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, point, token) = await this.SummarisedSurveyAsync(ResponseIdentity.Anonymous);

        await this.Service().ToggleAsync(survey, point, token, ReactionKind.Agree, this.Cancellation);

        var stored = await mDb.PointReactions.SingleAsync(this.Cancellation);

        Assert.IsNull(stored.CreatedAt);
    }

    [TestMethod]
    public async Task The_tally_separates_the_group_from_the_viewer()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, point, mine) = await this.SummarisedSurveyAsync();
        var theirs = await this.AddResponderAsync(survey);
        var service = this.Service();

        await service.ToggleAsync(survey, point, mine, ReactionKind.Agree, this.Cancellation);
        await service.ToggleAsync(survey, point, theirs, ReactionKind.Agree, this.Cancellation);
        await service.ToggleAsync(survey, point, theirs, ReactionKind.Important, this.Cancellation);

        var summaryId = await mDb.Summaries.Select(s => s.Id).SingleAsync(this.Cancellation);
        var tally = (await service.TallyAsync(summaryId, mine, this.Cancellation))[point];

        Assert.AreEqual(2, tally.Agree);
        Assert.AreEqual(1, tally.Important);
        Assert.IsTrue(tally.Mine.Contains(ReactionKind.Agree));
        Assert.IsFalse(tally.Mine.Contains(ReactionKind.Important));
    }

    [TestMethod]
    public async Task A_viewer_who_did_not_respond_sees_counts_but_nothing_of_their_own()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, point, token) = await this.SummarisedSurveyAsync();
        var service = this.Service();

        await service.ToggleAsync(survey, point, token, ReactionKind.Agree, this.Cancellation);

        var summaryId = await mDb.Summaries.Select(s => s.Id).SingleAsync(this.Cancellation);
        var tally = (await service.TallyAsync(summaryId, null, this.Cancellation))[point];

        Assert.AreEqual(1, tally.Agree);
        Assert.IsEmpty(tally.Mine);
        Assert.IsNull(tally.MyNote);
    }

    private ReactionService Service()
    {
        return new ReactionService(mDb);
    }

    private async Task<string> AddResponderAsync(Survey survey)
    {
        var token = Secrets.NewToken();

        mDb.Responses.Add(new Response()
        {
            Id = Guid.NewGuid(),
            SurveyId = survey.Id,
            Body = "Another answer",
            AuthTokenHash = Secrets.HashToken(token),
        });

        await mDb.SaveChangesAsync(this.Cancellation);

        return token;
    }

    private async Task<(Survey Survey, int PointId, string Token)> SummarisedSurveyAsync(
        ResponseIdentity identity = ResponseIdentity.Required
    )
    {
        var survey = NewSurvey(identity);
        survey.IsAcceptingResponses = false;

        var token = Secrets.NewToken();

        survey.Responses.Add(new Response()
        {
            Id = Guid.NewGuid(),
            Body = "CI is slow",
            Author = identity == ResponseIdentity.Anonymous ? null : "Anna",
            AuthTokenHash = Secrets.HashToken(token),
        });

        var point = new SummaryTopicPoint { Description = "CI is slow" };
        var topic = new SummaryTopic { Name = "Tooling" };
        var summary = new Summary { Id = Guid.CreateVersion7(), Body = "Overview", IsDraft = false, IsPublic = true };

        topic.Points.Add(point);
        summary.Topics.Add(topic);
        survey.Summaries.Add(summary);

        mDb.Surveys.Add(survey);
        await mDb.SaveChangesAsync(this.Cancellation);

        return (survey, point.Id, token);
    }
}
