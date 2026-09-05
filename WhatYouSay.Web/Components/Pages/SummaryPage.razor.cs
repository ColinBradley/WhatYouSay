using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
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
        CanReact = false,
        ReactReason = null,
        Tallies = new Dictionary<int, NodeReactionTally>(),
    };

    private string? mResponderToken;

    private IReadOnlyList<Crumb> mCrumbs = [];

    [Inject]
    private TopicService Topics { get; set; } = default!;

    [Inject]
    private SummaryService Summaries { get; set; } = default!;

    [Inject]
    private ReactionService Reactions { get; set; } = default!;

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
        mResponderToken = ResponderCookie.Read(this.HttpContext, mTopic.Id);

        // Reacting is what publishing turns on, so an unpublished version an admin is
        // previewing takes no reactions — they would attach to nodes the next draft replaces.
        var canReact = mSummary.IsVisibleToPublic
            && await this.Reactions.CanReactAsync(mTopic.Id, mResponderToken);

        mReading = new SummaryReading()
        {
            ResponsesArePublic = mTopic.AreResponsesPublic,
            CanReact = canReact,
            ReactReason = this.ReactReason(canReact),
            Tallies = await this.Reactions.TallyAsync(mSummary.Id, mResponderToken),
        };

        WhatYouSayTelemetry.SummaryViewed(mTopic);
    }

    private async Task ReactAsync()
    {
        if (mTopic is null
            || mSummary is null
            || !mReading.CanReact
            || mResponderToken is null
            || this.Action is null)
        {
            return;
        }

        var parts = this.Action.Split(':');

        if (parts.Length != 3
            || !int.TryParse(parts[0], out var nodeId)
            || !Enum.TryParse<ReactionKind>(parts[1], out var kind))
        {
            return;
        }

        switch (parts[2])
        {
            case "toggle":
                await this.Reactions.ToggleAsync(mTopic, nodeId, mResponderToken, kind);
                break;

            case "set":
                var note = this.HttpContext.Request.Form[SummaryReading.NoteField(nodeId)].ToString();
                await this.Reactions.SetObjectionAsync(mTopic, nodeId, mResponderToken, note);
                break;

            case "withdraw":
                await this.Reactions.WithdrawAsync(mTopic, nodeId, mResponderToken, kind);
                break;
        }

        this.Navigation.NavigateTo(
            this.HttpContext.Request.Path + this.HttpContext.Request.QueryString);
    }

    private string? ReactReason(bool canReact)
    {
        if (canReact)
        {
            return null;
        }

        return mSummary!.IsVisibleToPublic
            ? "Only people who answered this topic can react to it."
            : "This version has not been published yet.";
    }

    /// <summary>Versions are held newest first, but read oldest first.</summary>
    private int VersionNumber()
    {
        return mVersions.Count - mVersions.FindIndex(v => v.Id == mSummary!.Id);
    }
}
