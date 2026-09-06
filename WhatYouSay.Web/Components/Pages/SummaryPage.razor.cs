using Microsoft.AspNetCore.Components;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Telemetry;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Components.Shared;
using WhatYouSay.Web.Telemetry;

namespace WhatYouSay.Web.Components.Pages;

/// <summary>
/// The static shell around <see cref="SummaryReader"/>. Mints the responder token and writes its
/// cookie, which needs an <see cref="HttpContext"/> a circuit does not have.
/// </summary>
public partial class SummaryPage
{
    private Topic? mTopic;

    private Summary? mSummary;

    private List<Summary> mVersions = [];

    private bool mIsAdmin;

    /// <summary>Only ever non-zero for an admin, since only they list unpublished versions.</summary>
    private int mUnpublished;

    private string mReactorToken = string.Empty;

    private IReadOnlyList<Crumb> mCrumbs = [];

    [Inject]
    private TopicService Topics { get; set; } = default!;

    [Inject]
    private SummaryService Summaries { get; set; } = default!;

    [Inject]
    private AdminSession Session { get; set; } = default!;

    [Parameter]
    public string Code { get; set; } = string.Empty;

    [Parameter]
    public Guid? SummaryId { get; set; }

    [CascadingParameter]
    public HttpContext HttpContext { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        mTopic = await this.Topics.FindByCodeAsync(this.Code);

        if (mTopic is null)
        {
            mCrumbs = [Breadcrumb.Home(), new Crumb { Text = "Not found" }];

            return;
        }

        using var activity = WebTelemetry.Source.Start().SetTopic(mTopic);

        mCrumbs = this.SummaryId is null
            ? [Breadcrumb.Home(), new Crumb { Text = mTopic.Title }]
            :
            [
                Breadcrumb.Home(),
                Breadcrumb.Topic(mTopic.Code, mTopic.Title),
                new Crumb { Text = "Version" },
            ];

        mIsAdmin = await this.Session.CanAdministerAsync(mTopic.Id);

        mVersions =
        [
            .. mIsAdmin
                ? await this.Summaries.ListAllAsync(mTopic.Id)
                : await this.Summaries.ListVisibleAsync(mTopic.Id),
        ];

        mUnpublished = mVersions.Count(v => !v.IsVisibleToPublic);

        // The bare URL is the public one, so it resolves to the newest published version for
        // everyone including an admin — what the group sees is what an admin checking the
        // link should see. A draft is reached by its own id, which is what the admin summary
        // list links to.
        mSummary = this.SummaryId is { } id
            ? await this.Summaries.FindAsync(id)
            : await this.Summaries.FindLatestVisibleAsync(mTopic.Id);

        if (mSummary is not null
            && (mSummary.TopicId != mTopic.Id || !(mSummary.IsVisibleToPublic || mIsAdmin)))
        {
            mSummary = null;
        }

        if (mSummary is null)
        {
            return;
        }

        // On arrival, not on a first reaction: by then the circuit has started and there is no
        // response left to set a cookie on. Nothing is stored against it until this viewer acts.
        mReactorToken = ResponderCookie.Read(this.HttpContext, mTopic.Id) ?? Secrets.NewToken();
        ResponderCookie.Write(this.HttpContext, mTopic.Id, mReactorToken);

        WhatYouSayTelemetry.SummaryViewed(mTopic);
    }

    /// <summary>Versions are held newest first, but read oldest first.</summary>
    private int VersionNumber()
    {
        return mVersions.Count - mVersions.FindIndex(v => v.Id == mSummary!.Id);
    }
}
