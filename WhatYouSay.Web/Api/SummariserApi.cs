using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Telemetry;
using WhatYouSay.Web.Telemetry;

namespace WhatYouSay.Web.Api;

/// <summary>
/// The summariser API. Survey in the path, token in the header, JSON everywhere except the
/// briefing, which is prose because a model reads it.
/// </summary>
public static class SummariserApi
{
    public static IEndpointRouteBuilder MapSummariserApi(this IEndpointRouteBuilder endpoints)
    {
        var surveys = endpoints.MapGroup("/api/surveys/{code}")
            // No browser form posts here and the token is not a cookie, so there is nothing
            // for antiforgery to protect.
            .DisableAntiforgery()
            .AddEndpointFilter(AuthenticateAsync);

        surveys.MapGet("/ai-summary-start", GetBriefingAsync);
        surveys.MapGet("/", GetSurveyAsync);
        surveys.MapGet("/responses", GetResponsesAsync);
        surveys.MapGet("/summaries", GetSummariesAsync);
        surveys.MapGet("/summaries/{summaryId:guid}", GetSummaryAsync);
        surveys.MapGet("/summaries/{summaryId:guid}/reactions", GetReactionsAsync);
        surveys.MapPost("/summaries", CreateSummaryAsync);
        surveys.MapPut("/summaries/{summaryId:guid}", UpdateSummaryAsync);

        return endpoints;
    }

    private static async ValueTask<object?> AuthenticateAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        using var activity = WebTelemetry.Source.Start();

        var http = context.HttpContext;

        // Status code pages would re-execute an API refusal into the Razor not-found page,
        // which answers a JSON request with HTML.
        if (http.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
        {
            statusCodePages.Enabled = false;
        }

        var session = http.RequestServices.GetRequiredService<SummariserSession>();
        var code = http.Request.RouteValues["code"] as string ?? string.Empty;

        var refusal = await session.AuthenticateAsync(http.Request, code, http.RequestAborted);

        if (refusal == SummariserRefusal.None)
        {
            activity.SetSurvey(session.Survey);

            return await next(context);
        }

        var reason = Reason(refusal);

        activity.RecordFailure(reason);
        WebTelemetry.RequestRefused(reason);

        return Refuse(refusal, code, session.ScopedCode);
    }

    private static string Reason(SummariserRefusal refusal) =>
        refusal switch
        {
            SummariserRefusal.MissingToken => "missing_token",
            SummariserRefusal.UnknownToken => "unknown_token",
            _ => "wrong_survey",
        };

    private static ProblemHttpResult Refuse(SummariserRefusal refusal, string code, string? scopedCode) =>
        refusal switch
        {
            SummariserRefusal.MissingToken => TypedResults.Problem(
                title: "No summariser token",
                detail: "Send the survey's summariser token as an Authorization header, "
                    + "in the form: Bearer <token>",
                statusCode: StatusCodes.Status401Unauthorized
            ),
            SummariserRefusal.UnknownToken => TypedResults.Problem(
                title: "Unknown summariser token",
                detail: "That token does not match any survey. It may have been regenerated.",
                statusCode: StatusCodes.Status401Unauthorized
            ),
            _ => TypedResults.Problem(
                title: "Token is for a different survey",
                detail: $"This token is scoped to survey {scopedCode}, not {code}.",
                statusCode: StatusCodes.Status403Forbidden
            ),
        };

    private static async Task<ContentHttpResult> GetBriefingAsync(
        SummariserSession session,
        WhatYouSayContext db,
        HttpContext http,
        CancellationToken cancellationToken
    )
    {
        var survey = session.Survey;

        using var activity = WebTelemetry.Source.Start().SetSurvey(survey);

        var counts = await CountAsync(db, survey.Id, cancellationToken);
        var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";

        WebTelemetry.BriefingServed(survey);

        return TypedResults.Text(
            SummariserBriefing.For(survey, baseUrl, counts.Responses, counts.Summaries)
        );
    }

    private static async Task<Ok<SurveyInfo>> GetSurveyAsync(
        SummariserSession session,
        WhatYouSayContext db,
        CancellationToken cancellationToken
    )
    {
        var survey = session.Survey;

        using var activity = WebTelemetry.Source.Start().SetSurvey(survey);

        var counts = await CountAsync(db, survey.Id, cancellationToken);

        WebTelemetry.RequestServed(survey, "get_survey");

        return TypedResults.Ok(new SurveyInfo()
        {
            Code = survey.Code,
            Title = survey.Title,
            Prompt = survey.Description,
            ResponseIdentity = survey.ResponseIdentity.ToString(),
            IsAcceptingResponses = survey.IsAcceptingResponses,
            AreResponsesPublic = survey.AreResponsesPublic,
            ResponseCount = counts.Responses,
            SummaryCount = counts.Summaries,
        });
    }

    private static async Task<Ok<IReadOnlyList<ResponseInfo>>> GetResponsesAsync(
        SummariserSession session,
        ResponseService responses,
        CancellationToken cancellationToken
    )
    {
        var survey = session.Survey;

        using var activity = WebTelemetry.Source.Start().SetSurvey(survey);

        var live = await responses.ListAsync(survey, cancellationToken);

        IReadOnlyList<ResponseInfo> result =
        [
            .. live.Select(r => new ResponseInfo()
            {
                Id = r.Id,
                Body = r.Body,
                Author = r.Author,
                CreatedAt = r.CreatedAt,
            }),
        ];

        activity?.SetTag("response.count", result.Count);
        WebTelemetry.RequestServed(survey, "list_responses");

        return TypedResults.Ok(result);
    }

    private static async Task<Ok<IReadOnlyList<SummaryInfo>>> GetSummariesAsync(
        SummariserSession session,
        SummaryService summaries,
        CancellationToken cancellationToken
    )
    {
        var survey = session.Survey;

        using var activity = WebTelemetry.Source.Start().SetSurvey(survey);

        var all = await summaries.ListAllAsync(survey.Id, cancellationToken);
        var result = new List<SummaryInfo>();

        foreach (var summary in all)
        {
            var detailed = await summaries.FindAsync(summary.Id, cancellationToken);

            result.Add(Describe(detailed!));
        }

        WebTelemetry.RequestServed(survey, "list_summaries");

        return TypedResults.Ok<IReadOnlyList<SummaryInfo>>(result);
    }

    private static async Task<Results<Ok<SummaryDetail>, ProblemHttpResult>> GetSummaryAsync(
        SummariserSession session,
        SummaryService summaries,
        Guid summaryId,
        CancellationToken cancellationToken
    )
    {
        var survey = session.Survey;

        using var activity = WebTelemetry.Source.Start().SetSurvey(survey);

        var summary = await FindAsync(session, summaries, summaryId, cancellationToken);

        if (summary is null)
        {
            activity.RecordFailure("unknown_summary");

            return NoSuchSummary(summaryId);
        }

        WebTelemetry.RequestServed(survey, "get_summary");

        return TypedResults.Ok(new SummaryDetail()
        {
            Info = Describe(summary),
            Body = summary.Body,
            Topics =
            [
                .. summary.Topics.Select(topic => new TopicDetail()
                {
                    Name = topic.Name,
                    Description = topic.Description,
                    Points =
                    [
                        .. topic.Points.Select(point => new PointDetail()
                        {
                            Id = point.Id,
                            Description = point.Description,
                            Sentiment = point.Sentiment,
                            Objectivity = point.Objectivity,
                            References =
                            [
                                .. point.References.Select(reference => new ReferenceDetail()
                                {
                                    ResponseId = reference.ResponseId,
                                    Quote = reference.Quote,
                                    Intensity = reference.Intensity,
                                }),
                            ],
                        }),
                    ],
                }),
            ],
        });
    }

    private static async Task<Results<Ok<IReadOnlyList<ReactionInfo>>, ProblemHttpResult>> GetReactionsAsync(
        SummariserSession session,
        SummaryService summaries,
        WhatYouSayContext db,
        Guid summaryId,
        CancellationToken cancellationToken
    )
    {
        var survey = session.Survey;

        using var activity = WebTelemetry.Source.Start().SetSurvey(survey);

        var summary = await FindAsync(session, summaries, summaryId, cancellationToken);

        if (summary is null)
        {
            activity.RecordFailure("unknown_summary");

            return NoSuchSummary(summaryId);
        }

        var points = summary.Topics.SelectMany(t => t.Points).ToList();
        var pointIds = points.Select(p => p.Id).ToList();

        var reactions = await db.PointReactions
            .Where(r => pointIds.Contains(r.PointId))
            .ToListAsync(cancellationToken);

        IReadOnlyList<ReactionInfo> result =
        [
            .. points.Select(point =>
            {
                var mine = reactions.Where(r => r.PointId == point.Id).ToList();

                return new ReactionInfo()
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
                            .Select(r => r.Note!),
                    ],
                };
            }),
        ];

        activity?.SetTag("objection.count", result.Sum(r => r.Objections.Count));
        WebTelemetry.RequestServed(survey, "list_reactions");

        return TypedResults.Ok(result);
    }

    private static async Task<Results<Created<DraftResult>, ProblemHttpResult>> CreateSummaryAsync(
        SummariserSession session,
        SummaryService summaries,
        SummaryDraft summary,
        CancellationToken cancellationToken
    )
    {
        var survey = session.Survey;

        using var activity = WebTelemetry.Source.Start().SetSurvey(survey);

        try
        {
            var saved = await summaries.SaveDraftAsync(
                survey,
                summary,
                "agent",
                null,
                cancellationToken
            );

            WebTelemetry.RequestServed(survey, "create_summary");

            return TypedResults.Created(
                $"/api/surveys/{survey.Code}/summaries/{saved.Id}",
                Result(survey, saved)
            );
        }
        catch (SummaryGroundingException rejection)
        {
            // SaveDraftAsync has already counted and tagged the rejection.
            return Rejected(rejection);
        }
    }

    private static async Task<Results<Ok<DraftResult>, ProblemHttpResult>> UpdateSummaryAsync(
        SummariserSession session,
        SummaryService summaries,
        Guid summaryId,
        SummaryDraft summary,
        CancellationToken cancellationToken
    )
    {
        var survey = session.Survey;

        using var activity = WebTelemetry.Source.Start().SetSurvey(survey);

        if (await FindAsync(session, summaries, summaryId, cancellationToken) is null)
        {
            activity.RecordFailure("unknown_summary");

            return NoSuchSummary(summaryId);
        }

        try
        {
            var saved = await summaries.SaveDraftAsync(
                survey,
                summary,
                "agent",
                summaryId,
                cancellationToken
            );

            WebTelemetry.RequestServed(survey, "update_summary");

            return TypedResults.Ok(Result(survey, saved));
        }
        catch (SummaryGroundingException rejection)
        {
            return Rejected(rejection);
        }
    }

    /// <summary>
    /// A rejection has to be as useful as the rules are strict, so every failure travels
    /// back at once, each located by a JSON Pointer into what was sent.
    /// </summary>
    private static ProblemHttpResult Rejected(SummaryGroundingException rejection)
    {
        var status = rejection.Reason switch
        {
            "survey_open" or "summary_published" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity,
        };

        return TypedResults.Problem(
            title: "The draft was not saved",
            detail: rejection.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?>()
            {
                ["reason"] = rejection.Reason,
                ["errors"] = rejection.Failures,
            }
        );
    }

    private static ProblemHttpResult NoSuchSummary(Guid summaryId) =>
        TypedResults.Problem(
            title: "No such summary",
            detail: $"No summary {summaryId} on this survey.",
            statusCode: StatusCodes.Status404NotFound
        );

    private static async Task<Summary?> FindAsync(
        SummariserSession session,
        SummaryService summaries,
        Guid summaryId,
        CancellationToken cancellationToken
    )
    {
        var summary = await summaries.FindAsync(summaryId, cancellationToken);

        return summary is null || summary.SurveyId != session.Survey.Id ? null : summary;
    }

    private static async Task<(int Responses, int Summaries)> CountAsync(
        WhatYouSayContext db,
        Guid surveyId,
        CancellationToken cancellationToken
    )
    {
        var responses = await db.Responses.CountAsync(
            r => r.SurveyId == surveyId && !r.IsDeleted,
            cancellationToken
        );

        var summaries = await db.Summaries.CountAsync(
            s => s.SurveyId == surveyId,
            cancellationToken
        );

        return (responses, summaries);
    }

    private static DraftResult Result(Survey survey, Summary summary)
    {
        var points = summary.Topics.SelectMany(t => t.Points).ToList();

        return new DraftResult()
        {
            SummaryId = summary.Id,
            EditUrl = $"/surveys/{survey.Code}/admin/summaries/{summary.Id}",
            TopicCount = summary.Topics.Count,
            PointCount = points.Count,
            ReferenceCount = points.Sum(p => p.References.Count),
        };
    }

    private static SummaryInfo Describe(Summary summary)
    {
        return new SummaryInfo()
        {
            Id = summary.Id,
            CreatedAt = summary.CreatedAt,
            IsDraft = summary.IsDraft,
            IsPublic = summary.IsPublic,
            CreatedBy = summary.CreatedBy,
            TopicCount = summary.Topics.Count,
            PointCount = summary.Topics.Sum(t => t.Points.Count),
        };
    }
}
