using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Components.Shared;

namespace WhatYouSay.Web.Components.Pages;

public partial class AdminSummariesPage
{
    private Topic? mTopic;

    private List<Summary> mSummaries = [];

    private int mLiveResponses;

    private IReadOnlyList<Crumb> mCrumbs = [];

    private string? mError;

    [Inject]
    private TopicService Topics { get; set; } = default!;

    [Inject]
    private SummaryService Summaries { get; set; } = default!;

    [Inject]
    private TopicAdminService Admin { get; set; } = default!;

    [Inject]
    private AdminSession Session { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public string Code { get; set; } = string.Empty;

    [SupplyParameterFromForm]
    public string? Action { get; set; }

    protected override async Task OnInitializedAsync()
    {
        mTopic = await this.Topics.FindByCodeAsync(this.Code);

        if (mTopic is null)
        {
            mCrumbs = [Breadcrumb.Home(), new Crumb { Text = "Not found" }];

            return;
        }

        if (!await this.Session.CanAdministerAsync(mTopic.Id))
        {
            this.Navigation.NavigateTo($"/topics/{this.Code}/admin");

            return;
        }

        mCrumbs =
        [
            Breadcrumb.Home(),
            Breadcrumb.Topic(mTopic.Code, mTopic.Title),
            new Crumb { Text = "Admin", Href = $"/topics/{mTopic.Code}/admin" },
            new Crumb { Text = "Summaries" },
        ];

        await this.LoadAsync();
    }

    private async Task LoadAsync()
    {
        var versions = await this.Summaries.ListAllAsync(mTopic!.Id);

        mLiveResponses = await this.Topics.CountResponsesAsync(mTopic.Id);
        mSummaries = [];

        foreach (var version in versions)
        {
            mSummaries.Add(await this.Summaries.FindAsync(version.Id) ?? version);
        }
    }

    /// <summary>
    /// A hint, not a state: nothing stores that a version is outdated, and a person who
    /// worked the new responses in by hand will not clear it. Only the agent writes the
    /// count, because only the agent reads every response.
    /// </summary>
    private bool IsOutdated(Summary summary)
    {
        return summary.CreatedBy == "agent" && summary.ResponseCountAtWrite != mLiveResponses;
    }

    private async Task ActAsync()
    {
        if (mTopic is null
            || this.Action is null
            || !await this.Session.CanAdministerAsync(mTopic.Id))
        {
            return;
        }

        if (this.Action == "new")
        {
            var created = await this.Admin.CreateEmptySummaryAsync(mTopic);

            this.Navigation.NavigateTo($"/topics/{this.Code}/admin/summaries/{created.Id}");

            return;
        }

        var parts = this.Action.Split(':');

        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var summaryId))
        {
            return;
        }

        try
        {
            switch (parts[1])
            {
                case "publish":
                    await this.Admin.SetSummaryVisibilityAsync(mTopic, summaryId, true);
                    break;

                case "unpublish":
                    await this.Admin.SetSummaryVisibilityAsync(mTopic, summaryId, false);
                    break;

                case "delete":
                    await this.Admin.DeleteSummaryAsync(mTopic, summaryId);
                    break;
            }
        }
        catch (SummaryGroundingException failure)
        {
            mError = failure.Message;
            await this.LoadAsync();

            return;
        }

        this.Navigation.NavigateTo($"/topics/{this.Code}/admin/summaries");
    }
}
