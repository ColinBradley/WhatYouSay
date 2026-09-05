using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
        topics.MapGet("/summaries/{summaryId:guid}/comments", GetCommentsAsync);
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

    /// <summary>
    /// Plain skip/take, no cursor: only a frozen response can be cited, so one arriving
    /// mid-page can shift the window but cannot change anything already read.
    /// </summary>
    private static async Task<Ok<ResponsePage>> GetResponsesAsync(
        SummariserSession session,
        ResponseService responses,
        CancellationToken cancellationToken,
        int skip = 0,
        int take = 200
    )
    {
        var topic = session.Topic;

        using var activity = WebTelemetry.Source.Start().SetTopic(topic);

        var live = await responses.ListAsync(topic, cancellationToken);
        var from = Math.Clamp(skip, 0, live.Count);
        var count = Math.Clamp(take, 0, live.Count - from);

        var page = new ResponsePage()
        {
            Total = live.Count,
            Skip = from,
            Items =
            [
                .. live.Skip(from).Take(count).Select(r => new ResponseInfo()
                {
                    Id = r.Id,
                    Body = r.Body,
                    Author = r.Author,
                    CreatedAt = r.CreatedAt,
                }),
            ],
        };

        activity?.SetTag("response.count", page.Items.Count);
        activity?.SetTag("response.total", page.Total);
        WebTelemetry.RequestServed(topic, "list_responses");

        return TypedResults.Ok(page);
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

    private static async Task<Results<Ok<IReadOnlyList<CommentInfo>>, ProblemHttpResult>> GetCommentsAsync(
        SummariserSession session,
        SummaryService summaries,
        CommentService comments,
        Guid summaryId,
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

        // Hidden comments are not served: they were closed on purpose, and an agent
        // answering a settled objection would reopen it.
        var visible = await comments.ListAsync(topic, summaryId, null, false, cancellationToken);

        IReadOnlyList<CommentInfo> result =
        [
            .. visible.Select(comment => new CommentInfo()
            {
                NodeId = comment.NodeId,
                NodeText = comment.NodeText,
                Body = comment.Body,
                Author = comment.Author,
                ResponseBody = comment.ResponseBody,
            }),
        ];

        activity?.SetTag("comment.count", result.Count);
        WebTelemetry.RequestServed(topic, "list_comments");

        return TypedResults.Ok(result);
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
                    Counts = mine
                        .GroupBy(r => r.Kind)
                        .ToDictionary(group => group.Key.ToString(), group => group.Count()),
                };
            }),
        ];

        activity?.SetTag("reaction.count", result.Sum(r => r.Counts.Values.Sum()));
        WebTelemetry.RequestServed(topic, "list_reactions");

        return TypedResults.Ok(result);
    }

    private static async Task<Results<Created<DraftResult>, ProblemHttpResult>> CreateSummaryAsync(
        SummariserSession session,
        SummaryService summaries,
        HttpRequest request,
        CancellationToken cancellationToken
    )
    {
        var topic = session.Topic;

        using var activity = WebTelemetry.Source.Start().SetTopic(topic);

        var (summary, malformed) = await ReadDraftAsync(request, cancellationToken);

        if (malformed is not null)
        {
            activity.RecordFailure("malformed_payload");

            return malformed;
        }

        try
        {
            var saved = await summaries.SaveDraftAsync(
                topic,
                summary!,
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
        HttpRequest request,
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

        var (summary, malformed) = await ReadDraftAsync(request, cancellationToken);

        if (malformed is not null)
        {
            activity.RecordFailure("malformed_payload");

            return malformed;
        }

        try
        {
            var saved = await summaries.SaveDraftAsync(
                topic,
                summary!,
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
    /// Reads the body here rather than letting the framework bind it, so a payload the
    /// reader cannot make sense of gets the located answer every other mistake gets. Binding
    /// happens before a handler runs, so a missing field or a stray comma would otherwise
    /// come back as a bare 400 with no body outside Development — the one class of mistake
    /// the API refused to explain, in the surface whose whole job is explaining itself.
    /// </summary>
    private static async Task<(SummaryDraft? Draft, ProblemHttpResult? Malformed)> ReadDraftAsync(
        HttpRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var draft = await request.ReadFromJsonAsync<SummaryDraft>(cancellationToken);

            return draft is null
                ? (null, Malformed("/", "The request body was empty. Send the whole summary."))
                : (draft, null);
        }
        catch (JsonException failure)
        {
            return (null, Malformed(Pointer(failure.Path), Explain(failure.Message)));
        }
    }

    /// <summary>
    /// Trims the reader's own message down to the part addressed to whoever sent the JSON.
    /// The tail repeats the path we have already converted, and "change the reader options"
    /// is advice for this codebase that an agent would otherwise try to act on.
    /// </summary>
    private static string Explain(string message)
    {
        var end = message.IndexOf(" Path:", StringComparison.Ordinal);
        var trimmed = end < 0 ? message : message[..end];

        return trimmed.Replace(" Change the reader options.", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// System.Text.Json locates a fault as <c>$.nodes[0].text</c>; every other failure this
    /// API reports is a JSON Pointer. One shape, so a caller can act on the path without
    /// knowing which layer rejected it.
    /// </summary>
    private static string Pointer(string? path)
    {
        if (path is null or "$")
        {
            return "/";
        }

        return path[1..].Replace("[", "/").Replace("]", string.Empty).Replace(".", "/");
    }

    private static ProblemHttpResult Malformed(string path, string? message)
    {
        var detail = message ?? "The request body is not valid JSON.";

        return TypedResults.Problem(
            title: "The draft was not saved",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>()
            {
                ["reason"] = "malformed_payload",
                ["errors"] = new[]
                {
                    new GroundingFailure()
                    {
                        Path = path,
                        Reason = "malformed_payload",
                        Message = detail,
                    },
                },
            }
        );
    }
    /// <summary>
    /// A rejection has to be as useful as the rules are strict, so every failure travels
    /// back at once, each located by a JSON Pointer into what was sent.
    /// </summary>
    private static ProblemHttpResult Rejected(SummaryGroundingException rejection)
    {
        var status = rejection.Reason switch
        {
            "topic_open" or "summary_not_agent_editable" => StatusCodes.Status409Conflict,
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
            IsAgentEditable = summary.IsAgentEditable,
            IsPublic = summary.IsPublic,
            CreatedBy = summary.CreatedBy,
            NodeCount = summary.Nodes.Count,
            MaxDepth = SummaryTree.Depth(summary.Roots),
        };
    }
}
