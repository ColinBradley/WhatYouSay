using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>
/// The one interactive component in the app. A tree editor posting a form per keystroke-sized
/// change would reload the page under the person using it, which is the whole reason this
/// page departs from the static SSR everything else uses.
/// </summary>
public partial class SummaryEditor
{
    private Survey? mSurvey;

    private Summary? mSummary;

    private IReadOnlyList<Response> mResponses = [];

    private IReadOnlyList<Objection> mObjections = [];

    private IReadOnlySet<int> mUngrounded = new HashSet<int>();

    private string mBody = string.Empty;

    private string? mError;

    private string? mNotice;

    /// <summary>The node whose delete button is armed, so a subtree cannot go in one click.</summary>
    private int? mConfirmingDelete;

    /// <summary>The node whose citation form is open, and what has been typed into it.</summary>
    private int? mCiting;

    private Guid mCiteResponseId;

    private string mCiteQuote = string.Empty;

    [Inject]
    private IServiceScopeFactory Scopes { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public Guid SurveyId { get; set; }

    [Parameter]
    [EditorRequired]
    public string Code { get; set; } = string.Empty;

    [Parameter]
    [EditorRequired]
    public Guid SummaryId { get; set; }

    /// <summary>Published versions are read-only; unpublish is the way back in.</summary>
    internal bool IsLocked =>
        mSummary is not null && !mSummary.IsDraft;

    internal string? LockedReason =>
        this.IsLocked ? "Published, so it is read-only. Unpublish it to make changes." : null;

    /// <summary>Live responses, for the citation picker.</summary>
    internal IReadOnlyList<Response> Responses =>
        mResponses;

    /// <summary>Which response the open citation form is pointed at.</summary>
    internal Guid CiteResponseId
    {
        get => mCiteResponseId;
        set => mCiteResponseId = value;
    }

    internal string CiteQuote
    {
        get => mCiteQuote;
        set => mCiteQuote = value;
    }

    /// <summary>Whether this node ends a branch with nothing cited on it or above it.</summary>
    internal bool IsUngrounded(int nodeId) =>
        mUngrounded.Contains(nodeId);

    internal bool IsCiting(int nodeId) =>
        mCiting == nodeId;

    internal bool IsConfirmingDelete(int nodeId) =>
        mConfirmingDelete == nodeId;

    /// <summary>This node's sibling group, so a move with nowhere to go renders disabled.</summary>
    internal IReadOnlyList<SummaryNode> SiblingsOf(SummaryNode node)
    {
        return mSummary is null ? [] : SummaryTree.Siblings(mSummary, node.ParentId);
    }

    protected override async Task OnInitializedAsync()
    {
        await this.LoadAsync();
    }

    /// <summary>
    /// Every operation runs in a scope of its own. A scoped DbContext would otherwise live as
    /// long as the circuit, accumulating tracked entities across an editing session and
    /// answering later reads from the first one.
    /// </summary>
    private async Task RunAsync(Func<IServiceProvider, Survey, Task> action)
    {
        mError = null;
        mNotice = null;

        try
        {
            await using var scope = this.Scopes.CreateAsyncScope();
            var survey = await this.RequireSurveyAsync(scope.ServiceProvider);

            await action(scope.ServiceProvider, survey);
        }
        catch (SummaryGroundingException failure)
        {
            mError = failure.Message;
        }
        catch (InvalidOperationException failure)
        {
            mError = failure.Message;
        }

        await this.LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using (var scope = this.Scopes.CreateAsyncScope())
        {
            var survey = await this.RequireSurveyAsync(scope.ServiceProvider);
            var edits = scope.ServiceProvider.GetRequiredService<SummaryEditService>();

            mSurvey = survey;
            mSummary = await edits.LoadAsync(survey, this.SummaryId);

            if (mSummary is not null)
            {
                mBody = mSummary.Body;

                mUngrounded = SummaryGrounding.Ungrounded(mSummary)
                    .Select(node => node.Id)
                    .ToHashSet();

                mResponses = await scope.ServiceProvider
                    .GetRequiredService<ResponseService>()
                    .ListAsync(survey);

                mObjections = await scope.ServiceProvider
                    .GetRequiredService<ReactionService>()
                    .ListObjectionsAsync(survey, this.SummaryId);

                if (mCiteResponseId == Guid.Empty && mResponses.Count > 0)
                {
                    mCiteResponseId = mResponses[0].Id;
                }
            }
        }

        this.StateHasChanged();
    }

    private async Task<Survey> RequireSurveyAsync(IServiceProvider services)
    {
        return await services.GetRequiredService<SurveyService>().FindByCodeAsync(this.Code)
            ?? throw new InvalidOperationException($"Survey {this.Code} no longer exists.");
    }

    private Task SaveBodyAsync()
    {
        return this.RunAsync((services, survey) =>
            services.GetRequiredService<SummaryEditService>()
                .SetBodyAsync(survey, this.SummaryId, mBody));
    }

    internal Task SetTextAsync(int nodeId, string text)
    {
        return this.RunAsync((services, survey) =>
            services.GetRequiredService<SummaryEditService>()
                .SetTextAsync(survey, this.SummaryId, nodeId, text));
    }

    internal Task AddNodeAsync(int? parentId)
    {
        return this.RunAsync((services, survey) =>
            services.GetRequiredService<SummaryEditService>()
                .AddNodeAsync(survey, this.SummaryId, parentId, "New node"));
    }

    internal Task MoveAsync(int nodeId, NodeMove move)
    {
        return this.RunAsync((services, survey) =>
            services.GetRequiredService<SummaryEditService>()
                .MoveAsync(survey, this.SummaryId, nodeId, move));
    }

    internal async Task DeleteNodeAsync(int nodeId)
    {
        if (mConfirmingDelete != nodeId)
        {
            mConfirmingDelete = nodeId;
            this.StateHasChanged();

            return;
        }

        mConfirmingDelete = null;

        await this.RunAsync((services, survey) =>
            services.GetRequiredService<SummaryEditService>()
                .DeleteNodeAsync(survey, this.SummaryId, nodeId));
    }

    internal async Task AddReferenceAsync(int nodeId)
    {
        var quote = mCiteQuote;

        await this.RunAsync((services, survey) =>
            services.GetRequiredService<SummaryEditService>()
                .AddReferenceAsync(survey, this.SummaryId, nodeId, mCiteResponseId, quote));

        if (mError is null)
        {
            mCiteQuote = string.Empty;
            mCiting = null;

            this.StateHasChanged();
        }
    }

    internal Task DeleteReferenceAsync(int referenceId)
    {
        return this.RunAsync((services, survey) =>
            services.GetRequiredService<SummaryEditService>()
                .DeleteReferenceAsync(survey, this.SummaryId, referenceId));
    }

    private async Task SetPublishedAsync(bool published)
    {
        await this.RunAsync((services, survey) =>
            services.GetRequiredService<SurveyAdminService>()
                .SetSummaryVisibilityAsync(survey, this.SummaryId, published));

        if (mError is null)
        {
            mNotice = published
                ? "Published. Everyone with the link can read it, and responders can react."
                : "Unpublished. It is a draft again, and editable.";

            this.StateHasChanged();
        }
    }

    private async Task DeleteSummaryAsync()
    {
        await this.RunAsync((services, survey) =>
            services.GetRequiredService<SurveyAdminService>()
                .DeleteSummaryAsync(survey, this.SummaryId));

        if (mError is null)
        {
            this.Navigation.NavigateTo($"/surveys/{this.Code}/admin/summaries");
        }
    }

    internal void StartCiting(int nodeId)
    {
        mCiting = mCiting == nodeId ? null : nodeId;
        mCiteQuote = string.Empty;

        this.StateHasChanged();
    }

    /// <summary>The response the citation form is currently pointed at, for copying out of.</summary>
    internal Response? CiteSource()
    {
        return mResponses.FirstOrDefault(response => response.Id == mCiteResponseId);
    }

    internal IReadOnlyList<Objection> ObjectionsFor(int nodeId)
    {
        return [.. mObjections.Where(objection => objection.NodeId == nodeId)];
    }

    internal static string Label(Response response)
    {
        var body = response.Body.ReplaceLineEndings(" ");
        var text = body.Length <= 60 ? body : body[..60] + "…";

        return response.Author is { } author ? $"{author}: {text}" : text;
    }
}
