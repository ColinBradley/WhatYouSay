using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>An earlier wording of a response, with the references that quote it.</summary>
public sealed record EarlierRevision
{
    public required ResponseRevision Revision { get; init; }

    public required IReadOnlyList<SummaryNodeReference> References { get; init; }
}

/// <summary>A click on one reaction button of one response.</summary>
public sealed record ResponseReactionClick
{
    public required Guid ResponseId { get; init; }

    public required ReactionKind Kind { get; init; }
}

public partial class ResponsePane
{
    [Parameter]
    [EditorRequired]
    public IReadOnlyList<Response> Responses { get; set; } = [];

    /// <summary>Every reference in the summary, so each response can find the ones citing it.</summary>
    [Parameter]
    public ILookup<Guid, SummaryNodeReference> References { get; set; } =
        Array.Empty<SummaryNodeReference>().ToLookup(r => r.ResponseId);

    /// <summary>The reactions on each response. A response nobody has reacted to needs no entry.</summary>
    [Parameter]
    [EditorRequired]
    public IReadOnlyDictionary<Guid, ReactionTally> Tallies { get; set; } =
        new Dictionary<Guid, ReactionTally>();

    [Parameter]
    [EditorRequired]
    public EventCallback<ResponseReactionClick> OnReact { get; set; }

    private ReactionTally TallyFor(Response response)
    {
        return this.Tallies.GetValueOrDefault(response.Id, ReactionTally.None);
    }

    private Task ReactAsync(Response response, ReactionKind kind)
    {
        return this.OnReact.InvokeAsync(new ResponseReactionClick()
        {
            ResponseId = response.Id,
            Kind = kind,
        });
    }

    private IReadOnlyList<SummaryNodeReference> CurrentReferencesFor(Response response)
    {
        return [.. this.References[response.Id].Where(r => r.RevisionNumber == response.RevisionNumber)];
    }

    /// <summary>
    /// Earlier wordings of the response that the summary still quotes.
    /// Each is shown as it was, because its offsets mean nothing in the current body.
    /// </summary>
    private IReadOnlyList<EarlierRevision> EarlierRevisionsFor(Response response)
    {
        return
        [
            .. this.References[response.Id]
                .Where(r => r.RevisionNumber != response.RevisionNumber)
                .GroupBy(r => r.RevisionNumber)
                .OrderBy(group => group.Key)
                .Select(group => new EarlierRevision()
                {
                    Revision = group.First().Revision,
                    References = [.. group],
                }),
        ];
    }
}
