using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using WhatYouSay.Data;
using WhatYouSay.Services;
using WhatYouSay.Telemetry;
using WhatYouSay.Web.Telemetry;

namespace WhatYouSay.Web.Api;

/// <summary>
/// The summariser API. Topic in the path, token in the header, JSON everywhere except the
/// briefing, which is prose because a model reads it.
/// </summary>
public static class SummariserApi
{
    public static IEndpointRouteBuilder MapSummariserApi(this IEndpointRouteBuilder endpoints)
    {
        var topics = endpoints.MapGroup("/api/topics/{code}")
            // No browser form posts here and the token is not a cookie, so there is nothing
            // for antiforgery to protect.
            .DisableAntiforgery()
            .AddEndpointFilter(AuthenticateAsync);

        topics.MapGet("/ai-summary-start", GetBriefingAsync);
        topics.MapGet("/", GetTopicAsync);
        topics.MapGet("/responses", GetResponsesAsync);
        topics.MapGet("/summaries", GetSummariesAsync);
        topics.MapGet("/summaries/{summaryId:guid}", GetSummaryAsync);
        topics.MapGet("/summaries/{summaryId:guid}/reactions", GetReactionsAsync);
        topics.MapPost("/summaries", CreateSummaryAsync);
        topics.MapPut("/summaries/{summaryId:guid}", UpdateSummaryAsync);

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
            activity.SetTopic(session.Topic);

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
            _ => "wrong_topic",
        };

    private static ProblemHttpResult Refuse(SummariserRefusal refusal, string code, string? scopedCode) =>
        refusal switch
        {
            SummariserRefusal.MissingToken => TypedResults.Problem(
                title: "No summariser token",
                detail: "Send the topic's summariser token as an Authorization header, "
                    + "in the form: Bearer <token>",
                statusCode: StatusCodes.Status401Unauthorized
            ),
            SummariserRefusal.UnknownToken => TypedResults.Problem(
                title: "Unknown summariser token",
                detail: "That token does not match any topic. It may have been regenerated.",
                statusCode: StatusCodes.Status401Unauthorized
            ),
            _ => TypedResults.Problem(
                title: "Token is for a different topic",
                detail: $"This token is scoped to topic {scopedCode}, not {code}.",
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
        var topic = session.Topic;

        using var activity = WebTelemetry.Source.Start().SetTopic(topic);

        var counts = await CountAsync(db, topic.Id, cancellationToken);
        var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";

        WebTelemetry.BriefingServed(topic);

        return TypedResults.Text(
            SummariserBriefing.For(topic, baseUrl, counts.Responses, counts.Summaries)
        );
    }

    private static async Task<Ok<TopicInfo>> GetTopicAsync(
        SummariserSession session,
        WhatYouSayContext db,
        CancellationToken cancellationToken
    )
    {
        var topic = session.Topic;

        using var activity = WebTelemetry.Source.Start().SetTopic(topic);

        var counts = await CountAsync(db, topic.Id, cancellationToken);

        WebTelemetry.RequestServed(topic, "get_topic");

        return TypedResults.Ok(new TopicInfo()
        {
            Code = topic.Code,
            Title = topic.Title,
            Prompt = topic.Description,
            ResponseIdentity = topic.ResponseIdentity.ToString(),
            IsAcceptingResponses = topic.IsAcceptingResponses,
            AreResponsesPublic = topic.AreResponsesPublic,
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
        var topic = session.Topic;

        using var activity = WebTelemetry.Source.Start().SetTopic(topic);

        var live = await responses.ListAsync(topic, cancellationToken);

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
        WebTelemetry.RequestServed(topic, "list_responses");

        return TypedResults.Ok(result);
    }

    private static async Task<Ok<IReadOnlyList<SummaryInfo>>> GetSummariesAsync(
        SummariserSession session,
        SummaryService summaries,
        CancellationToken cancellationToken
    )
    {
        var topic = session.Topic;

        using var activity = WebTelemetry.Source.Start().SetTopic(topic);

        var all = await summaries.ListAllAsync(topic.Id, cancellationToken);
        var result = new List<SummaryInfo>();

        foreach (var summary in all)
        {
            var detailed = await summaries.FindAsync(summary.Id, cancellationToken);

            result.Add(Describe(detailed!));
        }

        WebTelemetry.RequestServed(topic, "list_summaries");

        return TypedResults.Ok<IReadOnlyList<SummaryInfo>>(result);
    }

    private static async Task<Results<Ok<SummaryDetail>, ProblemHttpResult>> GetSummaryAsync(
        SummariserSession session,
        SummaryService summaries,
        Guid summaryId,
        CancellationToken cancellationToken
    )
    {
        var topic = session.Topic;

        using var activity = WebTelemetry.Source.Start().SetTopic(topic);

        var summary = await FindAsync(session, summaries, summaryId, cancellationToken);

        if (summary is null)
        {
            activity.RecordFailure("unknown_summary");

            return NoSuchSummary(summaryId);
        }

        WebTelemetry.RequestServed(topic, "get_summary");

        return TypedResults.Ok(new SummaryDetail()
        {
            Info = Describe(summary),
            Body = summary.Body,
            Nodes = [.. summary.Roots.Select(Describe)],
        });
    }

    /// <summary>
    /// Nested on the way out as well as in, so what an agent reads back has the same shape
    /// as what it would send.
    /// </summary>
    private static NodeDetail Describe(SummaryNode node)
    {
        return new NodeDetail()
        {
            Id = node.Id,
            Text = node.Text,
            References =
            [
                .. node.References.Select(reference => new ReferenceDetail()
                {
                    ResponseId = reference.ResponseId,
                    Quote = reference.Quote,
                }),
            ],
            Children = [.. node.Children.Select(Describe)],
        };
    }

    private static async Task<Results<Ok<IReadOnlyList<ReactionInfo>>, ProblemHttpResult>> GetReactionsAsync(
        SummariserSession session,
        SummaryService summaries,
        WhatYouSayContext db,
        Guid summaryId,
        CancellationToken cancellationToken
    )
    {
        var topic = session.Topic;

        using var activity = WebTelemetry.Source.Start().SetTopic(topic);

        var summary = await FindAsync(session, summaries, summaryId, cancellationToken);

        if (summary is null)
        {
            activity.RecordFailure("unknown_summary");

            return NoSuchSummary(summaryId);
        }

        // Flat, in reading order: a second pass wants the nodes people objected to, and the
        // tree structure it would need to fix them comes from GET /summaries/{id}.
        var nodes = SummaryTree.Flatten(summary.Roots).ToList();

        var reactions = await db.NodeReactions
            .Where(r => r.Node.SummaryId == summary.Id)
            .ToListAsync(cancellationToken);

        IReadOnlyList<ReactionInfo> result =
        [
            .. nodes.Select(node =>
            {
                var mine = reactions.Where(r => r.NodeId == node.Id).ToList();

                return new ReactionInfo()
                {
                    NodeId = node.Id,
                    NodeText = node.Text,
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
        WebTelemetry.RequestServed(topic, "list_reactions");

        return TypedResults.Ok(result);
    }

    private static async Task<Results<Created<DraftResult>, ProblemHttpResult>> CreateSummaryAsync(
        SummariserSession session,
        SummaryService summaries,
        SummaryDraft summary,
        CancellationToken cancellationToken
    )
    {
        var topic = session.Topic;

        using var activity = WebTelemetry.Source.Start().SetTopic(topic);

        try
        {
            var saved = await summaries.SaveDraftAsync(
                topic,
                summary,
                "agent",
                null,
                cancellationToken
            );

            WebTelemetry.RequestServed(topic, "create_summary");

            return TypedResults.Created(
                $"/api/topics/{topic.Code}/summaries/{saved.Id}",
                Result(topic, saved)
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
        var topic = session.Topic;

        using var activity = WebTelemetry.Source.Start().SetTopic(topic);

        if (await FindAsync(session, summaries, summaryId, cancellationToken) is null)
        {
            activity.RecordFailure("unknown_summary");

            return NoSuchSummary(summaryId);
        }

        try
        {
            var saved = await summaries.SaveDraftAsync(
                topic,
                summary,
                "agent",
                summaryId,
                cancellationToken
            );

            WebTelemetry.RequestServed(topic, "update_summary");

            return TypedResults.Ok(Result(topic, saved));
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
            "topic_open" or "summary_published" => StatusCodes.Status409Conflict,
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
            detail: $"No summary {summaryId} on this topic.",
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

        return summary is null || summary.TopicId != session.Topic.Id ? null : summary;
    }

    private static async Task<(int Responses, int Summaries)> CountAsync(
        WhatYouSayContext db,
        Guid topicId,
        CancellationToken cancellationToken
    )
    {
        var responses = await db.Responses.CountAsync(
            r => r.TopicId == topicId && !r.IsDeleted,
            cancellationToken
        );

        var summaries = await db.Summaries.CountAsync(
            s => s.TopicId == topicId,
            cancellationToken
        );

        return (responses, summaries);
    }

    private static DraftResult Result(Topic topic, Summary summary)
    {
        return new DraftResult()
        {
            SummaryId = summary.Id,
            EditUrl = $"/topics/{topic.Code}/admin/summaries/{summary.Id}",
            NodeCount = summary.Nodes.Count,
            MaxDepth = SummaryTree.Depth(summary.Roots),
            ReferenceCount = summary.Nodes.Sum(n => n.References.Count),
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
            NodeCount = summary.Nodes.Count,
            MaxDepth = SummaryTree.Depth(summary.Roots),
        };
    }
}
