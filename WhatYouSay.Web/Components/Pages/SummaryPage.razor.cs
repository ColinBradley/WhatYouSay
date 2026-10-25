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
/// The static shell around <see cref="TopicReader"/>. Mints the responder token and writes its
/// cookie, which needs an <see cref="HttpContext"/> a circuit does not have.
/// </summary>
public partial class SummaryPage
{
    private Topic? mTopic;

    private bool mIsAdmin;

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

        // On arrival, not on a first reaction: by then the circuit has started and there is no
        // response left to set a cookie on. Responses take reactions before any summary exists,
        // so every visitor gets a token. Nothing is stored against it until this viewer acts.
        mReactorToken = ResponderCookie.Read(this.HttpContext, mTopic.Id) ?? Secrets.NewToken();
        ResponderCookie.Write(this.HttpContext, mTopic.Id, mReactorToken);

        if (await this.ShowsSummaryAsync(mTopic))
        {
            WhatYouSayTelemetry.SummaryViewed(mTopic);
        }
    }

    /// <summary>
    /// Whether the page opens on a summary, which counts as a summary view. The reader finds the
    /// summary again for itself, so this reads only the version list rather than a whole tree.
    /// </summary>
    private async Task<bool> ShowsSummaryAsync(Topic topic)
    {
        if (this.SummaryId is not { } id)
        {
            return (await this.Summaries.ListVisibleAsync(topic.Id)).Count > 0;
        }

        var versions = mIsAdmin
            ? await this.Summaries.ListAllAsync(topic.Id)
            : await this.Summaries.ListVisibleAsync(topic.Id);

        return versions.Any(v => v.Id == id);
    }
}
