using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Components.Shared;

namespace WhatYouSay.Web.Components.Pages;

public partial class AdminResponsesPage
{
    private Survey? mSurvey;

    private IReadOnlyList<Response> mResponses = [];

    private IReadOnlyList<Crumb> mCrumbs = [];

    [Inject]
    private SurveyService Surveys { get; set; } = default!;

    [Inject]
    private ResponseService Responses { get; set; } = default!;

    [Inject]
    private SurveyAdminService Admin { get; set; } = default!;

    [Inject]
    private AdminSession Session { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public string Code { get; set; } = string.Empty;

    [SupplyParameterFromForm]
    public Guid? ResponseId { get; set; }

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
            new Crumb { Text = "Responses" },
        ];

        mResponses = await this.Responses.ListAsync(mSurvey);
    }

    private async Task DeleteAsync()
    {
        if (mSurvey is null
            || this.ResponseId is not { } id
            || !await this.Session.CanAdministerAsync(mSurvey.Id))
        {
            return;
        }

        await this.Admin.DeleteResponseAsync(mSurvey, id);

        this.Navigation.NavigateTo($"/surveys/{this.Code}/admin/responses");
    }
}
