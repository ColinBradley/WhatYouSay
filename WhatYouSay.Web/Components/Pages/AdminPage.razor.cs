using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Components.Shared;
using WhatYouSay.Web.Api;
using WhatYouSay.Web.Live;
using WhatYouSay.Web.Presentation;

namespace WhatYouSay.Web.Components.Pages;

public partial class AdminPage
{
    private Topic? mTopic;

    private bool mIsAdmin;

    private string? mError;

    private string? mNewToken;

    private string? mPrompt;

    private string? mShareLink;

    private string? mJoinLink;

    private string? mRunnerSetup;

    private bool mCanChangeIdentityAndTiming;

    private string? mFixedReason;

    private int mResponseCount;

    private int mFrozenCount;

    private int mSummaryCount;

    private Summary? mLiveSummary;

    private IReadOnlyList<Crumb> mCrumbs = [];

    [Inject]
    private TopicService Topics { get; set; } = default!;

    [Inject]
    private TopicAdminService Admin { get; set; } = default!;

    [Inject]
    private SummaryService Summaries { get; set; } = default!;

    [Inject]
    private TopicLiveUpdates Live { get; set; } = default!;

    [Inject]
    private AdminSession Session { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private PublicAddress Address { get; set; } = default!;

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

    [SupplyParameterFromForm(FormName = "admin-guidance")]
    public string? ExpectedFeedback { get; set; }

    [SupplyParameterFromForm(FormName = "admin-guidance")]
    public string? SummarisingInstructions { get; set; }

    [SupplyParameterFromForm(FormName = "admin-guidance")]
    public string? SummaryLengthTarget { get; set; }

    [SupplyParameterFromForm(FormName = "admin-freeze")]
    public bool FreezeResponses { get; set; }

    /// <summary>Which summary-updates button was pressed: pause, resume, review or live.</summary>
    [SupplyParameterFromForm(FormName = "admin-summary-updates")]
    public string? SummaryUpdates { get; set; }

    private bool IsLive =>
        mTopic?.SummaryPolicy == SummaryPolicy.Live;

    /// <summary>The site's address with no trailing slash, for the commands an admin copies.</summary>
    private string SiteUrl =>
        this.Address.Resolve(string.Empty, this.Navigation.BaseUri).AbsoluteUri.TrimEnd('/');

    private string? FreezeReason =>
        mResponseCount switch
        {
            0 => "Nobody has responded yet.",
            _ when mFrozenCount == mResponseCount => "Every response is already frozen.",
            _ => null,
        };

    private string? ThawReason =>
        mFrozenCount == 0 ? "No response is frozen." : null;

    private string? PauseReason =>
        (this.IsLive, mLiveSummary) switch
        {
            (false, _) => "With review on, the summariser only writes drafts, so there is nothing to pause.",
            (_, null) => "There is no live summary yet.",
            (_, { IsAgentEditable: false }) => "Summariser updates are already paused.",
            _ => null,
        };

    private string? ResumeReason =>
        (this.IsLive, mLiveSummary) switch
        {
            (false, _) => "With review on, the summariser only writes drafts, so there is nothing to resume.",
            (_, null) => "There is no live summary yet.",
            (_, { IsAgentEditable: true }) => "The summariser is already updating the live summary.",
            _ => null,
        };

    [SupplyParameterFromForm(FormName = "admin-settings")]
    public bool RecordsTiming { get; set; }

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
        mShareLink = this.Address.Resolve($"topics/{this.Code}", this.Navigation.BaseUri).AbsoluteUri;
        mJoinLink = this.Address.Resolve($"topics/{this.Code}/respond", this.Navigation.BaseUri).AbsoluteUri;
        mRunnerSetup = RunnerSetup.For(mTopic!, this.SiteUrl, this.Address.IsConfigured);
        mResponseCount = await this.Topics.CountResponsesAsync(mTopic!.Id);
        mFrozenCount = await this.Topics.CountFrozenResponsesAsync(mTopic.Id);
        mSummaryCount = (await this.Summaries.ListAllAsync(mTopic.Id)).Count;
        mLiveSummary = await this.Summaries.FindLiveAsync(mTopic.Id);

        mCanChangeIdentityAndTiming = await this.Admin.CanChangeIdentityAndTimingAsync(mTopic);
        mFixedReason = mCanChangeIdentityAndTiming
            ? null
            : "Somebody has already answered under these settings. Changing them now cannot "
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

    private async Task SetFrozenAsync()
    {
        await this.GuardedAsync(() => this.Admin.SetResponsesFrozenAsync(mTopic!, this.FreezeResponses));
    }

    private async Task SetSummaryUpdatesAsync()
    {
        await this.GuardedAsync(() => this.SummaryUpdates switch
        {
            "pause" or "resume" when mLiveSummary is { } live =>
                this.Admin.SetAgentEditableAsync(mTopic!, live.Id, this.SummaryUpdates == "resume"),
            "review" => this.Admin.SetSummaryPolicyAsync(mTopic!, SummaryPolicy.Reviewed),
            "live" => this.Admin.SetSummaryPolicyAsync(mTopic!, SummaryPolicy.Live),
            _ => Task.CompletedTask,
        });
    }

    private async Task SaveSettingsAsync()
    {
        // A disabled checkbox posts nothing, which would read as unticked. Once identity and
        // timing are fixed, pass the topic's own values so saving the other settings still works.
        var fixedNow = !mCanChangeIdentityAndTiming;

        await this.GuardedAsync(() => this.Admin.UpdateSettingsAsync(
            mTopic!,
            this.IsPubliclyListed,
            this.AreResponsesPublic,
            fixedNow ? mTopic!.IsAnonymous : this.IsAnonymous,
            fixedNow ? mTopic!.RecordsTiming : this.RecordsTiming));
    }

    private async Task SaveGuidanceAsync()
    {
        await this.GuardedAsync(() => this.Admin.UpdateGuidanceAsync(
            mTopic!,
            this.ExpectedFeedback,
            this.SummarisingInstructions,
            this.SummaryLengthTarget));
    }

    private async Task RegenerateTokenAsync()
    {
        if (mTopic is null || !await this.Session.CanAdministerAsync(mTopic.Id))
        {
            return;
        }

        mNewToken = await this.Admin.RegenerateSummariserTokenAsync(mTopic);

        mPrompt = SummariserPrompt.For(mTopic, this.SiteUrl, mNewToken);

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

        this.Live.Publish(mTopic.Id);
        this.Reload();
    }

    private void Reload()
    {
        this.Navigation.NavigateTo($"/topics/{this.Code}/admin");
    }
}
