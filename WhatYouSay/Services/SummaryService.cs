using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

public class SummaryService
{
    private readonly WhatYouSayContext mDb;

    public SummaryService(WhatYouSayContext db)
    {
        mDb = db;
    }

    /// <summary>
    /// The summary a non-admin sees at the topic's own URL.
    /// That is the live summary if there is one, and otherwise the newest published summary.
    /// </summary>
    public async Task<Summary?> FindLatestVisibleAsync(
        Guid topicId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        var summary = await this.Detailed()
            .Where(s => s.TopicId == topicId && !s.IsDraft && s.IsPublic)
            .OrderByDescending(s => s.IsLive)
            .ThenByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return Assembled(summary);
    }

    /// <summary>The topic's live summary, without its tree. Null when the topic has none.</summary>
    public async Task<Summary?> FindLiveAsync(Guid topicId, CancellationToken cancellationToken = default)
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await mDb.Summaries.FirstOrDefaultAsync(s => s.TopicId == topicId && s.IsLive, cancellationToken);
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

        return await mDb.Summaries
            .Where(s => s.TopicId == topicId && !s.IsDraft && s.IsPublic)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// How many archived nodes each of the topic's summaries holds, keyed by summary id.
    /// A summary with none has no entry.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, int>> CountArchivedAsync(
        Guid topicId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await mDb.SummaryNodes
            .Where(n => n.Summary.TopicId == topicId && n.ArchivedAt != null)
            .GroupBy(n => n.SummaryId)
            .Select(group => new { SummaryId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.SummaryId, entry => entry.Count, cancellationToken);
    }

    public async Task<IReadOnlyList<Summary>> ListAllAsync(
        Guid topicId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await mDb.Summaries
            .Where(s => s.TopicId == topicId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a summary, or replaces the overview and tree of an existing one.
    /// Rejects the whole call rather than applying it partially, so a summary is never half-cited.
    /// </summary>
    /// <remarks>
    /// A new summary becomes the live summary when the topic is live and has none yet.
    /// The live summary is published from the start. Any other new summary is a draft.
    /// Stored nodes that the draft leaves out are archived rather than deleted, so the
    /// reactions and comments on them survive.
    /// </remarks>
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
            ? await mDb.Summaries
                .Include(s => s.Nodes.Where(n => n.ArchivedAt == null))
                    .ThenInclude(n => n.References)
                .FirstOrDefaultAsync(s => s.Id == id && s.TopicId == topic.Id, cancellationToken)
            : null;

        if (replacing is not null && summary is null)
        {
            throw this.Reject(topic, "unknown_summary", "/", $"No summary {replacing} on this topic.");
        }

        if (summary is not null)
        {
            this.RequireWritable(topic, summary, draft);
        }

        var stored = summary?.Nodes.ToDictionary(node => node.Id) ?? [];
        var revisions = await this.ValidateAsync(topic, draft, stored, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var creating = summary is null;

        if (summary is null)
        {
            var live = topic.SummaryPolicy == SummaryPolicy.Live
                && !await mDb.Summaries.AnyAsync(s => s.TopicId == topic.Id && s.IsLive, cancellationToken);

            summary = new Summary()
            {
                Id = Guid.CreateVersion7(),
                TopicId = topic.Id,
                Body = draft.Body,
                CreatedBy = createdBy,
                IsLive = live,
                IsDraft = !live,
                IsPublic = live,
                IsAgentEditable = true,
                ResponseCountAtWrite = revisions.Count,
                CreatedAt = now,
                UpdatedAt = now,
            };

            mDb.Summaries.Add(summary);
        }
        else
        {
            summary.Body = draft.Body;
            summary.UpdatedAt = now;
            summary.ResponseCountAtWrite = revisions.Count;
            summary.Revision++;
        }

        var kept = new HashSet<int>();

        for (var i = 0; i < draft.Nodes.Count; i++)
        {
            Apply(draft.Nodes[i], null, i);
        }

        // An archived node keeps its parent, even a parent still in the tree, so the admin
        // can see where it sat.
        var archived = stored
            .Where(entry => !kept.Contains(entry.Key))
            .Select(entry => entry.Value)
            .ToList();

        foreach (var node in archived)
        {
            node.ArchivedAt = now;
        }

        try
        {
            await mDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw this.Reject(
                topic,
                "stale_revision",
                "/basedOnRevision",
                $"Summary {summary.Id} changed while this draft was being saved, so nothing was "
                + "saved. GET it again and redo your changes against what is there now.");
        }
        catch (DbUpdateException) when (creating && summary.IsLive)
        {
            throw this.Reject(
                topic,
                "live_summary_exists",
                "/",
                "Another write created this topic's live summary first, so nothing was saved. "
                + "GET /summaries to find it, and revise that one instead.");
        }

        Forget(archived);
        SummaryTree.Assemble(summary);

        WhatYouSayTelemetry.SummaryDrafted(topic);

        return summary;

        // Takes archived nodes out of the summary in memory, so the summary handed back holds
        // only the current tree, as a fresh read would. Each node is detached first, because
        // removing a tracked node from the collection would delete it on the next save.
        void Forget(List<SummaryNode> nodes)
        {
            foreach (var node in nodes)
            {
                foreach (var reference in node.References)
                {
                    mDb.Entry(reference).State = EntityState.Detached;
                }

                mDb.Entry(node).State = EntityState.Detached;
                summary.Nodes.Remove(node);
            }
        }

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
                    mDb.References.RemoveRange(node.References);
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
                var revision = revisions[referenceDraft.ResponseId];
                var location = QuoteLocator.Locate(revision.Body, referenceDraft.Quote)!.Value;

                node.References.Add(new SummaryNodeReference()
                {
                    ResponseId = revision.ResponseId,
                    RevisionNumber = revision.Number,
                    // The span taken from Body, not the string that was sent: matching
                    // forgives presentation, so the two can differ and only one is true.
                    Quote = revision.Body[location.StartIndex..location.EndIndex],
                    StartIndex = location.StartIndex,
                    EndIndex = location.EndIndex,
                });
            }
        }
    }

    /// <summary>
    /// Refuses a write to a summary the summariser may not change, or one based on a revision
    /// that is no longer current.
    /// </summary>
    private void RequireWritable(Topic topic, Summary summary, SummaryDraft draft)
    {
        if (!summary.IsAgentEditable)
        {
            throw this.Reject(
                topic,
                "summary_not_agent_editable",
                "/",
                summary.IsLive
                    ? "An admin has paused summariser updates to the live summary, so nothing was "
                        + "saved. Try again once they resume them."
                    : "That summary is not open to the summariser. An admin can hand it back, or "
                        + "create a new one instead.");
        }

        if (!summary.IsDraft && !summary.IsLive)
        {
            throw this.Reject(
                topic,
                "summary_not_agent_editable",
                "/",
                "That summary is published, and only the live summary can change while people "
                + "read it. Create a new summary instead.");
        }

        if (summary.IsLive && draft.BasedOnRevision is null)
        {
            throw this.Reject(
                topic,
                "revision_required",
                "/basedOnRevision",
                "This is the live summary, which people can edit while you work. Send the "
                + "revision you read as basedOnRevision, so a change made since then is not "
                + "overwritten.");
        }

        if (draft.BasedOnRevision is { } basedOn && basedOn != summary.Revision)
        {
            throw this.Reject(
                topic,
                "stale_revision",
                "/basedOnRevision",
                $"Summary {summary.Id} is at revision {summary.Revision}, but this draft is based "
                + $"on revision {basedOn}. It has changed since you read it, so nothing was saved. "
                + "GET it again and redo your changes against what is there now.");
        }
    }

    /// <summary>
    /// Runs before anything is written, so a failure leaves nothing behind.
    /// Returns the current revision of every live response, keyed by response id.
    /// New citations are located in these.
    /// </summary>
    private async Task<Dictionary<Guid, ResponseRevision>> ValidateAsync(
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

        var revisions = await CurrentRevisions(mDb, topic)
            .ToDictionaryAsync(v => v.ResponseId, cancellationToken);

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

        return revisions;

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
            if (!revisions.TryGetValue(reference.ResponseId, out var revision))
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

            if (QuoteLocator.Locate(revision.Body, reference.Quote) is not null)
            {
                return;
            }

            var mismatch = QuoteLocator.Diagnose(revision.Body, reference.Quote);

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

    /// <summary>
    /// The current revision of each live response on the topic.
    /// A new citation may only be located in one of these.
    /// </summary>
    internal static IQueryable<ResponseRevision> CurrentRevisions(WhatYouSayContext db, Topic topic)
    {
        return db.ResponseRevisions.Where(v =>
            v.Response.TopicId == topic.Id
            && !v.Response.IsDeleted
            && v.Number == v.Response.RevisionNumber);
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
    /// Loads every node of the current tree flat, in one query — EF cannot eager-load an
    /// arbitrary depth, so the tree is stitched afterwards. Withdrawn responses, with every
    /// revision of them, and archived nodes are filtered out here rather than at render time,
    /// so no caller can surface one by accident.
    /// </summary>
    private IQueryable<Summary> Detailed()
    {
        return WithCitations(mDb.Summaries).AsSplitQuery();
    }

    /// <summary>
    /// Includes every node in the current tree, and each node's references to live responses.
    /// Each reference comes with its response and the revision it quotes.
    /// Archived nodes are left out here, so no read of the current tree can surface one.
    /// </summary>
    internal static IQueryable<Summary> WithCitations(IQueryable<Summary> summaries)
    {
        return summaries
            .Include(s => s.Nodes.Where(n => n.ArchivedAt == null))
                .ThenInclude(n => n.References.Where(r => !r.Response.IsDeleted))
                    .ThenInclude(r => r.Response)
            .Include(s => s.Nodes.Where(n => n.ArchivedAt == null))
                .ThenInclude(n => n.References.Where(r => !r.Response.IsDeleted))
                    .ThenInclude(r => r.Revision);
    }

    /// <summary>
    /// The nodes the summariser has archived from a summary, most recent first.
    /// Each comes with its parent, its citations of live responses, and every reaction and
    /// comment on it, hidden comments included. Empty when the summary is not on this topic.
    /// </summary>
    /// <remarks>
    /// Untracked, because a tracked archived node would be fixed up into the tree of any
    /// summary the same context had already loaded.
    /// </remarks>
    public async Task<IReadOnlyList<SummaryNode>> ListArchivedAsync(
        Topic topic,
        Guid summaryId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        return await mDb.SummaryNodes
            .AsNoTracking()
            .AsSplitQuery()
            .Where(n => n.SummaryId == summaryId && n.Summary.TopicId == topic.Id && n.ArchivedAt != null)
            .Include(n => n.Parent)
            .Include(n => n.References.Where(r => !r.Response.IsDeleted))
                .ThenInclude(r => r.Response)
            .Include(n => n.References.Where(r => !r.Response.IsDeleted))
                .ThenInclude(r => r.Revision)
            .Include(n => n.Reactions)
            .Include(n => n.Comments)
            .OrderByDescending(n => n.ArchivedAt)
            .ThenBy(n => n.Id)
            .ToListAsync(cancellationToken);
    }
}
