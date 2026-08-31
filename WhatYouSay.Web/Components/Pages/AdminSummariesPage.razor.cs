using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Components.Shared;

namespace WhatYouSay.Web.Components.Pages;

public partial class AdminSummariesPage
{
    private Survey? mSurvey;

    private List<Summary> mSummaries = [];

    private IReadOnlyList<Crumb> mCrumbs = [];

    [Inject]
    private SurveyService Surveys { get; set; } = default!;

    [Inject]
    private SummaryService Summaries { get; set; } = default!;

    [Inject]
    private SurveyAdminService Admin { get; set; } = default!;

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
        mSurvey = await this.Surveys.FindByCodeAsync(this.Code);

        if (mSurvey is null)
        {
            mCrumbs = [Breadcrumb.Home(), new Crumb { Text = "Not found" }];

            return;
        }

        if (!await this.Session.CanAdministerAsync(mSurvey.Id))
        {
            this.Navigation.NavigateTo($"/surveys/{this.Code}/admin");

            return;
        }

        mCrumbs =
        [
            Breadcrumb.Home(),
            Breadcrumb.Survey(mSurvey.Code, mSurvey.Title),
            new Crumb { Text = "Admin", Href = $"/surveys/{mSurvey.Code}/admin" },
            new Crumb { Text = "Summaries" },
        ];

        await this.LoadAsync();
    }

    private async Task LoadAsync()
    {
        var versions = await this.Summaries.ListAllAsync(mSurvey!.Id);
        mSummaries = [];

        foreach (var version in versions)
        {
            mSummaries.Add(await this.Summaries.FindAsync(version.Id) ?? version);
        }
    }

    private async Task ActAsync()
    {
        if (mSurvey is null
            || this.Action is null
            || !await this.Session.CanAdministerAsync(mSurvey.Id))
        {
            return;
        }

        var parts = this.Action.Split(':');

        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var summaryId))
        {
            return;
        }

        switch (parts[1])
        {
            case "publish":
                await this.Admin.SetSummaryVisibilityAsync(mSurvey, summaryId, true, true);
                break;

            case "unpublish":
                await this.Admin.SetSummaryVisibilityAsync(mSurvey, summaryId, false, false);
                break;

            case "delete":
                await this.Admin.DeleteSummaryAsync(mSurvey, summaryId);
                break;
        }

        this.Navigation.NavigateTo($"/surveys/{this.Code}/admin/summaries");
    }
}
