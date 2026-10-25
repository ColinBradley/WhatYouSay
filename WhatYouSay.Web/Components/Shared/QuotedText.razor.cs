using Microsoft.AspNetCore.Components;
using WhatYouSay.Data;

namespace WhatYouSay.Web.Components.Shared;

public partial class QuotedText
{
    /// <summary>The exact text the references were located in.</summary>
    [Parameter]
    [EditorRequired]
    public string Body { get; set; } = string.Empty;

    /// <summary>References located in <see cref="Body"/>. Their offsets mean nothing in any other text.</summary>
    [Parameter]
    public IReadOnlyList<SummaryNodeReference> References { get; set; } = [];

    /// <summary>Whether each mark links back to the node it supports.</summary>
    [Parameter]
    public bool Linked { get; set; }

    private string? NodeOf(int referenceId)
    {
        var reference = this.References.FirstOrDefault(r => r.Id == referenceId);

        return reference is null ? null : $"node-{reference.NodeId}";
    }
}
