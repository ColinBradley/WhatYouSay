using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>
/// The one interactive component in the app. A tree editor posting a form per keystroke-sized
/// change would reload the page under the person using it, which is the whole reason this
/// page departs from the static SSR everything else uses.
/// </summary>
public partial class SummaryEditor : IAsyncDisposable
{
    private Topic? mTopic;

    private Summary? mSummary;

    private IReadOnlyList<Response> mResponses = [];

    private IReadOnlyList<NodeCommentView> mComments = [];

    private IReadOnlySet<int> mUngrounded = new HashSet<int>();

    private string mBody = string.Empty;

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

    [Parameter]
    [EditorRequired]
    public Guid TopicId { get; set; }

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

    internal IReadOnlyList<Response> Responses =>
        mResponses;

    /// <summary>What already quotes this response, so the pane can mark those runs.</summary>
    private IEnumerable<SummaryNodeReference> ReferencesFor(Guid responseId)
    {
        return mSummary is null
            ? []
            : mSummary.Nodes
                .SelectMany(node => node.References)
                .Where(reference => reference.ResponseId == responseId);
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

    protected override async Task OnInitializedAsync()
    {
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
        mSelf?.Dispose();

        await ValueTask.CompletedTask;
    }

    /// <summary>
    /// Every operation runs in a scope of its own. A scoped DbContext would otherwise live as
    /// long as the circuit, accumulating tracked entities across an editing session and
    /// answering later reads from the first one.
    /// </summary>
    private async Task RunAsync(Func<IServiceProvider, Topic, Task> action)
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

        await this.LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using (var scope = this.Scopes.CreateAsyncScope())
        {
            var topic = await this.RequireTopicAsync(scope.ServiceProvider);
            var edits = scope.ServiceProvider.GetRequiredService<SummaryEditService>();

            mTopic = topic;
            mSummary = await edits.LoadAsync(topic, this.SummaryId);

            if (mSummary is not null)
            {
                mBody = mSummary.Body;

                mUngrounded = SummaryGrounding.Ungrounded(mSummary)
                    .Select(node => node.Id)
                    .ToHashSet();

                mResponses = await scope.ServiceProvider
                    .GetRequiredService<ResponseService>()
                    .ListAsync(topic);

                mComments = await scope.ServiceProvider
                    .GetRequiredService<CommentService>()
                    .ListAsync(topic, this.SummaryId, null, includeHidden: true);
            }
        }

        this.StateHasChanged();
    }

    private async Task<Topic> RequireTopicAsync(IServiceProvider services)
    {
        return await services.GetRequiredService<TopicService>().FindByCodeAsync(this.Code)
            ?? throw new InvalidOperationException($"Topic {this.Code} no longer exists.");
    }

    private Task SaveBodyAsync()
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
        var body = response.Body.ReplaceLineEndings(" ");
        var text = body.Length <= 60 ? body : body[..60] + "…";

        return response.Author is { } author ? $"{author}: {text}" : text;
    }
}
