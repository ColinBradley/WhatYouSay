using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Web.Live;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>
/// Loads what one viewer may see of a topic, and loads it again whenever anything on the topic
/// changes. Hands the result to its content as a <see cref="TopicView"/>, and itself to the
/// panes inside for their reactions and comments. Holds no layout, so pages lay the panes out
/// as they need.
/// </summary>
public partial class TopicFeed : IDisposable
{
    private TopicView? mView;

    /// <summary>What has been typed into each node's comment box but not yet sent.</summary>
    private readonly Dictionary<int, string> mDrafts = [];

    private IDisposable? mSubscription;

    private string? mError;

    /// <summary>
    /// Counts loads started. A load that finishes after a later one started is dropped, because
    /// loads overlap whenever one awaits the database.
    /// </summary>
    private int mLoads;

    [Inject]
    private IServiceScopeFactory Scopes { get; set; } = default!;

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

    /// <summary>
    /// A fixed version, named by the version's own URL. Null follows whichever summary the
    /// topic's URL shows, so the first one published appears without a refresh.
    /// </summary>
    [Parameter]
    public Guid? SummaryId { get; set; }

    /// <summary>Whether to load a summary at all. Off where no summary is shown.</summary>
    [Parameter]
    public bool IncludesSummary { get; set; } = true;

    /// <summary>Whether to load the viewer's own responses into <see cref="TopicView.Own"/>.</summary>
    [Parameter]
    public bool IncludesOwn { get; set; }

    /// <summary>Minted by the shell, because the cookie it lives in needs an <c>HttpContext</c>.</summary>
    [Parameter]
    [EditorRequired]
    public string ReactorToken { get; set; } = string.Empty;

    /// <summary>Admins see unpublished versions and hidden comments, and may hide anyone's comment.</summary>
    [Parameter]
    public bool IsAdmin { get; set; }

    [Parameter]
    [EditorRequired]
    public RenderFragment<TopicView> ChildContent { get; set; } = default!;

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
        mSubscription = this.Live.Subscribe(this.TopicId, this.OnChangedAsync);
        this.Connection.Reconnected += this.OnReconnected;

        await this.LoadAsync();
    }

    public void Dispose()
    {
        mSubscription?.Dispose();
        this.Connection.Reconnected -= this.OnReconnected;
        GC.SuppressFinalize(this);
    }

    internal Task ToggleOnNodeAsync(int nodeId, ReactionKind kind)
    {
        return this.RunAsync((services, topic) =>
            services.GetRequiredService<ReactionService>()
                .ToggleAsync(topic, nodeId, this.ReactorToken, kind));
    }

    internal Task ToggleOnResponseAsync(Guid responseId, ReactionKind kind)
    {
        return this.RunAsync((services, topic) =>
            services.GetRequiredService<ReactionService>()
                .ToggleOnResponseAsync(topic, responseId, this.ReactorToken, kind));
    }

    internal async Task AddCommentAsync(int nodeId)
    {
        var body = this.DraftFor(nodeId);

        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        if (await this.RunAsync((services, topic) =>
                services.GetRequiredService<CommentService>()
                    .AddAsync(topic, nodeId, this.ReactorToken, body, this.AuthorName())))
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
        return mView?.Comments.SelectMany(group => group).FirstOrDefault(c => c.IsMine)?.Author;
    }

    /// <summary>
    /// Runs one write in a DI scope of its own, then publishes it. Returns false when the write
    /// was refused. A refusal shows its reason and reloads.
    /// </summary>
    private async Task<bool> RunAsync(Func<IServiceProvider, Topic, Task> action)
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

            await this.ReloadAsync();

            return false;
        }

        // This circuit is a subscriber too, so its own reload comes back through the publish.
        this.Live.Publish(this.TopicId);

        return true;
    }

    private Task OnChangedAsync()
    {
        return this.InvokeAsync(this.ReloadAsync);
    }

    private void OnReconnected()
    {
        _ = this.InvokeAsync(this.ReloadAsync);
    }

    private async Task ReloadAsync()
    {
        await this.LoadAsync();

        // A reload runs from a notification or from a pane's event. Neither renders this
        // component, so it renders itself.
        this.StateHasChanged();
    }

    private async Task LoadAsync()
    {
        var load = ++mLoads;
        var view = await this.ReadAsync();

        if (load == mLoads)
        {
            mView = view;
        }
    }

    /// <summary>
    /// Reads the topic afresh and checks visibility every time. Content that stops being public
    /// therefore disappears on the next reload.
    /// </summary>
    private async Task<TopicView> ReadAsync()
    {
        await using var scope = this.Scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var topic = await this.RequireTopicAsync(services);
        var responses = services.GetRequiredService<ResponseService>();
        var reactions = services.GetRequiredService<ReactionService>();

        var view = new TopicView()
        {
            Topic = topic,
        };

        if (topic.AreResponsesPublic)
        {
            view = view with
            {
                Responses = await responses.ListAsync(topic),
            };
        }

        if (this.IncludesOwn)
        {
            view = view with
            {
                Own = await responses.ListOwnAsync(topic, this.ReactorToken),
            };
        }

        if (view.Responses.Count > 0 || view.Own.Count > 0)
        {
            view = view with
            {
                ResponseTallies = await reactions.TallyResponsesAsync(topic, this.ReactorToken),
            };
        }

        return this.IncludesSummary
            ? await this.WithSummaryAsync(view, services)
            : view;
    }

    private async Task<TopicView> WithSummaryAsync(TopicView view, IServiceProvider services)
    {
        var topic = view.Topic;
        var summaries = services.GetRequiredService<SummaryService>();

        var versions = this.IsAdmin
            ? await summaries.ListAllAsync(topic.Id)
            : await summaries.ListVisibleAsync(topic.Id);

        // The topic's URL is the public one, so it shows what the public sees, admin or not.
        // A draft is reached by its own id.
        var summary = this.SummaryId is { } id
            ? await summaries.FindAsync(id)
            : await summaries.FindLatestVisibleAsync(topic.Id);

        if (summary is null
            || summary.TopicId != topic.Id
            || !(summary.IsVisibleToPublic || this.IsAdmin))
        {
            return view with
            {
                Versions = versions,
            };
        }

        var comments = await services.GetRequiredService<CommentService>()
            .ListAsync(topic, summary.Id, this.ReactorToken, includeHidden: this.IsAdmin);

        return view with
        {
            Summary = summary,
            Versions = versions,
            NodeTallies = await services.GetRequiredService<ReactionService>()
                .TallyAsync(summary.Id, this.ReactorToken),
            Comments = comments.ToLookup(comment => comment.NodeId),
            References = topic.AreResponsesPublic
                ? summary.Nodes
                    .SelectMany(node => node.References)
                    .ToLookup(reference => reference.ResponseId)
                : view.References,
        };
    }

    private async Task<Topic> RequireTopicAsync(IServiceProvider services)
    {
        return await services.GetRequiredService<TopicService>().FindByCodeAsync(this.Code)
            ?? throw new InvalidOperationException($"Topic {this.Code} no longer exists.");
    }
}
