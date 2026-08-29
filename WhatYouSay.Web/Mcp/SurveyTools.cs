using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WhatYouSay.Data;
using WhatYouSay.Services;

namespace WhatYouSay.Web.Mcp;

[McpServerToolType]
public static class SurveyTools
{
    [McpServerTool(Name = "get_survey")]
    [Description("The survey this token is scoped to: its prompt, settings and counts. Call this first to learn what question people were answering.")]
    public static async Task<SurveyInfo> GetSurveyAsync(
        SummariserSession session,
        WhatYouSayContext db,
        CancellationToken cancellationToken)
    {
        var survey = await session.RequireSurveyAsync(cancellationToken);

        return new SurveyInfo
        {
            Code = survey.Code,
            Title = survey.Title,
            Prompt = survey.Description,
            ResponseIdentity = survey.ResponseIdentity.ToString(),
            IsAcceptingResponses = survey.IsAcceptingResponses,
            AreResponsesPublic = survey.AreResponsesPublic,
            ResponseCount = await db.Responses.CountAsync(
                r => r.SurveyId == survey.Id && !r.IsDeleted,
                cancellationToken),
            SummaryCount = await db.Summaries.CountAsync(s => s.SurveyId == survey.Id, cancellationToken)
        };
    }

    [McpServerTool(Name = "list_responses")]
    [Description("Every live response, with the id you must cite it by. Author and timestamp are present only when the survey records them — an anonymous survey stores neither, so there is no privileged view to ask for.")]
    public static async Task<IReadOnlyList<ResponseInfo>> ListResponsesAsync(
        SummariserSession session,
        ResponseService responses,
        CancellationToken cancellationToken)
    {
        var survey = await session.RequireSurveyAsync(cancellationToken);
        var live = await responses.ListAsync(survey, cancellationToken);

        return
        [
            .. live.Select(r => new ResponseInfo
            {
                Id = r.Id,
                Body = r.Body,
                Author = r.Author,
                CreatedAt = r.CreatedAt
            })
        ];
    }

    [McpServerTool(Name = "list_summaries")]
    [Description("Every summary version on this survey, newest first. Drafts are yours to edit; published ones are immutable.")]
    public static async Task<IReadOnlyList<SummaryInfo>> ListSummariesAsync(
        SummariserSession session,
        SummaryService summaries,
        CancellationToken cancellationToken)
    {
        var survey = await session.RequireSurveyAsync(cancellationToken);
        var all = await summaries.ListAllAsync(survey.Id, cancellationToken);
        var result = new List<SummaryInfo>();

        foreach (var summary in all)
        {
            var detailed = await summaries.FindAsync(summary.Id, cancellationToken);

            result.Add(Describe(detailed!));
        }

        return result;
    }

    [McpServerTool(Name = "get_summary")]
    [Description("One summary version in full: its narrative body, topics, points, and the exact quotes each point cites.")]
    public static async Task<SummaryDetail> GetSummaryAsync(
        SummariserSession session,
        SummaryService summaries,
        [Description("Summary id, from list_summaries.")] Guid summaryId,
        CancellationToken cancellationToken)
    {
        var summary = await RequireSummaryAsync(session, summaries, summaryId, cancellationToken);

        return new SummaryDetail
        {
            Info = Describe(summary),
            Body = summary.Body,
            Topics =
            [
                .. summary.Topics.Select(topic => new TopicDetail
                {
                    Name = topic.Name,
                    Description = topic.Description,
                    Points =
                    [
                        .. topic.Points.Select(point => new PointDetail
                        {
                            Id = point.Id,
                            Description = point.Description,
                            Sentiment = point.Sentiment,
                            Objectivity = point.Objectivity,
                            References =
                            [
                                .. point.References.Select(reference => new ReferenceDetail
                                {
                                    ResponseId = reference.ResponseId,
                                    Quote = reference.Quote,
                                    Intensity = reference.Intensity
                                })
                            ]
                        })
                    ]
                })
            ]
        };
    }

    [McpServerTool(Name = "list_reactions")]
    [Description("What responders said back about a published summary: agree and important counts per point, plus every 'this misrepresents me' objection in full. Objections are the highest-value input for a second pass — fix the points people objected to rather than starting cold.")]
    public static async Task<IReadOnlyList<ReactionInfo>> ListReactionsAsync(
        SummariserSession session,
        SummaryService summaries,
        WhatYouSayContext db,
        [Description("Summary id, from list_summaries.")] Guid summaryId,
        CancellationToken cancellationToken)
    {
        var summary = await RequireSummaryAsync(session, summaries, summaryId, cancellationToken);

        var points = summary.Topics.SelectMany(t => t.Points).ToList();
        var pointIds = points.Select(p => p.Id).ToList();

        var reactions = await db.PointReactions
            .Where(r => pointIds.Contains(r.PointId))
            .ToListAsync(cancellationToken);

        return
        [
            .. points.Select(point =>
            {
                var mine = reactions.Where(r => r.PointId == point.Id).ToList();

                return new ReactionInfo
                {
                    PointId = point.Id,
                    PointDescription = point.Description,
                    Agree = mine.Count(r => r.Kind == ReactionKind.Agree),
                    Important = mine.Count(r => r.Kind == ReactionKind.Important),
                    Misrepresents = mine.Count(r => r.Kind == ReactionKind.Misrepresents),
                    Objections =
                    [
                        .. mine
                            .Where(r => r.Kind == ReactionKind.Misrepresents
                                && !string.IsNullOrWhiteSpace(r.Note))
                            .Select(r => r.Note!)
                    ]
                };
            })
        ];
    }

    [McpServerTool(Name = "create_summary")]
    [Description("""
        Creates a draft summary. The whole structure goes in one call.

        Every point must cite at least one response, and every quote must be copied
        character for character from that response's body — the app checks both and rejects
        the entire call if either fails, naming what went wrong so you can fix it and retry.
        Do not paraphrase inside a quote, and do not supply offsets: the app finds them.

        Body is a short narrative overview, two or three paragraphs of markdown. It should
        read as prose about what came back, not as a restatement of the topic list.

        Sentiment runs -1 (negative) to +1 (positive); objectivity 0 (pure opinion) to
        1 (verifiable fact); intensity 0 to 1 for how strongly a quote supports its point.
        All three are optional.

        The result is a draft. A human reviews and publishes it — you cannot.
        """)]
    public static async Task<DraftResult> CreateSummaryAsync(
        SummariserSession session,
        SummaryService summaries,
        SummaryDraft summary,
        CancellationToken cancellationToken)
    {
        return await SaveAsync(session, summaries, summary, null, cancellationToken);
    }

    [McpServerTool(Name = "update_summary")]
    [Description("Replaces the contents of a draft summary you created. Published summaries are immutable — re-run create_summary to make a new version instead. Same grounding rules as create_summary.")]
    public static async Task<DraftResult> UpdateSummaryAsync(
        SummariserSession session,
        SummaryService summaries,
        [Description("Summary id, from list_summaries. Must still be a draft.")] Guid summaryId,
        SummaryDraft summary,
        CancellationToken cancellationToken)
    {
        return await SaveAsync(session, summaries, summary, summaryId, cancellationToken);
    }

    private static async Task<Summary> RequireSummaryAsync(
        SummariserSession session,
        SummaryService summaries,
        Guid summaryId,
        CancellationToken cancellationToken)
    {
        var survey = await session.RequireSurveyAsync(cancellationToken);
        var summary = await summaries.FindAsync(summaryId, cancellationToken);

        if (summary is null || summary.SurveyId != survey.Id)
        {
            throw new McpException($"No summary {summaryId} on this survey.");
        }

        return summary;
    }

    private static async Task<DraftResult> SaveAsync(
        SummariserSession session,
        SummaryService summaries,
        SummaryDraft summary,
        Guid? replacing,
        CancellationToken cancellationToken)
    {
        var survey = await session.RequireSurveyAsync(cancellationToken);

        try
        {
            var saved = await summaries.SaveDraftAsync(survey, summary, "agent", replacing, cancellationToken);

            return Result(survey, saved);
        }
        catch (SummaryGroundingException rejection)
        {
            // The SDK hides ordinary exception detail from clients, so the rejection has
            // to be an McpException or the agent gets "an error occurred" and nothing to fix.
            throw new McpException(rejection.Message);
        }
    }

    private static DraftResult Result(Survey survey, Summary summary)
    {
        var points = summary.Topics.SelectMany(t => t.Points).ToList();

        return new DraftResult
        {
            SummaryId = summary.Id,
            EditUrl = $"/surveys/{survey.Code}/admin/summaries/{summary.Id}",
            TopicCount = summary.Topics.Count,
            PointCount = points.Count,
            ReferenceCount = points.Sum(p => p.References.Count)
        };
    }

    private static SummaryInfo Describe(Summary summary)
    {
        return new SummaryInfo
        {
            Id = summary.Id,
            CreatedAt = summary.CreatedAt,
            IsDraft = summary.IsDraft,
            IsPublic = summary.IsPublic,
            CreatedBy = summary.CreatedBy,
            TopicCount = summary.Topics.Count,
            PointCount = summary.Topics.Sum(t => t.Points.Count)
        };
    }
}
