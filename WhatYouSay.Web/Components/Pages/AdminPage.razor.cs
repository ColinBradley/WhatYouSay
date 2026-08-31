using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Components.Shared;
using WhatYouSay.Web.Api;

namespace WhatYouSay.Web.Components.Pages;

public partial class AdminPage
{
    private Survey? mSurvey;

    private bool mIsAdmin;

    private string? mError;

    private string? mNewToken;

    private string? mPrompt;

    private string? mShareLink;

    private string? mReopenReason;

    private int mResponseCount;

    private int mSummaryCount;

    private IReadOnlyList<Crumb> mCrumbs = [];

    [Inject]
    private SurveyService Surveys { get; set; } = default!;

    [Inject]
    private SurveyAdminService Admin { get; set; } = default!;

    [Inject]
    private SummaryService Summaries { get; set; } = default!;

    [Inject]
    private AdminSession Session { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public string Code { get; set; } = string.Empty;

    [SupplyParameterFromForm(FormName = "admin-signin")]
    public string? Password { get; set; }

    [SupplyParameterFromForm(FormName = "admin-settings")]
    public bool IsPubliclyListed { get; set; }

    [SupplyParameterFromForm(FormName = "admin-settings")]
    public bool AreResponsesPublic { get; set; }

    protected override async Task OnInitializedAsync()
    {
        mSurvey = await this.Surveys.FindByCodeAsync(this.Code);

        if (mSurvey is null)
        {
            mCrumbs = [Breadcrumb.Home(), new Crumb { Text = "Not found" }];

            return;
        }

        mCrumbs =
        [
            Breadcrumb.Home(),
            Breadcrumb.Survey(mSurvey.Code, mSurvey.Title),
            new Crumb { Text = "Admin" },
        ];

        mIsAdmin = await this.Session.CanAdministerAsync(mSurvey.Id);

        if (!mIsAdmin)
        {
            return;
        }

        await this.LoadDashboardAsync();
    }

    private async Task LoadDashboardAsync()
    {
        mShareLink = this.Navigation.ToAbsoluteUri($"/surveys/{this.Code}").ToString();
        mResponseCount = await this.Surveys.CountResponsesAsync(mSurvey!.Id);
        mSummaryCount = (await this.Summaries.ListAllAsync(mSurvey.Id)).Count;

        mReopenReason = mSurvey.CanReopen
            ? null
            : "A summary has been generated, so this survey stays closed. Run a new survey instead.";
    }

    private async Task SignInAsync()
    {
        if (mSurvey is null)
        {
            return;
        }

        if (string.IsNullOrEmpty(this.Password) || !this.Admin.CheckPassword(mSurvey, this.Password))
        {
            mError = "That password does not match.";

            return;
        }

        this.Session.Grant(mSurvey.Id);
        this.Reload();
    }

    private void SignOut()
    {
        if (mSurvey is null)
        {
            return;
        }

        this.Session.Revoke(mSurvey.Id);
        this.Navigation.NavigateTo($"/surveys/{this.Code}");
    }

    private async Task CloseAsync()
    {
        await this.GuardedAsync(() => this.Admin.SetAcceptingResponsesAsync(mSurvey!, false));
    }

    private async Task ReopenAsync()
    {
        await this.GuardedAsync(() => this.Admin.SetAcceptingResponsesAsync(mSurvey!, true));
    }

    private async Task SaveSettingsAsync()
    {
        await this.GuardedAsync(() => this.Admin.UpdateSettingsAsync(
            mSurvey!, this.IsPubliclyListed, this.AreResponsesPublic));
    }

    private async Task RegenerateTokenAsync()
    {
        if (mSurvey is null || !await this.Session.CanAdministerAsync(mSurvey.Id))
        {
            return;
        }

        mNewToken = await this.Admin.RegenerateSummariserTokenAsync(mSurvey);

        mPrompt = SummariserPrompt.For(
            mSurvey, this.Navigation.BaseUri.TrimEnd('/'), mNewToken);

        // Stays on the page rather than redirecting, because the token is shown once.
        await this.LoadDashboardAsync();
    }

    private async Task GuardedAsync(Func<Task> action)
    {
        if (mSurvey is null || !await this.Session.CanAdministerAsync(mSurvey.Id))
        {
            return;
        }

        try
        {
            await action();
        }
        catch (InvalidOperationException refused)
        {
            mError = refused.Message;

            return;
        }

        this.Reload();
    }

    private void Reload()
    {
        this.Navigation.NavigateTo($"/surveys/{this.Code}/admin");
    }
}
