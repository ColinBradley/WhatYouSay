using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Components.Shared;

namespace WhatYouSay.Web.Components.Pages;

public partial class AdminResponsesPage
{
    private Topic? mTopic;

    private IReadOnlyList<Response> mResponses = [];

    private IReadOnlyList<Crumb> mCrumbs = [];

    [Inject]
    private TopicService Topics { get; set; } = default!;

    [Inject]
    private ResponseService Responses { get; set; } = default!;

    [Inject]
    private TopicAdminService Admin { get; set; } = default!;

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
            new Crumb { Text = "Responses" },
        ];

        mResponses = await this.Responses.ListAsync(mTopic);
    }

    private async Task DeleteAsync()
    {
        if (mTopic is null
            || this.ResponseId is not { } id
            || !await this.Session.CanAdministerAsync(mTopic.Id))
        {
            return;
        }

        await this.Admin.DeleteResponseAsync(mTopic, id);

        this.Navigation.NavigateTo($"/topics/{this.Code}/admin/responses");
    }
}
