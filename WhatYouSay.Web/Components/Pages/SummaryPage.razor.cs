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
    private Survey? mSurvey;

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
    private SurveyService Surveys { get; set; } = default!;

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
        mSurvey = await this.Surveys.FindByCodeAsync(this.Code);

        if (mSurvey is null)
        {
            mCrumbs = [Breadcrumb.Home(), new Crumb { Text = "Not found" }];

            return;
        }

        using var activity = WebTelemetry.Source.Start().SetSurvey(mSurvey);

        mCrumbs =
        [
            Breadcrumb.Home(),
            Breadcrumb.Survey(mSurvey.Code, mSurvey.Title),
            new Crumb { Text = "Summary" },
        ];

        mIsAdmin = await this.Session.CanAdministerAsync(mSurvey.Id);

        mVersions =
        [
            .. mIsAdmin
                ? await this.Summaries.ListAllAsync(mSurvey.Id)
                : await this.Summaries.ListVisibleAsync(mSurvey.Id),
        ];

        mUnpublished = mVersions.Count(v => !v.IsVisibleToPublic);

        // The bare URL is the public one, so it resolves to the newest published version for
        // everyone including an admin — what the group sees is what an admin checking the
        // link should see. A draft is reached by its own id, which is what the admin summary
        // list links to.
        mSummary = this.SummaryId is { } id
            ? await this.Summaries.FindAsync(id)
            : await this.Summaries.FindLatestVisibleAsync(mSurvey.Id);

        if (mSummary is not null
            && (mSummary.SurveyId != mSurvey.Id || !(mSummary.IsVisibleToPublic || mIsAdmin)))
        {
            mSummary = null;
        }

        if (mSummary is null)
        {
            return;
        }

        mRoots = [.. mSummary.Roots];
        mResponderToken = ResponderCookie.Read(this.HttpContext, mSurvey.Id);

        // Reacting is what publishing turns on, so an unpublished version an admin is
        // previewing takes no reactions — they would attach to nodes the next draft replaces.
        var canReact = mSummary.IsVisibleToPublic
            && await this.Reactions.CanReactAsync(mSurvey.Id, mResponderToken);

        mReading = new SummaryReading()
        {
            ResponsesArePublic = mSurvey.AreResponsesPublic,
            CanReact = canReact,
            ReactReason = this.ReactReason(canReact),
            Tallies = await this.Reactions.TallyAsync(mSummary.Id, mResponderToken),
        };

        WhatYouSayTelemetry.SummaryViewed(mSurvey);
    }

    private async Task ReactAsync()
    {
        if (mSurvey is null
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
                await this.Reactions.ToggleAsync(mSurvey, nodeId, mResponderToken, kind);
                break;

            case "set":
                var note = this.HttpContext.Request.Form[SummaryReading.NoteField(nodeId)].ToString();
                await this.Reactions.SetObjectionAsync(mSurvey, nodeId, mResponderToken, note);
                break;

            case "withdraw":
                await this.Reactions.WithdrawAsync(mSurvey, nodeId, mResponderToken, kind);
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
            ? "Only people who answered this survey can react to it."
            : "This version has not been published yet.";
    }

    /// <summary>Versions are held newest first, but read oldest first.</summary>
    private int VersionNumber()
    {
        return mVersions.Count - mVersions.FindIndex(v => v.Id == mSummary!.Id);
    }
}
