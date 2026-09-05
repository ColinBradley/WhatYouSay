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
        Guid surveyId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        var summary = await this.Detailed()
            .Where(s => s.SurveyId == surveyId && !s.IsDraft && s.IsPublic)
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
        Guid surveyId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await db.Summaries
            .Where(s => s.SurveyId == surveyId && !s.IsDraft && s.IsPublic)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Summary>> ListAllAsync(
        Guid surveyId,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await db.Summaries
            .Where(s => s.SurveyId == surveyId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a draft, or replaces an existing one. Rejects the whole call rather than
    /// applying it partially, so a summary is never half-cited.
    /// </summary>
    public async Task<Summary> SaveDraftAsync(
        Survey survey,
        SummaryDraft draft,
        string createdBy,
        Guid? replacing = null,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetSurvey(survey);

        var responses = await this.ValidateAsync(survey, draft, cancellationToken);

        var summary = replacing is { } id
            ? await db.Summaries
                .Include(s => s.Nodes)
                .FirstOrDefaultAsync(s => s.Id == id && s.SurveyId == survey.Id, cancellationToken)
            : null;

        if (replacing is not null && summary is null)
        {
            throw this.Reject(survey, "unknown_summary", "/", $"No summary {replacing} on this survey.");
        }

        if (summary is not null && !summary.IsDraft)
        {
            throw this.Reject(
                survey,
                "summary_published",
                "/",
                "That summary has been published, so it is immutable. Create a new one instead.");
        }

        var now = DateTimeOffset.UtcNow;

        if (summary is null)
        {
            summary = new Summary()
            {
                Id = Guid.CreateVersion7(),
                SurveyId = survey.Id,
                Body = draft.Body,
                CreatedBy = createdBy,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.Summaries.Add(summary);
        }
        else
        {
            summary.Body = draft.Body;
            summary.UpdatedAt = now;

            // The agent submits a whole summary each time, so replace rather than merge.
            db.SummaryNodes.RemoveRange(summary.Nodes);
            summary.Nodes.Clear();
        }

        for (var i = 0; i < draft.Nodes.Count; i++)
        {
            Graft(draft.Nodes[i], null, i);
        }

        await db.SaveChangesAsync(cancellationToken);

        SummaryTree.Assemble(summary);

        WhatYouSayTelemetry.SummaryDrafted(survey);

        return summary;

        void Graft(NodeDraft draftNode, SummaryNode? parent, int ordinal)
        {
            var node = new SummaryNode()
            {
                Text = draftNode.Text,
                Parent = parent,
                Ordinal = ordinal,
            };

            foreach (var referenceDraft in draftNode.References)
            {
                var response = responses[referenceDraft.ResponseId];
                var location = QuoteLocator.Locate(response.Body, referenceDraft.Quote)!.Value;

                node.References.Add(new SummaryNodeReference()
                {
                    ResponseId = response.Id,
                    Quote = referenceDraft.Quote,
                    StartIndex = location.StartIndex,
                    EndIndex = location.EndIndex,
                });
            }

            // Every node carries SummaryId, so every node joins the flat collection.
            summary.Nodes.Add(node);

            for (var i = 0; i < draftNode.Children.Count; i++)
            {
                Graft(draftNode.Children[i], node, i);
            }
        }
    }

    /// <summary>Runs before anything is written, so a failure leaves nothing behind.</summary>
    private async Task<Dictionary<Guid, Response>> ValidateAsync(
        Survey survey,
        SummaryDraft draft,
        CancellationToken cancellationToken
    )
    {
        if (survey.IsAcceptingResponses)
        {
            throw this.Reject(
                survey,
                "survey_open",
                "/",
                "This survey is still accepting responses. Summarising a moving target "
                + "produces quotes that stop matching, so close the survey first.");
        }

        if (draft.Nodes.Count == 0)
        {
            throw this.Reject(survey, "empty_summary", "/nodes", "A summary needs at least one node.");
        }

        var responses = await db.Responses
            .Where(r => r.SurveyId == survey.Id && !r.IsDeleted)
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        // Every problem is collected rather than thrown on, so one retry can fix the lot.
        var failures = new List<GroundingFailure>();

        for (var i = 0; i < draft.Nodes.Count; i++)
        {
            Check(draft.Nodes[i], $"/nodes/{i}", supported: false);
        }

        if (failures.Count > 0)
        {
            throw this.Reject(survey, failures);
        }

        return responses;

        // Nothing on a node says what sort of thing it is, so the rule cannot ask which nodes
        // are obliged to cite. What it can ask is that every path down the tree ends somewhere
        // real: a leaf either cites for itself or sits under something that does. A heading
        // needs no citation because the requirement lands on what hangs below it, and a
        // childless node with none is not a heading, whatever it was meant to be.
        void Check(NodeDraft node, string path, bool supported)
        {
            for (var r = 0; r < node.References.Count; r++)
            {
                CheckReference(node, node.References[r], $"{path}/references/{r}");
            }

            // Support inherits down a branch, so a child of a cited node need not re-cite.
            var grounded = supported || node.References.Count > 0;

            if (node.Children.Count == 0)
            {
                if (!grounded)
                {
                    failures.Add(new GroundingFailure()
                    {
                        Path = $"{path}/references",
                        Reason = "branch_without_citation",
                        Message = $"Nothing cites \"{Trim(node.Text)}\", it has nothing under it, "
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

        void CheckReference(NodeDraft node, ReferenceDraft reference, string path)
        {
            if (!responses.TryGetValue(reference.ResponseId, out var response))
            {
                failures.Add(new GroundingFailure()
                {
                    Path = $"{path}/responseId",
                    Reason = "unknown_response",
                    Message = $"\"{Trim(node.Text)}\" cites response {reference.ResponseId}, which "
                        + "is not a live response on this survey.",
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
                Message = $"\"{Trim(node.Text)}\" quotes \"{Trim(reference.Quote)}\", which does "
                    + $"not occur in response {reference.ResponseId}. Quotes must be copied "
                    + "exactly from the response text.",
                Nearest = mismatch.Nearest,
                Detail = mismatch.Detail,
            });
        }
    }

    private SummaryGroundingException Reject(
        Survey survey,
        string reason,
        string path,
        string message
    )
    {
        return this.Reject(
            survey,
            [new GroundingFailure() { Path = path, Reason = reason, Message = message }]
        );
    }

    private SummaryGroundingException Reject(
        Survey survey,
        List<GroundingFailure> failures
    )
    {
        // The first reason is the one that gets tagged; the rest travel on the exception.
        var reason = failures[0].Reason;

        WhatYouSayTelemetry.SummaryRejected(survey, reason);
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
