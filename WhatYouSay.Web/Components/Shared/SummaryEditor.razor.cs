using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Live;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>
/// The summary editor. Interactive, because a tree editor posting a form per keystroke-sized
/// change would reload the page under the person using it. Reloads whenever anything on the
/// topic changes, keeping whatever is typed but not yet saved.
/// </summary>
public partial class SummaryEditor : IAsyncDisposable
{
    private Topic? mTopic;

    private Summary? mSummary;

    private IReadOnlyList<Response> mResponses = [];

    private IReadOnlyList<NodeCommentView> mComments = [];

    private HashSet<int> mUngrounded = [];

    private string mBody = string.Empty;

    /// <summary>The overview as the last load found it. Null before the first load.</summary>
    private string? mStoredBody;

    /// <summary>Whether someone else saved the overview while an edit to it was unsaved here.</summary>
    private bool mBodyChanged;

    /// <summary>What has been typed into each node's text box but not yet saved.</summary>
    private readonly Dictionary<int, string> mTextDrafts = [];

    /// <summary>Each node's text as the last load found it.</summary>
    private Dictionary<int, string> mStoredTexts = [];

    /// <summary>Whether the summary has gone, deleted here or elsewhere.</summary>
    private bool mMissing;

    private IDisposable? mSubscription;

    /// <summary>
    /// Counts loads started. A load that finishes after a later one started is dropped, because
    /// loads overlap whenever one awaits the database.
    /// </summary>
    private int mLoads;

    private string? mError;

    private string? mNotice;

    /// <summary>Handed to app.js so a selection in the response pane can reach this component.</summary>
    private DotNetObjectReference<SummaryEditor>? mSelf;

    /// <summary>The node whose delete button is armed, so a subtree cannot go in one click.</summary>
    private int? mConfirmingDelete;

    /// <summary>
    /// What the toolbar acts on. Set when a node's text box takes focus and never cleared on
    /// blur: reaching for a toolbar button blurs the box, and clearing there would take the
    /// selection away a moment before the command needed it.
    /// </summary>
    private int? mSelected;

    [Inject]
    private IServiceScopeFactory Scopes { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private TopicLiveUpdates Live { get; set; } = default!;

    [Inject]
    private CircuitReconnection Connection { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public Guid TopicId { get; set; }

    [Parameter]
    [EditorRequired]
    public string Code { get; set; } = string.Empty;

    [Parameter]
    [EditorRequired]
    public Guid SummaryId { get; set; }

    /// <summary>
    /// Published versions are read-only, except the live summary. Unpublishing is the way back in.
    /// </summary>
    internal bool IsLocked =>
        mSummary is not null && !mSummary.IsDraft && !mSummary.IsLive;

    internal string? LockedReason =>
        this.IsLocked ? "Published, so it is read-only. Unpublish it to make changes." : null;

    private string? UnpublishReason =>
        mSummary switch
        {
            { IsLive: true } => "The live summary stays published. Require review, or make another summary live, first.",
            { IsVisibleToPublic: false } => "Not published, so there is nothing to take back.",
            _ => null,
        };

    private string AgentAccessLabel =>
        (mSummary?.IsLive, mSummary?.IsAgentEditable) switch
        {
            (true, true) => "Pause summariser",
            (true, _) => "Resume summariser",
            (_, true) => "Close to summariser",
            _ => "Open to summariser",
        };

    /// <summary>
    /// Why the summariser access button is disabled, or null when it is enabled.
    /// Only opening a summary to the summariser can be refused.
    /// </summary>
    private string? AgentAccessReason =>
        mSummary is { IsAgentEditable: false, IsDraft: false, IsLive: false }
            ? "Published, so only the live summary can be open to the summariser."
            : null;

    internal IReadOnlyList<Response> Responses =>
        mResponses;

    /// <summary>
    /// References to this response's current wording, so the pane can mark the quoted runs.
    /// Quotes of an earlier wording appear only on their node.
    /// </summary>
    private IReadOnlyList<SummaryNodeReference> CurrentReferencesFor(Response response)
    {
        return mSummary is null
            ? []
            :
            [
                .. mSummary.Nodes
                    .SelectMany(node => node.References)
                    .Where(reference => reference.ResponseId == response.Id
                        && reference.RevisionNumber == response.RevisionNumber),
            ];
    }

    /// <summary>Whether this node ends a branch with nothing cited on it or above it.</summary>
    internal bool IsUngrounded(int nodeId) =>
        mUngrounded.Contains(nodeId);

    internal bool IsSelected(int nodeId) =>
        mSelected == nodeId;

    internal SummaryNode? Selected =>
        mSummary is null || mSelected is null
            ? null
            : mSummary.Nodes.FirstOrDefault(node => node.Id == mSelected);

    /// <summary>Every toolbar command needs a node, so they all read the same reason.</summary>
    internal string? SelectionReason =>
        this.LockedReason ?? (this.Selected is null ? "Pick a node first." : null);

    internal void Select(int nodeId)
    {
        if (mSelected == nodeId)
        {
            return;
        }

        mSelected = nodeId;

        // The node's own handler re-renders the node. The toolbar is a sibling and hears
        // nothing, so every command would stay disabled against a node that is plainly picked.
        this.StateHasChanged();
    }

    /// <summary>Whether the selected node can go this way, for the toolbar's disabled state.</summary>
    internal bool CanMove(NodeMove move)
    {
        if (this.Selected is not { } node || this.IsLocked)
        {
            return false;
        }

        var siblings = this.SiblingsOf(node);
        var first = siblings is [var head, ..] && head.Id == node.Id;

        return move switch
        {
            NodeMove.Up => !first,
            NodeMove.Down => siblings is not [.., var last] || last.Id != node.Id,
            NodeMove.Indent => !first,
            NodeMove.Outdent => node.ParentId is not null,
            _ => false,
        };
    }

    internal string? MoveReason(NodeMove move)
    {
        if (this.SelectionReason is { } reason)
        {
            return reason;
        }

        return this.CanMove(move)
            ? null
            : move switch
            {
                NodeMove.Up => "Already first in its group.",
                NodeMove.Down => "Already last in its group.",
                NodeMove.Indent => "Nothing above it to sit under.",
                NodeMove.Outdent => "Already at the top level.",
                _ => null,
            };
    }

    internal bool IsConfirmingDelete(int nodeId) =>
        mConfirmingDelete == nodeId;

    /// <summary>This node's sibling group, so a move with nowhere to go renders disabled.</summary>
    internal IReadOnlyList<SummaryNode> SiblingsOf(SummaryNode node)
    {
        return mSummary is null ? [] : SummaryTree.Siblings(mSummary, node.ParentId);
    }

    /// <summary>The node's text as its box shows it: an unsaved draft, or what is stored.</summary>
    internal string TextFor(SummaryNode node)
    {
        return mTextDrafts.GetValueOrDefault(node.Id, node.Text);
    }

    internal void SetTextDraft(int nodeId, string text)
    {
        mTextDrafts[nodeId] = text;
    }

    protected override async Task OnInitializedAsync()
    {
        // Before the first load, so a change landing mid-load is not missed.
        mSubscription = this.Live.Subscribe(this.TopicId, this.OnChangedAsync);
        this.Connection.Reconnected += this.OnReconnected;

        await this.LoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        mSelf = DotNetObjectReference.Create(this);

        await this.JS.InvokeVoidAsync("whatYouSayQuoting.attach", mSelf);
    }

    public async ValueTask DisposeAsync()
    {
        mSubscription?.Dispose();
        this.Connection.Reconnected -= this.OnReconnected;
        mSelf?.Dispose();
        GC.SuppressFinalize(this);

        await ValueTask.CompletedTask;
    }

    /// <summary>
    /// Every operation runs in a scope of its own. A scoped DbContext would otherwise live as
    /// long as the circuit, accumulating tracked entities across an editing session and
    /// answering later reads from the first one.
    /// </summary>
    /// <returns>False when the operation was refused. A refusal shows its reason and reloads.</returns>
    private async Task<bool> RunAsync(Func<IServiceProvider, Topic, Task> action)
    {
        mError = null;
        mNotice = null;

        try
        {
            await using var scope = this.Scopes.CreateAsyncScope();
            var topic = await this.RequireTopicAsync(scope.ServiceProvider);

            await action(scope.ServiceProvider, topic);
        }
        catch (SummaryGroundingException failure)
        {
            mError = failure.Message;
        }
        catch (InvalidOperationException failure)
        {
            mError = failure.Message;
        }

        if (mError is not null)
        {
            await this.LoadAsync();

            return false;
        }

        // This circuit is a subscriber too, so its own reload comes back through the publish.
        this.Live.Publish(this.TopicId);

        return true;
    }

    private Task OnChangedAsync()
    {
        return this.InvokeAsync(this.LoadAsync);
    }

    private void OnReconnected()
    {
        _ = this.InvokeAsync(this.LoadAsync);
    }

    private async Task LoadAsync()
    {
        var load = ++mLoads;

        await using (var scope = this.Scopes.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var topic = await this.RequireTopicAsync(services);
            var summary = await services.GetRequiredService<SummaryEditService>().LoadAsync(topic, this.SummaryId);

            IReadOnlyList<Response> responses = summary is null
                ? []
                : await services.GetRequiredService<ResponseService>().ListAsync(topic);

            IReadOnlyList<NodeCommentView> comments = summary is null
                ? []
                : await services.GetRequiredService<CommentService>()
                    .ListAsync(topic, this.SummaryId, null, includeHidden: true);

            if (load != mLoads)
            {
                return;
            }

            mTopic = topic;
            mSummary = summary;
            mMissing = summary is null;
            mResponses = responses;
            mComments = comments;

            if (summary is not null)
            {
                mUngrounded = SummaryGrounding.Ungrounded(summary)
                    .Select(node => node.Id)
                    .ToHashSet();

                this.TakeBody(summary.Body);
                this.TakeTexts(summary);
            }
        }

        this.StateHasChanged();
    }

    /// <summary>
    /// Shows a freshly loaded overview, unless an edit to it is unsaved here. An unsaved edit
    /// stays in the box, and is flagged when someone else saved the overview meanwhile.
    /// </summary>
    private void TakeBody(string stored)
    {
        var untouched = mStoredBody is null || mBody == mStoredBody;

        // The service trims what it saves, so a save from here comes back trimmed.
        var savedHere = stored != mStoredBody && mBody.Trim() == stored;

        if (untouched || savedHere)
        {
            mBody = stored;
            mBodyChanged = false;
        }
        else if (stored != mStoredBody)
        {
            mBodyChanged = true;
        }

        mStoredBody = stored;
    }

    /// <summary>
    /// Drops the text drafts that a load has settled: those for nodes that have gone, those
    /// matching what is stored, and those this editor has just saved.
    /// A draft of a node that someone else changed stays, so saving it replaces their text.
    /// </summary>
    private void TakeTexts(Summary summary)
    {
        var stored = summary.Nodes.ToDictionary(node => node.Id, node => node.Text);

        foreach (var (nodeId, draft) in mTextDrafts.ToList())
        {
            var exists = stored.TryGetValue(nodeId, out var text);

            // The service trims what it saves, so a save from here comes back trimmed.
            var savedHere = exists && text != mStoredTexts.GetValueOrDefault(nodeId) && draft.Trim() == text;

            if (!exists || draft == text || savedHere)
            {
                mTextDrafts.Remove(nodeId);
            }
        }

        if (mConfirmingDelete is { } armed && !stored.ContainsKey(armed))
        {
            mConfirmingDelete = null;
        }

        mStoredTexts = stored;
    }

    private async Task<Topic> RequireTopicAsync(IServiceProvider services)
    {
        return await services.GetRequiredService<TopicService>().FindByCodeAsync(this.Code)
            ?? throw new InvalidOperationException($"Topic {this.Code} no longer exists.");
    }

    private Task<bool> SaveBodyAsync()
    {
        return this.RunAsync((services, topic) =>
            services.GetRequiredService<SummaryEditService>()
                .SetBodyAsync(topic, this.SummaryId, mBody));
    }

    internal Task SetCommentHiddenAsync(int commentId, bool hidden)
    {
        return this.RunAsync((services, topic) =>
            services.GetRequiredService<CommentService>()
                .SetHiddenAsync(topic, commentId, null, hidden, asAdmin: true));
    }

    internal Task SetTextAsync(int nodeId, string text)
    {
        return this.RunAsync((services, topic) =>
            services.GetRequiredService<SummaryEditService>()
                .SetTextAsync(topic, this.SummaryId, nodeId, text));
    }

    internal Task AddNodeAsync(int? parentId)
    {
        return this.RunAsync((services, topic) =>
            services.GetRequiredService<SummaryEditService>()
                .AddNodeAsync(topic, this.SummaryId, parentId, "New node"));
    }

    internal Task MoveAsync(int nodeId, NodeMove move)
    {
        return this.RunAsync((services, topic) =>
            services.GetRequiredService<SummaryEditService>()
                .MoveAsync(topic, this.SummaryId, nodeId, move));
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

        await this.RunAsync((services, topic) =>
            services.GetRequiredService<SummaryEditService>()
                .DeleteNodeAsync(topic, this.SummaryId, nodeId));

        if (mSelected == nodeId)
        {
            mSelected = null;
        }
    }

    /// <summary>
    /// Turns a selection in the response pane into a quote on the picked node. The text comes
    /// straight off the rendered body, so it matches what is stored and the locator finds it;
    /// the service still checks, because nothing here is trusted to have.
    /// </summary>
    [JSInvokable]
    public async Task QuoteSelectionAsync(string responseId, string quote)
    {
        if (this.Selected is not { } node || !Guid.TryParse(responseId, out var response))
        {
            return;
        }

        await this.RunAsync((services, topic) =>
            services.GetRequiredService<SummaryEditService>()
                .AddReferenceAsync(topic, this.SummaryId, node.Id, response, quote));

        this.StateHasChanged();
    }

    internal Task DeleteReferenceAsync(int referenceId)
    {
        return this.RunAsync((services, topic) =>
            services.GetRequiredService<SummaryEditService>()
                .DeleteReferenceAsync(topic, this.SummaryId, referenceId));
    }

    private async Task SetPublishedAsync(bool published)
    {
        await this.RunAsync((services, topic) =>
            services.GetRequiredService<TopicAdminService>()
                .SetSummaryVisibilityAsync(topic, this.SummaryId, published));

        if (mError is null)
        {
            mNotice = published
                ? "Published. Everyone with the link can read it, and responders can react."
                : "Unpublished. It is a draft again, and editable.";

            this.StateHasChanged();
        }
    }

    private Task<bool> SetAgentEditableAsync(bool editable)
    {
        return this.RunAsync((services, topic) =>
            services.GetRequiredService<TopicAdminService>()
                .SetAgentEditableAsync(topic, this.SummaryId, editable));
    }

    private async Task DeleteSummaryAsync()
    {
        await this.RunAsync((services, topic) =>
            services.GetRequiredService<TopicAdminService>()
                .DeleteSummaryAsync(topic, this.SummaryId));

        if (mError is null)
        {
            this.Navigation.NavigateTo($"/topics/{this.Code}/admin/summaries");
        }
    }

    internal IReadOnlyList<NodeCommentView> CommentsFor(int nodeId)
    {
        return [.. mComments.Where(comment => comment.NodeId == nodeId)];
    }

    internal static string Label(Response response)
    {
        var body = response.Current.Body.ReplaceLineEndings(" ");
        var text = body.Length <= 60 ? body : body[..60] + "…";

        return response.Author is { } author ? $"{author}: {text}" : text;
    }
}
