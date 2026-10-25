using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;

namespace WhatYouSay.Web.Components.Shared;

/// <summary>An earlier wording of a response, with the references that quote it.</summary>
public sealed record EarlierRevision
{
    public required ResponseRevision Revision { get; init; }

    public required IReadOnlyList<SummaryNodeReference> References { get; init; }
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
