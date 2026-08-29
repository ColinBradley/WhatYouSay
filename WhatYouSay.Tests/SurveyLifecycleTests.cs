using WhatYouSay.Data;

namespace WhatYouSay.Tests;

public class SurveyLifecycleTests : DatabaseTest
{
    [Fact]
    public async Task A_survey_cannot_reopen_once_a_summary_exists()
    {
        var survey = NewSurvey(ResponseIdentity.Required);
        survey.IsAcceptingResponses = false;

        mDb.Surveys.Add(survey);
        await mDb.SaveChangesAsync(Cancellation);

        Assert.True(survey.CanReopen);

        survey.Summaries.Add(new Summary { Body = "overview", CreatedBy = "agent" });
        await mDb.SaveChangesAsync(Cancellation);

        // Responses may only change while nothing references them.
        Assert.False(survey.CanReopen);
    }

    [Fact]
    public void An_open_survey_is_not_reopenable_because_it_was_never_closed()
    {
        var survey = NewSurvey(ResponseIdentity.Required);

        Assert.True(survey.IsAcceptingResponses);
        Assert.False(survey.CanReopen);
    }

    [Fact]
    public void A_summary_is_hidden_from_the_public_until_blessed_and_published()
    {
        var draft = new Summary { Body = "overview", IsDraft = true, IsPublic = true };
        var privateFinal = new Summary { Body = "overview", IsDraft = false, IsPublic = false };
        var published = new Summary { Body = "overview", IsDraft = false, IsPublic = true };

        Assert.False(draft.IsVisibleToPublic);
        Assert.False(privateFinal.IsVisibleToPublic);
        Assert.True(published.IsVisibleToPublic);
    }
}
