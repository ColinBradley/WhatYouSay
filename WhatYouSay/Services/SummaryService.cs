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
        CancellationToken cancellationToken = default)
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
        CancellationToken cancellationToken = default)
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        return await db.Summaries
            .Where(s => s.SurveyId == surveyId && !s.IsDraft && s.IsPublic)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Summary>> ListAllAsync(
        Guid surveyId,
        CancellationToken cancellationToken = default)
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
        CancellationToken cancellationToken = default)
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
            throw this.Reject(survey, "unknown_summary", $"No summary {replacing} on this survey.");
        }

        if (summary is not null && !summary.IsDraft)
        {
            throw this.Reject(
                survey,
                "summary_published",
                "That summary has been published, so it is immutable. Create a new one instead.");
        }

        var now = DateTimeOffset.UtcNow;

        if (summary is null)
        {
            summary = new Summary
            {
                Id = Guid.CreateVersion7(),
                SurveyId = survey.Id,
                Body = draft.Body,
                CreatedBy = createdBy,
                CreatedAt = now,
                UpdatedAt = now
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
            var topic = new SummaryTopic
            {
                Name = topicDraft.Name,
                Description = topicDraft.Description
            };

            foreach (var pointDraft in topicDraft.Points)
            {
                var point = new SummaryTopicPoint
                {
                    Description = pointDraft.Description,
                    Sentiment = pointDraft.Sentiment,
                    Objectivity = pointDraft.Objectivity
                };

                foreach (var referenceDraft in pointDraft.References)
                {
                    var response = responses[referenceDraft.ResponseId];
                    var location = QuoteLocator.Locate(response.Body, referenceDraft.Quote)!.Value;

                    point.References.Add(new SummaryTopicPointResponseReference
                    {
                        ResponseId = response.Id,
                        Quote = referenceDraft.Quote,
                        StartIndex = location.StartIndex,
                        EndIndex = location.EndIndex,
                        Intensity = referenceDraft.Intensity
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
        CancellationToken cancellationToken)
    {
        if (survey.IsAcceptingResponses)
        {
            throw this.Reject(
                survey,
                "survey_open",
                "This survey is still accepting responses. Summarising a moving target "
                + "produces quotes that stop matching, so close the survey first.");
        }

        if (draft.Topics.Count == 0)
        {
            throw this.Reject(survey, "empty_summary", "A summary needs at least one topic.");
        }

        var responses = await db.Responses
            .Where(r => r.SurveyId == survey.Id && !r.IsDeleted)
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        foreach (var topic in draft.Topics)
        {
            if (topic.Points.Count == 0)
            {
                throw this.Reject(
                    survey,
                    "empty_topic",
                    $"Topic \"{topic.Name}\" has no points. Drop it or give it one.");
            }

            foreach (var point in topic.Points)
            {
                if (point.References.Count == 0)
                {
                    throw this.Reject(
                        survey,
                        "point_without_citation",
                        $"Point \"{Trim(point.Description)}\" cites nothing. Every point must "
                        + "quote at least one response.");
                }

                foreach (var reference in point.References)
                {
                    if (!responses.TryGetValue(reference.ResponseId, out var response))
                    {
                        throw this.Reject(
                            survey,
                            "unknown_response",
                            $"Point \"{Trim(point.Description)}\" cites response "
                            + $"{reference.ResponseId}, which is not a live response on this survey.");
                    }

                    if (QuoteLocator.Locate(response.Body, reference.Quote) is null)
                    {
                        throw this.Reject(
                            survey,
                            "quote_not_found",
                            $"Point \"{Trim(point.Description)}\" quotes \"{Trim(reference.Quote)}\", "
                            + $"which does not occur in response {reference.ResponseId}. Quotes must "
                            + "be copied exactly from the response text.");
                    }
                }
            }
        }

        return responses;
    }

    private SummaryGroundingException Reject(Survey survey, string reason, string message)
    {
        WhatYouSayTelemetry.SummaryRejected(survey, reason);
        Activity.Current.RecordFailure(reason);

        return new SummaryGroundingException(reason, message);
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
