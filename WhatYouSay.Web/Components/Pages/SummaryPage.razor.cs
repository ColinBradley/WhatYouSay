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
    private static readonly PointReactionTally sNoReactions = new()
    {
        Agree = 0,
        Important = 0,
        Misrepresents = 0,
        Mine = new HashSet<ReactionKind>(),
    };

    private Survey? mSurvey;

    private Summary? mSummary;

    private List<Summary> mVersions = [];

    private IReadOnlyDictionary<int, PointReactionTally> mTallies =
        new Dictionary<int, PointReactionTally>();

    private bool mCanReact;

    private string? mReactReason;

    private string? mResponderToken;

    private IReadOnlyList<Crumb> mCrumbs = [];

    [Inject]
    private SurveyService Surveys { get; set; } = default!;

    [Inject]
    private SummaryService Summaries { get; set; } = default!;

    [Inject]
    private ReactionService Reactions { get; set; } = default!;

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

        mVersions = [.. await this.Summaries.ListVisibleAsync(mSurvey.Id)];

        mSummary = this.SummaryId is { } id
            ? await this.Summaries.FindAsync(id)
            : await this.Summaries.FindLatestVisibleAsync(mSurvey.Id);

        if (mSummary is not null
            && (!mSummary.IsVisibleToPublic || mSummary.SurveyId != mSurvey.Id))
        {
            mSummary = null;
        }

        if (mSummary is null)
        {
            return;
        }

        mResponderToken = ResponderCookie.Read(this.HttpContext, mSurvey.Id);
        mCanReact = await this.Reactions.CanReactAsync(mSurvey.Id, mResponderToken);
        mTallies = await this.Reactions.TallyAsync(mSummary.Id, mResponderToken);

        mReactReason = mCanReact
            ? null
            : "Only people who answered this survey can react to its points.";

        WhatYouSayTelemetry.SummaryViewed(mSurvey);
    }

    private async Task ReactAsync()
    {
        if (mSurvey is null || mSummary is null || mResponderToken is null || this.Action is null)
        {
            return;
        }

        var parts = this.Action.Split(':');

        if (parts.Length != 3
            || !int.TryParse(parts[0], out var pointId)
            || !Enum.TryParse<ReactionKind>(parts[1], out var kind))
        {
            return;
        }

        switch (parts[2])
        {
            case "toggle":
                await this.Reactions.ToggleAsync(mSurvey, pointId, mResponderToken, kind);
                break;

            case "set":
                var note = this.HttpContext.Request.Form[NoteField(pointId)].ToString();
                await this.Reactions.SetObjectionAsync(mSurvey, pointId, mResponderToken, note);
                break;

            case "withdraw":
                await this.Reactions.WithdrawAsync(mSurvey, pointId, mResponderToken, kind);
                break;
        }

        this.Navigation.NavigateTo(
            this.HttpContext.Request.Path + this.HttpContext.Request.QueryString);
    }

    /// <summary>Versions are held newest first, but read oldest first.</summary>
    private int VersionNumber()
    {
        return mVersions.Count - mVersions.FindIndex(v => v.Id == mSummary!.Id);
    }

    private static string NoteField(int pointId)
    {
        return $"Note_{pointId}";
    }

    private static string Pressed(PointReactionTally tally, ReactionKind kind)
    {
        return tally.Mine.Contains(kind) ? "btn-success" : "btn-outline-secondary";
    }

    private PointReactionTally TallyFor(int pointId)
    {
        return mTallies.TryGetValue(pointId, out var tally) ? tally : sNoReactions;
    }

    /// <summary>
    /// If the offsets no longer select the stored quote, show the quote alone rather than
    /// slicing the body blindly.
    /// </summary>
    private static (string Before, string Quote, string After)? Highlight(
        SummaryTopicPointResponseReference reference
    )
    {
        var body = reference.Response.Body;

        return QuoteLocator.Matches(body, reference.Quote, reference.StartIndex, reference.EndIndex)
            ? (body[..reference.StartIndex], reference.Quote, body[reference.EndIndex..])
            : null;
    }
}
