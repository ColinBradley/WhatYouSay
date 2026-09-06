using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Live;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>
/// The published summary, with reactions and comments applied in place and pushed to every
/// other circuit reading the same version.
/// </summary>
public partial class SummaryReader : IDisposable
{
    private static readonly NodeReactionTally sNoReactions = new()
    {
        Counts = new Dictionary<ReactionKind, int>(),
        Mine = new HashSet<ReactionKind>(),
    };

    private Topic? mTopic;

    private Summary? mSummary;

    private IReadOnlyList<SummaryNode> mRoots = [];

    private IReadOnlyList<Response> mResponses = [];

    private ILookup<Guid, SummaryNodeReference> mReferences =
        Array.Empty<SummaryNodeReference>().ToLookup(r => r.ResponseId);

    private IReadOnlyDictionary<int, NodeReactionTally> mTallies =
        new Dictionary<int, NodeReactionTally>();

    private IReadOnlyList<NodeCommentView> mComments = [];

    /// <summary>What has been typed into each node's comment box but not yet sent.</summary>
    private readonly Dictionary<int, string> mDrafts = [];

    private IDisposable? mSubscription;

    private string? mError;

    [Inject]
    private IServiceScopeFactory Scopes { get; set; } = default!;

    [Inject]
    private SummaryLiveUpdates Live { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public string Code { get; set; } = string.Empty;

    [Parameter]
    [EditorRequired]
    public Guid SummaryId { get; set; }

    /// <summary>Minted by the shell: the cookie it lives in needs an <c>HttpContext</c>.</summary>
    [Parameter]
    [EditorRequired]
    public string ReactorToken { get; set; } = string.Empty;

    /// <summary>Admins see hidden comments, and may hide anyone's.</summary>
    [Parameter]
    public bool IsAdmin { get; set; }

    internal bool ResponsesArePublic =>
        mTopic?.AreResponsesPublic == true;

    internal IReadOnlyList<Response> Responses =>
        mResponses;

    internal ILookup<Guid, SummaryNodeReference> References =>
        mReferences;

    internal NodeReactionTally TallyFor(int nodeId)
    {
        return mTallies.TryGetValue(nodeId, out var tally) ? tally : sNoReactions;
    }

    internal IReadOnlyList<NodeCommentView> CommentsFor(int nodeId)
    {
        return [.. mComments.Where(comment => comment.NodeId == nodeId)];
    }

    internal string DraftFor(int nodeId)
    {
        return mDrafts.GetValueOrDefault(nodeId, string.Empty);
    }

    internal void SetDraft(int nodeId, string value)
    {
        mDrafts[nodeId] = value;
    }

    protected override async Task OnInitializedAsync()
    {
        // Before the first load, so a change landing mid-load is not missed.
        mSubscription = this.Live.Subscribe(this.SummaryId, this.OnChangedAsync);

        await this.LoadAsync();
    }

    public void Dispose()
    {
        mSubscription?.Dispose();
    }

    internal Task ToggleAsync(int nodeId, ReactionKind kind)
    {
        return this.RunAsync((services, topic) =>
            services.GetRequiredService<ReactionService>()
                .ToggleAsync(topic, nodeId, this.ReactorToken, kind));
    }

    internal async Task AddCommentAsync(int nodeId)
    {
        var body = this.DraftFor(nodeId);

        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        await this.RunAsync((services, topic) =>
            services.GetRequiredService<CommentService>()
                .AddAsync(topic, nodeId, this.ReactorToken, body, this.AuthorName()));

        if (mError is null)
        {
            mDrafts.Remove(nodeId);
        }
    }

    internal Task SetCommentHiddenAsync(int commentId, bool hidden)
    {
        return this.RunAsync((services, topic) =>
            services.GetRequiredService<CommentService>()
                .SetHiddenAsync(topic, commentId, this.ReactorToken, hidden, this.IsAdmin));
    }

    /// <summary>The name on your own comment, so a second one does not ask for it again.</summary>
    private string? AuthorName()
    {
        return mComments.FirstOrDefault(c => c.IsMine)?.Author;
    }

    /// <summary>
    /// Runs one write in a DI scope of its own. A scoped DbContext otherwise lives as long as
    /// the circuit and answers later reads from what it first tracked.
    /// </summary>
    private async Task RunAsync(Func<IServiceProvider, Topic, Task> action)
    {
        mError = null;

        try
        {
            await using var scope = this.Scopes.CreateAsyncScope();
            var topic = await this.RequireTopicAsync(scope.ServiceProvider);

            await action(scope.ServiceProvider, topic);
        }
        catch (InvalidOperationException failure)
        {
            mError = failure.Message;

            await this.LoadAsync();

            return;
        }

        // This circuit is a subscriber too, so its own reload comes back through the publish.
        await this.Live.PublishAsync(this.SummaryId);
    }

    private async Task OnChangedAsync()
    {
        await this.InvokeAsync(async () =>
        {
            await this.LoadAsync();

            // The tree reaches this component through the cascade, which the framework never
            // sees, so it does not know this rendered output is stale.
            this.StateHasChanged();
        });
    }

    private async Task LoadAsync()
    {
        await using var scope = this.Scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var topic = await this.RequireTopicAsync(services);

        mTopic = topic;
        mSummary = await services.GetRequiredService<SummaryService>().FindAsync(this.SummaryId);

        if (mSummary is null || mSummary.TopicId != topic.Id)
        {
            mRoots = [];

            return;
        }

        mRoots = [.. mSummary.Roots];

        mTallies = await services.GetRequiredService<ReactionService>()
            .TallyAsync(mSummary.Id, this.ReactorToken);

        mComments = await services.GetRequiredService<CommentService>()
            .ListAsync(topic, mSummary.Id, this.ReactorToken, includeHidden: this.IsAdmin);

        if (topic.AreResponsesPublic)
        {
            mResponses = await services.GetRequiredService<ResponseService>().ListAsync(topic);

            mReferences = mSummary.Nodes
                .SelectMany(node => node.References)
                .ToLookup(reference => reference.ResponseId);
        }
    }

    private async Task<Topic> RequireTopicAsync(IServiceProvider services)
    {
        return await services.GetRequiredService<TopicService>().FindByCodeAsync(this.Code)
            ?? throw new InvalidOperationException($"Topic {this.Code} no longer exists.");
    }
}
