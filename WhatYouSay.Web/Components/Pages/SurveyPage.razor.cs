using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Components.Shared;

namespace WhatYouSay.Web.Components.Pages;

public partial class SurveyPage
{
    private Survey? mSurvey;

    private Response? mOwnResponse;

    private bool mHasVisibleSummary;

    private bool mFrozen;

    private string? mError;

    private IReadOnlyList<Crumb> mCrumbs = [];

    [Inject]
    private SurveyService Surveys { get; set; } = default!;

    [Inject]
    private ResponseService Responses { get; set; } = default!;

    [Inject]
    private SummaryService Summaries { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public string Code { get; set; } = string.Empty;

    [CascadingParameter]
    public HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    public string? Body { get; set; }

    [SupplyParameterFromForm]
    public string? Author { get; set; }

    [SupplyParameterFromForm]
    public string? Intent { get; set; }

    protected override async Task OnInitializedAsync()
    {
        mSurvey = await this.Surveys.FindByCodeAsync(this.Code);

        if (mSurvey is null)
        {
            mCrumbs = [Breadcrumb.Home(), new Crumb { Text = "Not found" }];

            return;
        }

        mCrumbs = [Breadcrumb.Home(), new Crumb { Text = mSurvey.Title }];
        mFrozen = !mSurvey.IsAcceptingResponses;
        mHasVisibleSummary = await this.Summaries.FindLatestVisibleAsync(mSurvey.Id) is not null;

        var token = ResponderCookie.Read(this.HttpContext, mSurvey.Id);

        if (token is not null)
        {
            mOwnResponse = await this.Responses.FindOwnAsync(mSurvey.Id, token);
        }

        // On a GET, prefill the editor with what is already stored.
        if (mOwnResponse is not null && this.Body is null)
        {
            this.Body = mOwnResponse.Body;
            this.Author = mOwnResponse.Author;
        }
    }

    private async Task SubmitAsync()
    {
        if (mSurvey is null || mFrozen)
        {
            return;
        }

        if (this.Intent == "withdraw")
        {
            await this.WithdrawAsync();

            return;
        }

        if (!this.Validate(mSurvey))
        {
            return;
        }

        if (mOwnResponse is null)
        {
            var token = await this.Responses.SubmitAsync(mSurvey, this.Body!, this.Author);

            // Static SSR, so the response has not started and a cookie can still be written.
            ResponderCookie.Write(this.HttpContext, mSurvey.Id, token);
        }
        else
        {
            await this.Responses.EditAsync(mSurvey, mOwnResponse, this.Body!, this.Author);
        }

        this.Reload();
    }

    private async Task WithdrawAsync()
    {
        if (mSurvey is null || mOwnResponse is null)
        {
            return;
        }

        await this.Responses.WithdrawAsync(mSurvey, mOwnResponse);
        ResponderCookie.Clear(this.HttpContext, mSurvey.Id);

        this.Reload();
    }

    private void Reload()
    {
        this.Navigation.NavigateTo($"/surveys/{this.Code}");
    }

    private bool Validate(Survey survey)
    {
        if (string.IsNullOrWhiteSpace(this.Body))
        {
            mError = "An answer is required.";

            return false;
        }

        if (survey.ResponseIdentity == ResponseIdentity.Required && string.IsNullOrWhiteSpace(this.Author))
        {
            mError = "This survey asks everyone to put their name to what they write.";

            return false;
        }

        mError = null;

        return true;
    }

    private string NameLabel()
    {
        return mSurvey!.ResponseIdentity == ResponseIdentity.Optional ? "Your name (optional)" : "Your name";
    }

    private string? FrozenReason()
    {
        return mFrozen
            ? "This survey has closed. Responses are frozen so the quotes in the summary stay accurate."
            : null;
    }

    private string StorageNotice()
    {
        return mSurvey!.ResponseIdentity switch
        {
            ResponseIdentity.Anonymous =>
                "Anonymous: no name and no timestamp is recorded, not even hidden. A cookie in "
                + "this browser is the only link back to your response.",
            ResponseIdentity.Optional =>
                "Your answer, the name you give if any, and the time are stored.",
            _ => "Your answer, your name and the time are stored.",
        };
    }
}
