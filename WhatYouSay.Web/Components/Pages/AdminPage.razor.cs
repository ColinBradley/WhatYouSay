using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Components.Shared;
using WhatYouSay.Web.Api;

namespace WhatYouSay.Web.Components.Pages;

public partial class AdminPage
{
    private Topic? mTopic;

    private bool mIsAdmin;

    private string? mError;

    private string? mNewToken;

    private string? mPrompt;

    private string? mShareLink;

    private bool mCanChangeIdentity;

    private string? mIdentityReason;

    private int mResponseCount;

    private int mSummaryCount;

    private IReadOnlyList<Crumb> mCrumbs = [];

    [Inject]
    private TopicService Topics { get; set; } = default!;

    [Inject]
    private TopicAdminService Admin { get; set; } = default!;

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

    [SupplyParameterFromForm(FormName = "admin-settings")]
    public bool IsAnonymous { get; set; }

    protected override async Task OnInitializedAsync()
    {
        mTopic = await this.Topics.FindByCodeAsync(this.Code);

        if (mTopic is null)
        {
            mCrumbs = [Breadcrumb.Home(), new Crumb { Text = "Not found" }];

            return;
        }

        mCrumbs =
        [
            Breadcrumb.Home(),
            Breadcrumb.Topic(mTopic.Code, mTopic.Title),
            new Crumb { Text = "Admin" },
        ];

        mIsAdmin = await this.Session.CanAdministerAsync(mTopic.Id);

        if (!mIsAdmin)
        {
            return;
        }

        await this.LoadDashboardAsync();
    }

    private async Task LoadDashboardAsync()
    {
        mShareLink = this.Navigation.ToAbsoluteUri($"/topics/{this.Code}").ToString();
        mResponseCount = await this.Topics.CountResponsesAsync(mTopic!.Id);
        mSummaryCount = (await this.Summaries.ListAllAsync(mTopic.Id)).Count;

        mCanChangeIdentity = mResponseCount == 0;
        mIdentityReason = mCanChangeIdentity
            ? null
            : "Somebody has already answered under this setting. Changing it now cannot "
                + "unrecord what was collected, and would change the deal they answered under.";
    }

    private async Task SignInAsync()
    {
        if (mTopic is null)
        {
            return;
        }

        if (string.IsNullOrEmpty(this.Password) || !this.Admin.CheckPassword(mTopic, this.Password))
        {
            mError = "That password does not match.";

            return;
        }

        this.Session.Grant(mTopic.Id);
        this.Reload();
    }

    private void SignOut()
    {
        if (mTopic is null)
        {
            return;
        }

        this.Session.Revoke(mTopic.Id);
        this.Navigation.NavigateTo($"/topics/{this.Code}");
    }

    private async Task CloseAsync()
    {
        await this.GuardedAsync(() => this.Admin.SetAcceptingResponsesAsync(mTopic!, false));
    }

    private async Task ReopenAsync()
    {
        await this.GuardedAsync(() => this.Admin.SetAcceptingResponsesAsync(mTopic!, true));
    }

    private async Task SaveSettingsAsync()
    {
        await this.GuardedAsync(() => this.Admin.UpdateSettingsAsync(
            mTopic!, this.IsPubliclyListed, this.AreResponsesPublic, this.IsAnonymous));
    }

    private async Task RegenerateTokenAsync()
    {
        if (mTopic is null || !await this.Session.CanAdministerAsync(mTopic.Id))
        {
            return;
        }

        mNewToken = await this.Admin.RegenerateSummariserTokenAsync(mTopic);

        mPrompt = SummariserPrompt.For(
            mTopic, this.Navigation.BaseUri.TrimEnd('/'), mNewToken);

        // Stays on the page rather than redirecting, because the token is shown once.
        await this.LoadDashboardAsync();
    }

    private async Task GuardedAsync(Func<Task> action)
    {
        if (mTopic is null || !await this.Session.CanAdministerAsync(mTopic.Id))
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
        this.Navigation.NavigateTo($"/topics/{this.Code}/admin");
    }
}
