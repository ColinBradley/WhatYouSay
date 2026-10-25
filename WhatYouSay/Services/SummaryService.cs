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
    /// The draft's input revision becomes the summary's applied input revision, which is null
    /// when the draft gives none.
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
            ? await this.CurrentTrees().FirstOrDefaultAsync(s => s.Id == id && s.TopicId == topic.Id, cancellationToken)
            : null;

        if (replacing is not null && summary is null)
        {
            throw this.Reject(topic, "unknown_summary", "/", $"No summary {replacing} on this topic.");
        }

        var live = summary is null
            && topic.SummaryPolicy == SummaryPolicy.Live
            && !await mDb.Summaries.AnyAsync(s => s.TopicId == topic.Id && s.IsLive, cancellationToken);

        return await this.SaveAsync(topic, draft, createdBy, summary, live, false, cancellationToken);
    }

    /// <summary>
    /// Creates the topic's live summary, or revises it, for a summariser running unattended.
    /// Rejects the whole call rather than applying it partially, as <see cref="SaveDraftAsync"/> does.
    /// </summary>
    /// <remarks>
    /// <see cref="SummaryDraft.BasedOnRevision"/> is null to create the live summary, and the
    /// revision read to revise it. A write that expected no live summary is refused when one
    /// exists, so two first writes cannot both land.
    /// <see cref="SummaryDraft.InputRevision"/> and every reference's revision are required.
    /// An input revision older than the topic's current one is accepted, so newer input stays pending.
    /// </remarks>
    public async Task<Summary> SaveLiveAsync(
        Topic topic,
        SummaryDraft draft,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        if (topic.SummaryPolicy != SummaryPolicy.Live)
        {
            throw this.Reject(
                topic,
                "summaries_reviewed",
                "/",
                "This topic has review switched on, so it has no live summary and nothing was "
                + "saved. Write a draft for the admin to review instead.");
        }

        if (draft.InputRevision is null)
        {
            throw this.Reject(
                topic,
                "input_revision_required",
                "/inputRevision",
                "A live write says which input it was generated from. Send the inputRevision of "
                + "the snapshot you read.");
        }

        var summary = await this.CurrentTrees()
            .FirstOrDefaultAsync(s => s.TopicId == topic.Id && s.IsLive, cancellationToken);

        if (summary is null && draft.BasedOnRevision is { } basedOn)
        {
            throw this.Reject(
                topic,
                "stale_revision",
                "/basedOnRevision",
                $"This draft is based on revision {basedOn} of the live summary, but the topic has "
                + "no live summary now, so nothing was saved. Read the topic again.");
        }

        if (summary is not null && draft.BasedOnRevision is null)
        {
            throw this.Reject(
                topic,
                "live_summary_exists",
                "/basedOnRevision",
                $"The topic already has live summary {summary.Id}, at revision {summary.Revision}, "
                + "so nothing was saved. Read the topic again and revise that summary, sending its "
                + "revision as basedOnRevision.");
        }

        return await this.SaveAsync(topic, draft, "agent", summary, true, true, cancellationToken);
    }

    /// <summary>
    /// Validates a draft and writes it over <paramref name="summary"/>, or into a new summary
    /// when that is null. A new summary is the live summary when <paramref name="live"/> is true,
    /// and a private draft otherwise.
    /// </summary>
    private async Task<Summary> SaveAsync(
        Topic topic,
        SummaryDraft draft,
        string createdBy,
        Summary? summary,
        bool live,
        bool requireNamedRevisions,
        CancellationToken cancellationToken
    )
    {
        if (summary is not null)
        {
            this.RequireWritable(topic, summary, draft);
        }

        await this.RequireInputRevisionAsync(topic, draft, summary, cancellationToken);

        var stored = summary?.Nodes.ToDictionary(node => node.Id) ?? [];
        var evidence = await this.ValidateAsync(topic, draft, stored, requireNamedRevisions, cancellationToken);
        var responseCount = await mDb.Responses.CountAsync(
            r => r.TopicId == topic.Id && !r.IsDeleted,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var creating = summary is null;

        if (summary is null)
        {
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
                ResponseCountAtWrite = responseCount,
                AppliedInputRevision = draft.InputRevision,
                CreatedAt = now,
                UpdatedAt = now,
            };

            mDb.Summaries.Add(summary);
        }
        else
        {
            summary.Body = draft.Body;
            summary.UpdatedAt = now;
            summary.ResponseCountAtWrite = responseCount;
            summary.AppliedInputRevision = draft.InputRevision;
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
                + "Read the topic again to find it, and revise that summary instead.");
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
                // Validation has already refused a reference that names no live revision.
                var revision = evidence.Find(referenceDraft)!;
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
    /// Refuses an input revision that the topic has not reached yet.
    /// Also refuses one older than the input that the summary already reflects, which only a
    /// caller mixing up two reads could send.
    /// </summary>
    private async Task RequireInputRevisionAsync(
        Topic topic,
        SummaryDraft draft,
        Summary? summary,
        CancellationToken cancellationToken
    )
    {
        if (draft.InputRevision is not { } claimed)
        {
            return;
        }

        var current = await mDb.Topics
            .Where(t => t.Id == topic.Id)
            .Select(t => t.InputRevision)
            .SingleAsync(cancellationToken);

        var applied = summary?.AppliedInputRevision ?? 1;

        if (claimed >= applied && claimed <= current)
        {
            return;
        }

        throw this.Reject(
            topic,
            "input_revision_out_of_range",
            "/inputRevision",
            $"Input revision {claimed} is outside {applied} to {current}. The summary already "
            + $"reflects input revision {applied}, and the topic is at {current}. Send the "
            + "inputRevision of the snapshot you generated from.");
    }

    /// <summary>
    /// Runs before anything is written, so a failure leaves nothing behind.
    /// Returns the revisions that the draft's references can be located in.
    /// </summary>
    private async Task<Evidence> ValidateAsync(
        Topic topic,
        SummaryDraft draft,
        Dictionary<int, SummaryNode> stored,
        bool requireNamedRevisions,
        CancellationToken cancellationToken
    )
    {
        if (draft.Nodes.Count == 0)
        {
            throw this.Reject(topic, "empty_summary", "/nodes", "A summary needs at least one node.");
        }

        var evidence = await this.EvidenceAsync(topic, draft, cancellationToken);

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

        return evidence;

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
            if (!evidence.CurrentNumbers.ContainsKey(reference.ResponseId))
            {
                failures.Add(new GroundingFailure()
                {
                    Path = $"{path}/responseId",
                    Reason = "unknown_response",
                    Message = $"\"{Trim(node.Text!)}\" cites response {reference.ResponseId}, which "
                        + "is not a live response on this topic. It may have been withdrawn, or "
                        + "hidden by the admin.",
                });

                return;
            }

            if (reference.Revision is null && requireNamedRevisions)
            {
                failures.Add(new GroundingFailure()
                {
                    Path = $"{path}/revision",
                    Reason = "revision_required",
                    Message = $"\"{Trim(node.Text!)}\" cites response {reference.ResponseId} without "
                        + "saying which revision the quote is copied from. Send the revision the "
                        + "response had in the input you read.",
                });

                return;
            }

            if (evidence.Find(reference) is not { } revision)
            {
                failures.Add(new GroundingFailure()
                {
                    Path = $"{path}/revision",
                    Reason = "unknown_revision",
                    Message = $"\"{Trim(node.Text!)}\" cites revision {reference.Revision} of response "
                        + $"{reference.ResponseId}, which has no such revision.",
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
                    + $"not occur in revision {revision.Number} of response {reference.ResponseId}. "
                    + "Quotes must be copied exactly from the response text.",
                Nearest = mismatch.Nearest,
                Detail = mismatch.Detail,
            });
        }
    }

    /// <summary>
    /// Loads every revision of each live response that the draft cites.
    /// A cited response that is withdrawn, hidden or on another topic is left out.
    /// </summary>
    private async Task<Evidence> EvidenceAsync(
        Topic topic,
        SummaryDraft draft,
        CancellationToken cancellationToken
    )
    {
        var cited = draft.Nodes
            .SelectMany(Walk)
            .SelectMany(node => node.References ?? [])
            .Select(reference => reference.ResponseId)
            .Distinct()
            .ToList();

        var rows = await mDb.ResponseRevisions
            .Where(v => v.Response.TopicId == topic.Id
                && !v.Response.IsDeleted
                && cited.Contains(v.ResponseId))
            .Select(v => new { Revision = v, Current = v.Response.RevisionNumber })
            .ToListAsync(cancellationToken);

        return new Evidence()
        {
            Revisions = rows.ToDictionary(row => (row.Revision.ResponseId, row.Revision.Number), row => row.Revision),
            CurrentNumbers = rows
                .GroupBy(row => row.Revision.ResponseId)
                .ToDictionary(group => group.Key, group => group.First().Current),
        };

        static IEnumerable<NodeDraft> Walk(NodeDraft node)
        {
            return node.Children.SelectMany(Walk).Prepend(node);
        }
    }

    /// <summary>The revisions of live responses that a draft cites, keyed so each reference finds the one it names.</summary>
    private sealed record Evidence
    {
        public required IReadOnlyDictionary<(Guid ResponseId, int Number), ResponseRevision> Revisions { get; init; }

        /// <summary>The current revision number of each cited live response.</summary>
        public required Dictionary<Guid, int> CurrentNumbers { get; init; }

        /// <summary>
        /// The revision a reference names, or the current one when it names none.
        /// Null when the response is not live on the topic, or has no such revision.
        /// </summary>
        public ResponseRevision? Find(ReferenceDraft reference)
        {
            if (!this.CurrentNumbers.TryGetValue(reference.ResponseId, out var current))
            {
                return null;
            }

            return this.Revisions.GetValueOrDefault((reference.ResponseId, reference.Revision ?? current));
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
    /// Summaries with their current tree and its references, tracked for a write.
    /// References to withdrawn responses are kept, so a node carried forward keeps them.
    /// </summary>
    private IQueryable<Summary> CurrentTrees()
    {
        return mDb.Summaries
            .Include(s => s.Nodes.Where(n => n.ArchivedAt == null))
                .ThenInclude(n => n.References);
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
