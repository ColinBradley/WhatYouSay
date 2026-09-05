using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Auth;
using WhatYouSay.Services;
using WhatYouSay.Telemetry;
using WhatYouSay.Web.Auth;
using WhatYouSay.Web.Components.Shared;
using WhatYouSay.Web.Telemetry;

namespace WhatYouSay.Web.Components.Pages;

public partial class SummaryPage
{
    private Topic? mTopic;

    private Summary? mSummary;

    private List<Summary> mVersions = [];

    private IReadOnlyList<SummaryNode> mRoots = [];

    private bool mIsAdmin;

    /// <summary>Only ever non-zero for an admin, since only they list unpublished versions.</summary>
    private int mUnpublished;

    private SummaryReading mReading = new()
    {
        ResponsesArePublic = false,
        Tallies = new Dictionary<int, NodeReactionTally>(),
        Comments = [],
    };

    private string? mReactorToken;

    private IReadOnlyList<Crumb> mCrumbs = [];

    [Inject]
    private TopicService Topics { get; set; } = default!;

    [Inject]
    private SummaryService Summaries { get; set; } = default!;

    [Inject]
    private ReactionService Reactions { get; set; } = default!;

    [Inject]
    private CommentService Comments { get; set; } = default!;

    [Inject]
    private AdminSession Session { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public string Code { get; set; } = string.Empty;

    [Parameter]
    public Guid? SummaryId { get; set; }

    [CascadingParameter]
    public HttpContext HttpContext { get; set; } = default!;

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

        using var activity = WebTelemetry.Source.Start().SetTopic(mTopic);

        mCrumbs =
        [
            Breadcrumb.Home(),
            Breadcrumb.Topic(mTopic.Code, mTopic.Title),
            new Crumb { Text = "Summary" },
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

        mRoots = [.. mSummary.Roots];
        mReactorToken = ResponderCookie.Read(this.HttpContext, mTopic.Id);

        mReading = new SummaryReading()
        {
            ResponsesArePublic = mTopic.AreResponsesPublic,
            Tallies = await this.Reactions.TallyAsync(mSummary.Id, mReactorToken),
            Comments = await this.Comments.ListAsync(
                mTopic,
                mSummary.Id,
                mReactorToken,
                includeHidden: mIsAdmin),
        };

        WhatYouSayTelemetry.SummaryViewed(mTopic);
    }

    private async Task ReactAsync()
    {
        if (mTopic is null || mSummary is null || this.Action is null)
        {
            return;
        }

        // A viewer who has never responded still needs an identity to dedupe on, so the
        // first reaction or comment mints one. Static SSR is what makes this possible:
        // there is a response to write the cookie header to.
        if (mReactorToken is null)
        {
            mReactorToken = Secrets.NewToken();
            ResponderCookie.Write(this.HttpContext, mTopic.Id, mReactorToken);
        }

        var parts = this.Action.Split(':');

        if (parts is ["comment", var target, var verb] && int.TryParse(target, out var id))
        {
            await this.CommentAsync(id, verb);
        }
        else if (parts is [var node, var kindName]
            && int.TryParse(node, out var nodeId)
            && Enum.TryParse<ReactionKind>(kindName, out var kind))
        {
            await this.Reactions.ToggleAsync(mTopic, nodeId, mReactorToken, kind);
        }

        this.Navigation.NavigateTo(
            this.HttpContext.Request.Path + this.HttpContext.Request.QueryString);
    }

    private async Task CommentAsync(int id, string verb)
    {
        switch (verb)
        {
            case "add":
                var body = this.HttpContext.Request.Form[SummaryReading.CommentField(id)].ToString();

                if (!string.IsNullOrWhiteSpace(body))
                {
                    await this.Comments.AddAsync(mTopic!, id, mReactorToken!, body, this.AuthorName());
                }

                break;

            case "hide":
            case "show":
                await this.Comments.SetHiddenAsync(
                    mTopic!,
                    id,
                    mReactorToken,
                    hidden: verb == "hide",
                    asAdmin: mIsAdmin);

                break;
        }
    }

    /// <summary>The name on your own response, so a comment does not ask for it twice.</summary>
    private string? AuthorName()
    {
        return mReading.Comments.FirstOrDefault(c => c.IsMine)?.Author;
    }
    /// <summary>Versions are held newest first, but read oldest first.</summary>
    private int VersionNumber()
    {
        return mVersions.Count - mVersions.FindIndex(v => v.Id == mSummary!.Id);
    }
}
