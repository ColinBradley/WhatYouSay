using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Tests;

[TestClass]
public class ResponseServiceTests : DatabaseTest
{
    [TestMethod]
    public async Task Submitting_returns_a_token_that_is_only_stored_hashed()
    {
        using var activity = TestTelemetry.Source.Start();

        var survey = await this.OpenSurveyAsync(ResponseIdentity.Optional);

        var token = await this.Service().SubmitAsync(survey, "Thai please", "Anna", this.Cancellation);

        var stored = await mDb.Responses.SingleAsync(this.Cancellation);

        Assert.AreNotEqual(token, stored.AuthTokenHash);
        Assert.AreEqual(Secrets.HashToken(token), stored.AuthTokenHash);
    }

    [TestMethod]
    public async Task The_returned_token_finds_that_response_again()
    {
        using var activity = TestTelemetry.Source.Start();

        var survey = await this.OpenSurveyAsync(ResponseIdentity.Optional);
        var service = this.Service();

        var token = await service.SubmitAsync(survey, "Thai please", "Anna", this.Cancellation);

        var found = await service.FindOwnAsync(survey.Id, token, this.Cancellation);

        Assert.IsNotNull(found);
        Assert.AreEqual("Thai please", found.Body);
        Assert.IsNull(await service.FindOwnAsync(survey.Id, Secrets.NewToken(), this.Cancellation));
    }

    [TestMethod]
    public async Task Anonymous_surveys_discard_the_author_even_when_one_is_supplied()
    {
        using var activity = TestTelemetry.Source.Start();

        var survey = await this.OpenSurveyAsync(ResponseIdentity.Anonymous);

        await this.Service().SubmitAsync(survey, "Meetings, mostly", "Colin", this.Cancellation);

        var stored = await mDb.Responses.SingleAsync(this.Cancellation);

        // The form hides the field, but the service must not depend on the form for this.
        Assert.IsNull(stored.Author);
        Assert.IsNull(stored.CreatedAt);
    }

    [TestMethod]
    public async Task Named_surveys_record_the_author_and_the_time()
    {
        using var activity = TestTelemetry.Source.Start();

        var survey = await this.OpenSurveyAsync(ResponseIdentity.Required);

        await this.Service().SubmitAsync(survey, "CI is slow", "Anna", this.Cancellation);

        var stored = await mDb.Responses.SingleAsync(this.Cancellation);

        Assert.AreEqual("Anna", stored.Author);
        Assert.IsNotNull(stored.CreatedAt);
    }

    [TestMethod]
    public async Task A_closed_survey_refuses_new_responses()
    {
        using var activity = TestTelemetry.Source.Start();

        var survey = await this.OpenSurveyAsync(ResponseIdentity.Optional);
        survey.IsAcceptingResponses = false;
        await mDb.SaveChangesAsync(this.Cancellation);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => this.Service().SubmitAsync(survey, "too late", null, this.Cancellation));
    }

    [TestMethod]
    public async Task Closing_a_survey_freezes_existing_responses()
    {
        using var activity = TestTelemetry.Source.Start();

        var survey = await this.OpenSurveyAsync(ResponseIdentity.Optional);
        var service = this.Service();

        var token = await service.SubmitAsync(survey, "original", "Anna", this.Cancellation);
        var response = (await service.FindOwnAsync(survey.Id, token, this.Cancellation))!;

        survey.IsAcceptingResponses = false;
        await mDb.SaveChangesAsync(this.Cancellation);

        // This is what keeps summary quote offsets from rotting.
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.EditAsync(survey, response, "changed", "Anna", this.Cancellation));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.WithdrawAsync(survey, response, this.Cancellation));
    }

    [TestMethod]
    public async Task Editing_while_open_updates_the_body_and_stamps_it_edited()
    {
        using var activity = TestTelemetry.Source.Start();

        var survey = await this.OpenSurveyAsync(ResponseIdentity.Optional);
        var service = this.Service();

        var token = await service.SubmitAsync(survey, "original", "Anna", this.Cancellation);
        var response = (await service.FindOwnAsync(survey.Id, token, this.Cancellation))!;

        await service.EditAsync(survey, response, "  changed  ", "Anna", this.Cancellation);

        Assert.AreEqual("changed", response.Body);
        Assert.IsNotNull(response.UpdatedAt);
    }

    [TestMethod]
    public async Task Withdrawing_hides_the_response_without_deleting_the_row()
    {
        using var activity = TestTelemetry.Source.Start();

        var survey = await this.OpenSurveyAsync(ResponseIdentity.Optional);
        var service = this.Service();

        var token = await service.SubmitAsync(survey, "never mind", null, this.Cancellation);
        var response = (await service.FindOwnAsync(survey.Id, token, this.Cancellation))!;

        await service.WithdrawAsync(survey, response, this.Cancellation);

        Assert.AreEqual(1, await mDb.Responses.CountAsync(this.Cancellation));
        Assert.IsEmpty(await service.ListAsync(survey, this.Cancellation));
        Assert.IsNull(await service.FindOwnAsync(survey.Id, token, this.Cancellation));
    }

    [TestMethod]
    public async Task Response_ids_are_random_rather_than_time_ordered()
    {
        using var activity = TestTelemetry.Source.Start();

        var survey = await this.OpenSurveyAsync(ResponseIdentity.Anonymous);

        await this.Service().SubmitAsync(survey, "Meetings, mostly", null, this.Cancellation);

        var stored = await mDb.Responses.SingleAsync(this.Cancellation);

        // Guid.CreateVersion7 embeds a Unix timestamp. Using one here would leak both the
        // order people answered in and roughly when, straight past the decision not to
        // record CreatedAt at all.
        Assert.AreEqual(4, VersionOf(stored.Id));
    }

    [TestMethod]
    public async Task Anonymous_surveys_do_not_list_responses_in_submission_order()
    {
        using var activity = TestTelemetry.Source.Start();

        var survey = await this.OpenSurveyAsync(ResponseIdentity.Anonymous);
        var service = this.Service();

        const int Count = 25;

        for (var i = 0; i < Count; i++)
        {
            await service.SubmitAsync(survey, i.ToString(), null, this.Cancellation);
        }

        var listed = await service.ListAsync(survey, this.Cancellation);
        var submissionOrder = Enumerable.Range(0, Count).ToList();

        Assert.HasCount(Count, listed);
        Assert.AreNotSequenceEqual(submissionOrder, listed.Select(r => int.Parse(r.Body)).ToList());
    }

    /// <summary>The version nibble sits in the high half of byte 6, big-endian.</summary>
    private static int VersionOf(Guid id) => (id.ToByteArray(bigEndian: true)[6] >> 4) & 0x0F;

    private ResponseService Service() => new(mDb);

    private async Task<Survey> OpenSurveyAsync(ResponseIdentity identity)
    {
        var survey = NewSurvey(identity);

        mDb.Surveys.Add(survey);
        await mDb.SaveChangesAsync(this.Cancellation);

        return survey;
    }
}
