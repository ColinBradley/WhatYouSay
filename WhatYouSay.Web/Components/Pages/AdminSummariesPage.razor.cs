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
        mSummaries = [];

        foreach (var version in versions)
        {
            mSummaries.Add(await this.Summaries.FindAsync(version.Id) ?? version);
        }
    }

    private async Task ActAsync()
    {
        if (mTopic is null
            || this.Action is null
            || !await this.Session.CanAdministerAsync(mTopic.Id))
        {
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
            // Publishing an ungrounded tree is refused here as well as in the editor, and
            // the editor is where the offending nodes are marked.
            mError = $"{failure.Message} Open it to see which nodes.";
            await this.LoadAsync();

            return;
        }

        this.Navigation.NavigateTo($"/topics/{this.Code}/admin/summaries");
    }
}
