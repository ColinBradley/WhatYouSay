using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

public class SummaryService(WhatYouSayContext db)
{
    /// <summary>
    /// The newest summary a non-admin is allowed to see: blessed by a human and published.
    /// </summary>
    public async Task<Summary?> FindLatestVisibleAsync(
        Guid topicId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        var summary = await this.Detailed()
            .Where(s => s.TopicId == topicId && !s.IsDraft && s.IsPublic)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return Assembled(summary);
    }

    public async Task<Summary?> FindAsync(Guid summaryId, CancellationToken cancellationToken = default)
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        var summary = await this.Detailed()
            .FirstOrDefaultAsync(s => s.Id == summaryId, cancellationToken);

        return Assembled(summary);
    }

    public async Task<IReadOnlyList<Summary>> ListVisibleAsync(
        Guid topicId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await db.Summaries
            .Where(s => s.TopicId == topicId && !s.IsDraft && s.IsPublic)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Summary>> ListAllAsync(
        Guid topicId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await db.Summaries
            .Where(s => s.TopicId == topicId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a draft, or replaces an existing one. Rejects the whole call rather than
    /// applying it partially, so a summary is never half-cited.
    /// </summary>
    public async Task<Summary> SaveDraftAsync(
        Topic topic,
        SummaryDraft draft,
        string createdBy,
        Guid? replacing = null,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        var summary = replacing is { } id
            ? await db.Summaries
                .Include(s => s.Nodes)
                    .ThenInclude(n => n.References)
                .FirstOrDefaultAsync(s => s.Id == id && s.TopicId == topic.Id, cancellationToken)
            : null;

        if (replacing is not null && summary is null)
        {
            throw this.Reject(topic, "unknown_summary", "/", $"No summary {replacing} on this topic.");
        }

        if (summary is not null && !summary.IsAgentEditable)
        {
            throw this.Reject(
                topic,
                "summary_not_agent_editable",
                "/",
                "That summary is not open to the summariser. An admin can hand it back, or "
                + "create a new one instead.");
        }

        var stored = summary?.Nodes.ToDictionary(node => node.Id) ?? [];
        var responses = await this.ValidateAsync(topic, draft, stored, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (summary is null)
        {
            summary = new Summary()
            {
                Id = Guid.CreateVersion7(),
                TopicId = topic.Id,
                Body = draft.Body,
                CreatedBy = createdBy,
                IsAgentEditable = true,
                ResponseCountAtWrite = responses.Count,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.Summaries.Add(summary);
        }
        else
        {
            summary.Body = draft.Body;
            summary.UpdatedAt = now;
            summary.ResponseCountAtWrite = responses.Count;
        }

        var kept = new HashSet<int>();

        for (var i = 0; i < draft.Nodes.Count; i++)
        {
            Apply(draft.Nodes[i], null, i);
        }

        // After the whole tree has been re-parented, so a node moved out from under a
        // deleted one is no longer its dependent and does not cascade away with it.
        foreach (var (storedId, node) in stored)
        {
            if (!kept.Contains(storedId))
            {
                db.SummaryNodes.Remove(node);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        SummaryTree.Assemble(summary);

        WhatYouSayTelemetry.SummaryDrafted(topic);

        return summary;

        void Apply(NodeDraft draftNode, SummaryNode? parent, int ordinal)
        {
            SummaryNode node;

            if (draftNode.Id is { } nodeId)
            {
                node = stored[nodeId];
                kept.Add(nodeId);

                // No text means carry it forward: its own words and citations are left
                // exactly as they are, and only its place in the tree is the agent's to say.
                if (draftNode.Text is { } revised)
                {
                    node.Text = revised;
                    db.References.RemoveRange(node.References);
                    node.References.Clear();
                    Cite(node, draftNode);
                }
            }
            else
            {
                node = new SummaryNode()
                {
                    // Validation refuses a node carrying neither an id nor text.
                    Text = draftNode.Text ?? throw new InvalidOperationException("A new node needs text."),
                };

                // Every node carries SummaryId, so every node joins the flat collection.
                summary.Nodes.Add(node);
                Cite(node, draftNode);
            }

            node.Parent = parent;
            node.Ordinal = ordinal;

            if (parent is null)
            {
                node.ParentId = null;
            }

            for (var i = 0; i < draftNode.Children.Count; i++)
            {
                Apply(draftNode.Children[i], node, i);
            }
        }

        void Cite(SummaryNode node, NodeDraft draftNode)
        {
            foreach (var referenceDraft in draftNode.References ?? [])
            {
                var response = responses[referenceDraft.ResponseId];
                var location = QuoteLocator.Locate(response.Body, referenceDraft.Quote)!.Value;

                node.References.Add(new SummaryNodeReference()
                {
                    ResponseId = response.Id,
                    // The span taken from Body, not the string that was sent: matching
                    // forgives presentation, so the two can differ and only one is true.
                    Quote = response.Body[location.StartIndex..location.EndIndex],
                    StartIndex = location.StartIndex,
                    EndIndex = location.EndIndex,
                });
            }
        }
    }

    /// <summary>Runs before anything is written, so a failure leaves nothing behind.</summary>
    private async Task<Dictionary<Guid, Response>> ValidateAsync(
        Topic topic,
        SummaryDraft draft,
        Dictionary<int, SummaryNode> stored,
        CancellationToken cancellationToken
    )
    {
        if (draft.Nodes.Count == 0)
        {
            throw this.Reject(topic, "empty_summary", "/nodes", "A summary needs at least one node.");
        }

        var responses = await db.Responses
            .Where(r => r.TopicId == topic.Id && !r.IsDeleted)
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        // Every problem is collected rather than thrown on, so one retry can fix the lot.
        var failures = new List<GroundingFailure>();
        var claimed = new HashSet<int>();

        for (var i = 0; i < draft.Nodes.Count; i++)
        {
            Check(draft.Nodes[i], $"/nodes/{i}", supported: false);
        }

        if (failures.Count > 0)
        {
            throw this.Reject(topic, failures);
        }

        return responses;

        // Nothing on a node says what sort of thing it is, so the rule cannot ask which nodes
        // are obliged to cite. What it can ask is that every path down the tree ends somewhere
        // real: a leaf either cites for itself or sits under something that does. A heading
        // needs no citation because the requirement lands on what hangs below it, and a
        // childless node with none is not a heading, whatever it was meant to be.
        //
        // A node carried forward by bare id sits outside that rule. It is not being asserted
        // here - it already exists, and a person may have written it without a quote - so it
        // is neither checked nor able to ground anything below it unless it is itself cited.
        void Check(NodeDraft node, string path, bool supported)
        {
            var existing = Resolve(node, path);
            var authored = !string.IsNullOrEmpty(node.Text);

            if (authored)
            {
                var references = node.References ?? [];

                for (var r = 0; r < references.Count; r++)
                {
                    CheckReference(node, references[r], $"{path}/references/{r}");
                }
            }
            else if (node.References is not null)
            {
                failures.Add(new GroundingFailure()
                {
                    Path = $"{path}/references",
                    Reason = "references_without_text",
                    Message = "References belong to the assertion they support, so they can only "
                        + "accompany text. Send the node's text along with them to revise it, or "
                        + "send the id alone to leave it as it stands.",
                });
            }

            // Support inherits down a branch, so a child of a cited node need not re-cite.
            var cites = authored
                ? node.References is { Count: > 0 }
                : existing is { References.Count: > 0 };

            var grounded = supported || cites;

            if (node.Children.Count == 0)
            {
                if (!grounded && authored)
                {
                    failures.Add(new GroundingFailure()
                    {
                        Path = $"{path}/references",
                        Reason = "branch_without_citation",
                        Message = $"Nothing cites \"{Trim(node.Text!)}\", it has nothing under it, "
                            + "and nothing above it cites a response either. Every branch has to "
                            + "end in something somebody actually wrote.",
                    });
                }

                return;
            }

            for (var c = 0; c < node.Children.Count; c++)
            {
                Check(node.Children[c], $"{path}/children/{c}", grounded);
            }
        }

        // An id is a claim about a node that already exists here, so it is checked the way a
        // responseId is: it has to name something in this summary, and only once.
        SummaryNode? Resolve(NodeDraft node, string path)
        {
            if (node.Id is not { } id)
            {
                if (string.IsNullOrEmpty(node.Text))
                {
                    failures.Add(new GroundingFailure()
                    {
                        Path = path,
                        Reason = "node_without_text",
                        Message = "A node needs text, or an id naming the stored node to carry "
                            + "forward. This one has neither.",
                    });
                }

                return null;
            }

            if (!stored.TryGetValue(id, out var existing))
            {
                failures.Add(new GroundingFailure()
                {
                    Path = $"{path}/id",
                    Reason = "unknown_node",
                    Message = $"Node {id} is not part of this summary. Ids come from GET on the "
                        + "version you are revising; leave the id out to add a new node.",
                });

                return null;
            }

            if (!claimed.Add(id))
            {
                failures.Add(new GroundingFailure()
                {
                    Path = $"{path}/id",
                    Reason = "duplicate_node_id",
                    Message = $"Node {id} appears more than once. A node has one place in the tree.",
                });

                return null;
            }

            return existing;
        }

        void CheckReference(NodeDraft node, ReferenceDraft reference, string path)
        {
            if (!responses.TryGetValue(reference.ResponseId, out var response))
            {
                failures.Add(new GroundingFailure()
                {
                    Path = $"{path}/responseId",
                    Reason = "unknown_response",
                    Message = $"\"{Trim(node.Text!)}\" cites response {reference.ResponseId}, which "
                        + "is not a live response on this topic.",
                });

                return;
            }

            // The whole of the quote-rot guarantee, and the only rule here about
            // ordering: an offset into text its author can still edit selects the wrong
            // words later while still validating now.
            if (!response.IsFrozen)
            {
                failures.Add(new GroundingFailure()
                {
                    Path = $"{path}/responseId",
                    Reason = "response_not_frozen",
                    Message = $"Response {reference.ResponseId} is still editable by whoever "
                        + "wrote it, so it cannot be cited yet. Close the topic to freeze it.",
                });

                return;
            }

            if (QuoteLocator.Locate(response.Body, reference.Quote) is not null)
            {
                return;
            }

            var mismatch = QuoteLocator.Diagnose(response.Body, reference.Quote);

            failures.Add(new GroundingFailure()
            {
                Path = $"{path}/quote",
                Reason = "quote_not_found",
                Message = $"\"{Trim(node.Text!)}\" quotes \"{Trim(reference.Quote)}\", which does "
                    + $"not occur in response {reference.ResponseId}. Quotes must be copied "
                    + "exactly from the response text.",
                Nearest = mismatch.Nearest,
                Detail = mismatch.Detail,
            });
        }
    }

    private SummaryGroundingException Reject(
        Topic topic,
        string reason,
        string path,
        string message
    )
    {
        return this.Reject(
            topic,
            [new GroundingFailure() { Path = path, Reason = reason, Message = message }]
        );
    }

    private SummaryGroundingException Reject(
        Topic topic,
        List<GroundingFailure> failures
    )
    {
        // The first reason is the one that gets tagged; the rest travel on the exception.
        var reason = failures[0].Reason;

        WhatYouSayTelemetry.SummaryRejected(topic, reason);
        Activity.Current.RecordFailure(reason);

        return new SummaryGroundingException(reason, Explain(failures), failures);
    }

    /// <summary>One readable message covering every failure, for callers without the list.</summary>
    private static string Explain(List<GroundingFailure> failures)
    {
        if (failures.Count == 1)
        {
            return Describe(failures[0]);
        }

        var lines = failures.Select(f => $"- {f.Path}: {Describe(f)}");

        return $"{failures.Count} problems with this draft, none of it saved:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, lines);
    }

    private static string Describe(GroundingFailure failure)
    {
        if (failure.Nearest is null)
        {
            return failure.Detail is null
                ? failure.Message
                : $"{failure.Message} {failure.Detail}";
        }

        return $"{failure.Message} {failure.Detail} The response actually says: "
            + $"\"{failure.Nearest}\"";
    }

    private static string Trim(string value)
    {
        return value.Length <= 60 ? value : value[..60] + "…";
    }

    private static Summary? Assembled(Summary? summary)
    {
        if (summary is not null)
        {
            SummaryTree.Assemble(summary);
        }

        return summary;
    }

    /// <summary>
    /// Loads every node of the summary flat, in one query — EF cannot eager-load an arbitrary
    /// depth, so the tree is stitched afterwards. Withdrawn responses are filtered out here
    /// rather than at render time, so no caller can surface one by accident.
    /// </summary>
    private IQueryable<Summary> Detailed()
    {
        return db.Summaries
            .Include(s => s.Nodes)
                .ThenInclude(n => n.References.Where(r => !r.Response.IsDeleted))
                    .ThenInclude(r => r.Response)
            .AsSplitQuery();
    }
}
