using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;

namespace WhatYouSay.Web.Components.Shared;

public partial class ResponsePane
{
    [Parameter]
    [EditorRequired]
    public IReadOnlyList<Response> Responses { get; set; } = [];

    /// <summary>Every reference in the summary, so each response can find the ones citing it.</summary>
    [Parameter]
    public ILookup<Guid, SummaryNodeReference> References { get; set; } =
        Array.Empty<SummaryNodeReference>().ToLookup(r => r.ResponseId);

    private IEnumerable<SummaryNodeReference> ReferencesFor(Guid responseId)
    {
        return this.References[responseId];
    }

    /// <summary>The node a quote supports, for the jump back into the tree.</summary>
    private string? NodeOf(int referenceId)
    {
        var reference = this.References
            .SelectMany(group => group)
            .FirstOrDefault(r => r.Id == referenceId);

        return reference is null ? null : $"node-{reference.NodeId}";
    }
}
