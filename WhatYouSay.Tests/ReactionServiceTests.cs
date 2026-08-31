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

        var (survey, node, token) = await this.SummarisedSurveyAsync();
        var service = this.Service();

        await service.ToggleAsync(survey, node, token, ReactionKind.Agree, this.Cancellation);
        Assert.AreEqual(1, await mDb.NodeReactions.CountAsync(this.Cancellation));

        await service.ToggleAsync(survey, node, token, ReactionKind.Agree, this.Cancellation);
        Assert.AreEqual(0, await mDb.NodeReactions.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task Someone_who_did_not_respond_cannot_react()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, node, _) = await this.SummarisedSurveyAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Service().ToggleAsync(
                survey, node, Secrets.NewToken(), ReactionKind.Agree, this.Cancellation));

        Assert.AreEqual(0, await mDb.NodeReactions.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task A_withdrawn_responder_loses_the_right_to_react()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, node, token) = await this.SummarisedSurveyAsync();

        var response = await mDb.Responses.SingleAsync(this.Cancellation);
        response.IsDeleted = true;
        await mDb.SaveChangesAsync(this.Cancellation);

        Assert.IsFalse(await this.Service().CanReactAsync(survey.Id, token, this.Cancellation));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Service().ToggleAsync(survey, node, token, ReactionKind.Agree, this.Cancellation));
    }

    [TestMethod]
    public async Task A_node_on_another_survey_cannot_be_reacted_to()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, _, token) = await this.SummarisedSurveyAsync();
        var (_, otherNode, _) = await this.SummarisedSurveyAsync();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Service().ToggleAsync(survey, otherNode, token, ReactionKind.Agree, this.Cancellation));
    }

    [TestMethod]
    public async Task An_objection_carries_a_note_and_can_be_reworded()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, node, token) = await this.SummarisedSurveyAsync();
        var service = this.Service();

        await service.SetObjectionAsync(survey, node, token, "That is not what I meant", this.Cancellation);
        await service.SetObjectionAsync(survey, node, token, "Closer, but still wrong", this.Cancellation);

        var stored = await mDb.NodeReactions.SingleAsync(this.Cancellation);

        // Rewording edits the objection rather than withdrawing and re-raising it.
        Assert.AreEqual(ReactionKind.Misrepresents, stored.Kind);
        Assert.AreEqual("Closer, but still wrong", stored.Note);
    }

    [TestMethod]
    public async Task An_objection_is_withdrawn_explicitly_rather_than_by_resubmitting()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, node, token) = await this.SummarisedSurveyAsync();
        var service = this.Service();

        await service.SetObjectionAsync(survey, node, token, "Wrong", this.Cancellation);
        await service.WithdrawAsync(survey, node, token, ReactionKind.Misrepresents, this.Cancellation);

        Assert.AreEqual(0, await mDb.NodeReactions.CountAsync(this.Cancellation));
    }

    [TestMethod]
    public async Task Anonymous_surveys_record_no_reaction_timestamps()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, node, token) = await this.SummarisedSurveyAsync(ResponseIdentity.Anonymous);

        await this.Service().ToggleAsync(survey, node, token, ReactionKind.Agree, this.Cancellation);

        var stored = await mDb.NodeReactions.SingleAsync(this.Cancellation);

        Assert.IsNull(stored.CreatedAt);
    }

    [TestMethod]
    public async Task The_tally_separates_the_group_from_the_viewer()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, node, mine) = await this.SummarisedSurveyAsync();
        var theirs = await this.AddResponderAsync(survey);
        var service = this.Service();

        await service.ToggleAsync(survey, node, mine, ReactionKind.Agree, this.Cancellation);
        await service.ToggleAsync(survey, node, theirs, ReactionKind.Agree, this.Cancellation);
        await service.ToggleAsync(survey, node, theirs, ReactionKind.Important, this.Cancellation);

        var summaryId = await mDb.Summaries.Select(s => s.Id).SingleAsync(this.Cancellation);
        var tally = (await service.TallyAsync(summaryId, mine, this.Cancellation))[node];

        Assert.AreEqual(2, tally.Agree);
        Assert.AreEqual(1, tally.Important);
        Assert.Contains(ReactionKind.Agree, tally.Mine);
        Assert.DoesNotContain(ReactionKind.Important, tally.Mine);
    }

    [TestMethod]
    public async Task A_viewer_who_did_not_respond_sees_counts_but_nothing_of_their_own()
    {
        using var activity = TestTelemetry.Source.Start();

        var (survey, node, token) = await this.SummarisedSurveyAsync();
        var service = this.Service();

        await service.ToggleAsync(survey, node, token, ReactionKind.Agree, this.Cancellation);

        var summaryId = await mDb.Summaries.Select(s => s.Id).SingleAsync(this.Cancellation);
        var tally = (await service.TallyAsync(summaryId, null, this.Cancellation))[node];

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

    private async Task<(Survey Survey, int NodeId, string Token)> SummarisedSurveyAsync(
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

        var heading = new SummaryNode { Text = "Tooling" };
        var leaf = new SummaryNode { Text = "CI is slow", Parent = heading };
        var summary = new Summary { Id = Guid.CreateVersion7(), Body = "Overview", IsDraft = false, IsPublic = true };

        summary.Nodes.Add(heading);
        summary.Nodes.Add(leaf);
        survey.Summaries.Add(summary);

        mDb.Surveys.Add(survey);
        await mDb.SaveChangesAsync(this.Cancellation);

        return (survey, leaf.Id, token);
    }
}
