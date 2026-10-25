using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>An earlier wording of a response, with the references that quote it.</summary>
public sealed record EarlierRevision
{
    public required ResponseRevision Revision { get; init; }

    public required IReadOnlyList<SummaryNodeReference> References { get; init; }
}

/// <summary>Every public response on a topic, as cards that take reactions. Sits inside a <see cref="TopicFeed"/>.</summary>
public partial class ResponsePane
{
    [CascadingParameter]
    public TopicFeed Feed { get; set; } = default!;

    [CascadingParameter]
    public TopicView View { get; set; } = default!;

    private IReadOnlyList<SummaryNodeReference> CurrentReferencesFor(Response response)
    {
        return [.. this.View.References[response.Id].Where(r => r.RevisionNumber == response.RevisionNumber)];
    }

    /// <summary>
    /// Earlier wordings of the response that the summary still quotes.
    /// Each is shown as it was, because its offsets mean nothing in the current body.
    /// </summary>
    private IReadOnlyList<EarlierRevision> EarlierRevisionsFor(Response response)
    {
        return
        [
            .. this.View.References[response.Id]
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
