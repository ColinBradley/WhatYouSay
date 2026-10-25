using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>
/// What one viewer may see of a topic, as one reload read it. <see cref="TopicFeed"/> replaces
/// it whole on each reload, so a render never mixes two reloads.
/// </summary>
public sealed record TopicView
{
    public required Topic Topic { get; init; }

    /// <summary>The summary on show. Null when there is none that this viewer may read.</summary>
    public Summary? Summary { get; init; }

    /// <summary>The topic's summaries that this viewer may read, newest first.</summary>
    public IReadOnlyList<Summary> Versions { get; init; } = [];

    /// <summary>Every live response, in listing order. Empty unless the topic's responses are public.</summary>
    public IReadOnlyList<Response> Responses { get; init; } = [];

    /// <summary>
    /// The viewer's own live responses, in listing order, whether the topic's responses are
    /// public or not. Empty unless the feed was asked for them.
    /// </summary>
    public IReadOnlyList<Response> Own { get; init; } = [];

    /// <summary>
    /// The references in the summary on show, by response, so each response can find the ones
    /// citing it. Empty unless the topic's responses are public.
    /// </summary>
    public ILookup<Guid, SummaryNodeReference> References { get; init; } =
        Array.Empty<SummaryNodeReference>().ToLookup(r => r.ResponseId);

    /// <summary>The reactions on each response. A response nobody has reacted to has no entry.</summary>
    public IReadOnlyDictionary<Guid, ReactionTally> ResponseTallies { get; init; } =
        new Dictionary<Guid, ReactionTally>();

    /// <summary>The reactions on each node of the summary on show.</summary>
    public IReadOnlyDictionary<int, ReactionTally> NodeTallies { get; init; } =
        new Dictionary<int, ReactionTally>();

    /// <summary>The comments on the summary on show, by node.</summary>
    public ILookup<int, NodeCommentView> Comments { get; init; } =
        Array.Empty<NodeCommentView>().ToLookup(c => c.NodeId);

    public bool ResponsesArePublic =>
        this.Topic.AreResponsesPublic;

    public ReactionTally TallyFor(Response response)
    {
        return this.ResponseTallies.GetValueOrDefault(response.Id, ReactionTally.None);
    }

    public ReactionTally TallyFor(int nodeId)
    {
        return this.NodeTallies.GetValueOrDefault(nodeId, ReactionTally.None);
    }

    public IReadOnlyList<NodeCommentView> CommentsFor(int nodeId)
    {
        return [.. this.Comments[nodeId]];
    }
}
