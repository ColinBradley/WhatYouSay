using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Components.Shared;

namespace WhatYouSay.Web.Components.Pages;

/// <summary>Lists the nodes the summariser has archived from one summary, with the feedback each gathered.</summary>
public partial class AdminArchivePage
{
    private Topic? mTopic;

    private IReadOnlyList<SummaryNode> mNodes = [];

    private IReadOnlyList<Crumb> mCrumbs = [];

    [Inject]
    private TopicService Topics { get; set; } = default!;

    [Inject]
    private SummaryService Summaries { get; set; } = default!;

    [Inject]
    private AdminSession Session { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public string Code { get; set; } = string.Empty;

    [Parameter]
    public Guid Id { get; set; }

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
            new Crumb { Text = "Summaries", Href = $"/topics/{mTopic.Code}/admin/summaries" },
            new Crumb { Text = "Edit", Href = $"/topics/{mTopic.Code}/admin/summaries/{this.Id}" },
            new Crumb { Text = "Archived" },
        ];

        mNodes = await this.Summaries.ListArchivedAsync(mTopic, this.Id);
    }

    private static IEnumerable<(ReactionKind Kind, int Count)> ReactionCounts(SummaryNode node)
    {
        return node.Reactions
            .GroupBy(reaction => reaction.Kind)
            .OrderBy(group => group.Key)
            .Select(group => (group.Key, group.Count()));
    }
}
