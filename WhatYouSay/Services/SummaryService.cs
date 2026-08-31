using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;
using System.Diagnostics;
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

        return await this.Detailed()
            .Where(s => s.SurveyId == surveyId && !s.IsDraft && s.IsPublic)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Summary?> FindAsync(Guid summaryId, CancellationToken cancellationToken = default)
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await this.Detailed().FirstOrDefaultAsync(s => s.Id == summaryId, cancellationToken);
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
                .Include(s => s.Topics)
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
            db.SummaryTopics.RemoveRange(summary.Topics);
            summary.Topics.Clear();
        }

        foreach (var topicDraft in draft.Topics)
        {
            var topic = new SummaryTopic()
            {
                Name = topicDraft.Name,
                Description = topicDraft.Description,
            };

            foreach (var pointDraft in topicDraft.Points)
            {
                var point = new SummaryTopicPoint()
                {
                    Description = pointDraft.Description,
                    Sentiment = pointDraft.Sentiment,
                    Objectivity = pointDraft.Objectivity,
                };

                foreach (var referenceDraft in pointDraft.References)
                {
                    var response = responses[referenceDraft.ResponseId];
                    var location = QuoteLocator.Locate(response.Body, referenceDraft.Quote)!.Value;

                    point.References.Add(new SummaryTopicPointResponseReference()
                    {
                        ResponseId = response.Id,
                        Quote = referenceDraft.Quote,
                        StartIndex = location.StartIndex,
                        EndIndex = location.EndIndex,
                        Intensity = referenceDraft.Intensity,
                    });
                }

                topic.Points.Add(point);
            }

            summary.Topics.Add(topic);
        }

        await db.SaveChangesAsync(cancellationToken);

        WhatYouSayTelemetry.SummaryDrafted(survey);

        return summary;
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

        if (draft.Topics.Count == 0)
        {
            throw this.Reject(survey, "empty_summary", "/topics", "A summary needs at least one topic.");
        }

        var responses = await db.Responses
            .Where(r => r.SurveyId == survey.Id && !r.IsDeleted)
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        // Every problem is collected rather than thrown on, so one retry can fix the lot.
        var failures = new List<GroundingFailure>();

        for (var t = 0; t < draft.Topics.Count; t++)
        {
            var topic = draft.Topics[t];

            if (topic.Points.Count == 0)
            {
                failures.Add(new GroundingFailure()
                {
                    Path = $"/topics/{t}/points",
                    Reason = "empty_topic",
                    Message = $"Topic \"{topic.Name}\" has no points. Drop it or give it one.",
                });

                continue;
            }

            for (var p = 0; p < topic.Points.Count; p++)
            {
                var point = topic.Points[p];

                if (point.References.Count == 0)
                {
                    failures.Add(new GroundingFailure()
                    {
                        Path = $"/topics/{t}/points/{p}/references",
                        Reason = "point_without_citation",
                        Message = $"Point \"{Trim(point.Description)}\" cites nothing. Every point "
                            + "must quote at least one response.",
                    });

                    continue;
                }

                for (var r = 0; r < point.References.Count; r++)
                {
                    var path = $"/topics/{t}/points/{p}/references/{r}";
                    var reference = point.References[r];

                    if (!responses.TryGetValue(reference.ResponseId, out var response))
                    {
                        failures.Add(new GroundingFailure()
                        {
                            Path = $"{path}/responseId",
                            Reason = "unknown_response",
                            Message = $"Point \"{Trim(point.Description)}\" cites response "
                                + $"{reference.ResponseId}, which is not a live response on this survey.",
                        });

                        continue;
                    }

                    if (QuoteLocator.Locate(response.Body, reference.Quote) is not null)
                    {
                        continue;
                    }

                    var mismatch = QuoteLocator.Diagnose(response.Body, reference.Quote);

                    failures.Add(new GroundingFailure()
                    {
                        Path = $"{path}/quote",
                        Reason = "quote_not_found",
                        Message = $"Point \"{Trim(point.Description)}\" quotes "
                            + $"\"{Trim(reference.Quote)}\", which does not occur in response "
                            + $"{reference.ResponseId}. Quotes must be copied exactly from the "
                            + "response text.",
                        Nearest = mismatch.Nearest,
                        Detail = mismatch.Detail,
                    });
                }
            }
        }

        if (failures.Count > 0)
        {
            throw this.Reject(survey, failures);
        }

        return responses;
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
        IReadOnlyList<GroundingFailure> failures
    )
    {
        // The first reason is the one that gets tagged; the rest travel on the exception.
        var reason = failures[0].Reason;

        WhatYouSayTelemetry.SummaryRejected(survey, reason);
        Activity.Current.RecordFailure(reason);

        return new SummaryGroundingException(reason, Explain(failures), failures);
    }

    /// <summary>One readable message covering every failure, for callers without the list.</summary>
    private static string Explain(IReadOnlyList<GroundingFailure> failures)
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

    /// <summary>
    /// Loads the whole tree. Withdrawn responses are filtered out here rather than at
    /// render time, so no caller can surface one by accident.
    /// </summary>
    private IQueryable<Summary> Detailed()
    {
        return db.Summaries
            .Include(s => s.Topics)
                .ThenInclude(t => t.Points)
                    .ThenInclude(p => p.References.Where(r => !r.Response.IsDeleted))
                        .ThenInclude(r => r.Response)
            .AsSplitQuery();
    }
}
