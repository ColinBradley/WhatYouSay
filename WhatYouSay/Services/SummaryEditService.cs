using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

/// <summary>Which way a node moves. Up and down stay among siblings; the other two change depth.</summary>
public enum NodeMove
{
    Up,
    Down,
    Indent,
    Outdent,
}

/// <summary>
/// A human's edits to a draft summary, one operation at a time.
/// </summary>
/// <remarks>
/// Separate from <see cref="SummaryService"/> because the two have opposite shapes: an agent
/// replaces a whole tree and is validated as a whole, where a person nudges one node and
/// spends most of the edit with the tree in a state no agent would be allowed to submit.
/// </remarks>
public class SummaryEditService(WhatYouSayContext db)
{
    /// <summary>The draft with its whole tree, references and their responses.</summary>
    public async Task<Summary?> LoadAsync(
        Survey survey,
        Guid summaryId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        var summary = await db.Summaries
            .Include(s => s.Nodes)
                .ThenInclude(n => n.References.Where(r => !r.Response.IsDeleted))
                    .ThenInclude(r => r.Response)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == summaryId && s.SurveyId == survey.Id, cancellationToken);

        if (summary is not null)
        {
            SummaryTree.Assemble(summary);
        }

        return summary;
    }

    public async Task SetBodyAsync(
        Survey survey,
        Guid summaryId,
        string body,
        CancellationToken cancellationToken = default
    )
    {
        var summary = await this.RequireDraftAsync(survey, summaryId, cancellationToken);

        summary.Body = body.Trim();

        await this.SaveAsync(survey, summary, "body", cancellationToken);
    }

    public async Task SetTextAsync(
        Survey survey,
        Guid summaryId,
        int nodeId,
        string text,
        CancellationToken cancellationToken = default
    )
    {
        var summary = await this.RequireDraftAsync(survey, summaryId, cancellationToken);
        var node = Require(summary, nodeId);
        var trimmed = text.Trim();

        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException(
                "A node has to say something. Its text is the only thing that carries its meaning.");
        }

        node.Text = trimmed;

        await this.SaveAsync(survey, summary, "text", cancellationToken);
    }

    /// <summary>Appends a node to the end of a sibling group, or to the roots when parentless.</summary>
    public async Task<int> AddNodeAsync(
        Survey survey,
        Guid summaryId,
        int? parentId,
        string text,
        CancellationToken cancellationToken = default
    )
    {
        var summary = await this.RequireDraftAsync(survey, summaryId, cancellationToken);

        if (parentId is { } id)
        {
            Require(summary, id);
        }

        var trimmed = text.Trim();

        var node = new SummaryNode()
        {
            SummaryId = summary.Id,
            ParentId = parentId,
            Text = trimmed.Length == 0 ? "New node" : trimmed,
            Ordinal = SummaryTree.Siblings(summary, parentId).Count,
        };

        // Added through the navigation alone. Adding to the DbSet as well would leave the
        // node in summary.Nodes twice once fixup runs, and every ordinal after it wrong.
        summary.Nodes.Add(node);

        await this.SaveAsync(survey, summary, "add", cancellationToken);

        return node.Id;
    }

    /// <summary>Takes the node's whole subtree with it, along with every quote and reaction on it.</summary>
    public async Task DeleteNodeAsync(
        Survey survey,
        Guid summaryId,
        int nodeId,
        CancellationToken cancellationToken = default
    )
    {
        var summary = await this.RequireDraftAsync(survey, summaryId, cancellationToken);
        var node = Require(summary, nodeId);
        var doomed = SummaryTree.Subtree(summary, node).ToList();

        // Removed explicitly rather than left to the self-referencing cascade, which only
        // fires for children EF happens to be tracking.
        db.SummaryNodes.RemoveRange(doomed);

        foreach (var gone in doomed)
        {
            summary.Nodes.Remove(gone);
        }

        SummaryTree.Renumber(SummaryTree.Siblings(summary, node.ParentId));

        await this.SaveAsync(survey, summary, "delete", cancellationToken);
    }

    /// <summary>Reorders or reparents a node. A move with nowhere to go does nothing.</summary>
    public async Task MoveAsync(
        Survey survey,
        Guid summaryId,
        int nodeId,
        NodeMove move,
        CancellationToken cancellationToken = default
    )
    {
        var summary = await this.RequireDraftAsync(survey, summaryId, cancellationToken);
        var node = Require(summary, nodeId);
        var siblings = SummaryTree.Siblings(summary, node.ParentId);
        var at = siblings.IndexOf(node);

        switch (move)
        {
            case NodeMove.Up when at > 0:
                (siblings[at - 1], siblings[at]) = (siblings[at], siblings[at - 1]);
                SummaryTree.Renumber(siblings);
                break;

            case NodeMove.Down when at < siblings.Count - 1:
                (siblings[at + 1], siblings[at]) = (siblings[at], siblings[at + 1]);
                SummaryTree.Renumber(siblings);
                break;

            case NodeMove.Indent when at > 0:
                var adopter = siblings[at - 1];
                node.ParentId = adopter.Id;
                node.Ordinal = SummaryTree.Siblings(summary, adopter.Id).Count;
                siblings.RemoveAt(at);
                SummaryTree.Renumber(siblings);
                break;

            case NodeMove.Outdent when node.ParentId is { } parentId:
                var parent = Require(summary, parentId);
                var uncles = SummaryTree.Siblings(summary, parent.ParentId);

                node.ParentId = parent.ParentId;
                siblings.RemoveAt(at);
                SummaryTree.Renumber(siblings);

                // Lands directly after what used to be its parent, which is where the eye
                // expects it after an outdent.
                uncles.Insert(uncles.IndexOf(parent) + 1, node);
                SummaryTree.Renumber(uncles);
                break;

            default:
                return;
        }

        SummaryTree.Assemble(summary);

        await this.SaveAsync(survey, summary, "move", cancellationToken);
    }

    /// <summary>
    /// Cites a span of a response. The quote is located rather than trusted, so a hand-typed
    /// one fails exactly as an agent's would.
    /// </summary>
    public async Task AddReferenceAsync(
        Survey survey,
        Guid summaryId,
        int nodeId,
        Guid responseId,
        string quote,
        CancellationToken cancellationToken = default
    )
    {
        var summary = await this.RequireDraftAsync(survey, summaryId, cancellationToken);
        var node = Require(summary, nodeId);

        var response = await db.Responses.FirstOrDefaultAsync(
            r => r.Id == responseId && r.SurveyId == survey.Id && !r.IsDeleted,
            cancellationToken);

        if (response is null)
        {
            throw Reject(
                "unknown_response",
                $"/nodes/{nodeId}/references/responseId",
                $"Response {responseId} is not a live response on this survey.");
        }

        if (QuoteLocator.Locate(response.Body, quote) is not { } location)
        {
            var mismatch = QuoteLocator.Diagnose(response.Body, quote);

            throw new SummaryGroundingException(
                "quote_not_found",
                mismatch.Nearest is null
                    ? $"That quote does not occur in the response. {mismatch.Detail}"
                    : $"That quote does not occur in the response. {mismatch.Detail} The response "
                        + $"actually says: \"{mismatch.Nearest}\"",
                [
                    new GroundingFailure()
                    {
                        Path = $"/nodes/{nodeId}/references/quote",
                        Reason = "quote_not_found",
                        Message = "Quotes must be copied exactly from the response text.",
                        Nearest = mismatch.Nearest,
                        Detail = mismatch.Detail,
                    },
                ]);
        }

        var reference = new SummaryNodeReference()
        {
            NodeId = node.Id,
            ResponseId = response.Id,
            Quote = quote,
            StartIndex = location.StartIndex,
            EndIndex = location.EndIndex,
        };

        node.References.Add(reference);

        await this.SaveAsync(survey, summary, "cite", cancellationToken);
    }

    public async Task DeleteReferenceAsync(
        Survey survey,
        Guid summaryId,
        int referenceId,
        CancellationToken cancellationToken = default
    )
    {
        var summary = await this.RequireDraftAsync(survey, summaryId, cancellationToken);

        var reference = await db.References.FirstOrDefaultAsync(
            r => r.Id == referenceId && r.Node.SummaryId == summary.Id,
            cancellationToken);

        if (reference is null)
        {
            return;
        }

        db.References.Remove(reference);

        await this.SaveAsync(survey, summary, "uncite", cancellationToken);
    }

    private async Task SaveAsync(
        Survey survey,
        Summary summary,
        string kind,
        CancellationToken cancellationToken
    )
    {
        summary.UpdatedAt = DateTimeOffset.UtcNow;

        // A version an agent wrote stops being purely the agent's the moment a person
        // changes it, and the version list is where that shows.
        summary.CreatedBy = "human";

        await db.SaveChangesAsync(cancellationToken);

        WhatYouSayTelemetry.SummaryEdited(survey, kind);
    }

    private async Task<Summary> RequireDraftAsync(
        Survey survey,
        Guid summaryId,
        CancellationToken cancellationToken
    )
    {
        var summary = await this.LoadAsync(survey, summaryId, cancellationToken)
            ?? throw new InvalidOperationException($"No summary {summaryId} on this survey.");

        if (!summary.IsDraft)
        {
            throw new InvalidOperationException(
                "This summary is published. Unpublish it before making changes, so nobody is "
                + "reading a version that is moving under them.");
        }

        return summary;
    }

    private static SummaryNode Require(Summary summary, int nodeId)
    {
        return summary.Nodes.FirstOrDefault(node => node.Id == nodeId)
            ?? throw new InvalidOperationException($"Node {nodeId} is not on this summary.");
    }

    private static SummaryGroundingException Reject(string reason, string path, string message)
    {
        return new SummaryGroundingException(
            reason,
            message,
            [new GroundingFailure() { Path = path, Reason = reason, Message = message }]);
    }
}
