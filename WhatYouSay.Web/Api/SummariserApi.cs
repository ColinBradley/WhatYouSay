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
        topics.MapGet("/live", GetLiveAsync);
        topics.MapPut("/live", PutLiveAsync);
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
        SummaryService summaries,
        HttpContext http,
        CancellationToken cancellationToken
    )
    {
        var topic = session.Topic;

        using var activity = WebTelemetry.Source.Start().SetTopic(topic);

        var counts = await CountAsync(db, topic.Id, cancellationToken);
        var live = await summaries.FindLiveAsync(topic.Id, cancellationToken);
        var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";

        WebTelemetry.BriefingServed(topic);

        return TypedResults.Text(
            SummariserBriefing.For(topic, baseUrl, counts.Responses, counts.Summaries, live?.Id)
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
            ResponseTiming = topic.ResponseTiming.ToString(),
            IsAcceptingResponses = topic.IsAcceptingResponses,
            AreResponsesPublic = topic.AreResponsesPublic,
            ResponseCount = counts.Responses,
            SummaryCount = counts.Summaries,
            InputRevision = topic.InputRevision,
        });
    }

    /// <summary>
    /// The whole summariser input in one consistent read, with an ETag. A request whose
    /// If-None-Match holds the current ETag gets 304 and no body, so an idle poll costs two
    /// small queries.
    /// </summary>
    private static async Task<Results<Ok<LiveSnapshot>, StatusCodeHttpResult>> GetLiveAsync(
        SummariserSession session,
        SummariserInputService inputs,
        HttpContext http,
        CancellationToken cancellationToken
    )
    {
        var topic = session.Topic;

        using var activity = WebTelemetry.Source.Start().SetTopic(topic);

        var read = await inputs.ReadAsync(topic, KnownVersions(http.Request), cancellationToken);

        http.Response.Headers.ETag = $"\"{ETagPrefix}{read.Version}\"";

        if (read.Input is not { } input)
        {
            WebTelemetry.RequestServed(topic, "get_live_unchanged");

            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        WebTelemetry.RequestServed(topic, "get_live");

        return TypedResults.Ok(Snapshot(input));
    }

    /// <summary>Starts every ETag, so a tag from an older contract never matches.</summary>
    private static string ETagPrefix =>
        $"v{SummariserModel.ContractVersion}.";

    /// <summary>The versions named by the request's If-None-Match, ignoring tags this API did not issue.</summary>
    private static List<string> KnownVersions(HttpRequest request)
    {
        var prefix = $"\"{ETagPrefix}";

        return
        [
            .. request.GetTypedHeaders().IfNoneMatch
                .Select(tag => tag.Tag.Value)
                .OfType<string>()
                .Where(tag => tag.StartsWith(prefix, StringComparison.Ordinal) && tag.EndsWith('"'))
                .Select(tag => tag[prefix.Length..^1]),
        ];
    }

    private static LiveSnapshot Snapshot(SummariserInput input)
    {
        var topic = input.Topic;
        var summary = input.LiveSummary;
        var comments = input.Comments.ToLookup(comment => comment.NodeId);

        return new LiveSnapshot()
        {
            InputRevision = topic.InputRevision,
            IsPending = input.IsPending,
            IsWritable = input.IsWritable,
            NotWritableReason = (input.IsWritable, topic.SummaryPolicy) switch
            {
                (true, _) => null,
                (_, SummaryPolicy.Live) => "paused",
                _ => "reviewed",
            },
            LiveSummary = summary is null
                ? null
                : new LiveSummaryState()
                {
                    Id = summary.Id,
                    Revision = summary.Revision,
                    AppliedInputRevision = summary.AppliedInputRevision,
                },
            Input = new LiveInput()
            {
                Topic = new LiveTopic()
                {
                    Title = topic.Title,
                    Question = topic.Description,
                    ResponseIdentity = topic.ResponseIdentity.ToString(),
                    ResponseTiming = topic.ResponseTiming.ToString(),
                },
                Guidance = new LiveGuidance()
                {
                    ExpectedFeedback = topic.ExpectedFeedback,
                    Instructions = topic.SummarisingInstructions,
                    LengthTarget = topic.SummaryLengthTarget,
                },
                Responses =
                [
                    .. input.Responses.Select(response => new LiveResponse()
                    {
                        Id = response.Id,
                        Revision = response.RevisionNumber,
                        Body = response.Current.Body,
                        Author = response.Author,
                        SubmittedAt = response.SubmittedAt,
                        IsEdited = response.IsEdited,
                        Reactions = Counts(input.ResponseReactions.GetValueOrDefault(response.Id)),
                    }),
                ],
                Summary = summary is null
                    ? null
                    : new LiveSummaryContent()
                    {
                        Body = summary.Body,
                        Nodes = [.. summary.Roots.Select(Node)],
                    },
            },
            Model = new ModelBrief()
            {
                Instructions = SummariserModel.Instructions,
                OutputSchema = SummariserModel.OutputSchema,
            },
        };

        LiveNode Node(SummaryNode node)
        {
            return new LiveNode()
            {
                Id = node.Id,
                Text = node.Text,
                References = [.. node.References.Select(Describe)],
                Reactions = Counts(input.NodeReactions.GetValueOrDefault(node.Id)),
                Comments =
                [
                    .. comments[node.Id].Select(comment => new LiveComment()
                    {
                        Author = comment.Author,
                        Body = comment.Body,
                        CommenterResponseIds = [.. comment.CommenterResponses.Select(r => r.ResponseId)],
                    }),
                ],
                Children = [.. node.Children.Select(Node)],
            };
        }
    }

    /// <summary>Counts by kind name, in the order the kinds are declared. Kinds nobody used are left out.</summary>
    private static Dictionary<string, int> Counts(ReactionTally? tally)
    {
        return (tally ?? ReactionTally.None).Counts
            .OrderBy(entry => entry.Key)
            .ToDictionary(entry => entry.Key.ToString(), entry => entry.Value);
    }

    /// <summary>
    /// Creates or revises the topic's live summary. Takes a draft with <c>inputRevision</c>,
    /// a revision on every reference, and <c>basedOnRevision</c> set to null only to create it.
    /// </summary>
    private static async Task<Results<Ok<LiveWriteResult>, Created<LiveWriteResult>, ProblemHttpResult>> PutLiveAsync(
        SummariserSession session,
        SummaryService summaries,
        WhatYouSayContext db,
        HttpRequest request,
        CancellationToken cancellationToken
    )
    {
        var topic = session.Topic;

        using var activity = WebTelemetry.Source.Start().SetTopic(topic);

        var (draft, malformed) = await ReadDraftAsync(request, cancellationToken);

        if (malformed is not null)
        {
            activity.RecordFailure("malformed_payload");

            return malformed;
        }

        Summary saved;

        try
        {
            saved = await summaries.SaveLiveAsync(topic, draft!, cancellationToken);
        }
        catch (SummaryGroundingException rejection)
        {
            return Rejected(rejection);
        }

        var current = await db.Topics
            .Where(t => t.Id == topic.Id)
            .Select(t => t.InputRevision)
            .SingleAsync(cancellationToken);

        // SaveLiveAsync refuses a draft without an input revision.
        var applied = saved.AppliedInputRevision!.Value;

        var result = new LiveWriteResult()
        {
            SummaryId = saved.Id,
            Revision = saved.Revision,
            AppliedInputRevision = applied,
            InputRevision = current,
            IsPending = applied < current,
            NodeCount = saved.Nodes.Count,
            MaxDepth = SummaryTree.Depth(saved.Roots),
            ReferenceCount = saved.Nodes.Sum(n => n.References.Count),
        };

        WebTelemetry.RequestServed(topic, "write_live");

        // A write without basedOnRevision is only accepted when it creates the live summary.
        return draft!.BasedOnRevision is null
            ? TypedResults.Created($"/api/topics/{topic.Code}/live", result)
            : TypedResults.Ok(result);
    }

    /// <summary>
    /// Pages through the live responses in listing order. Paging with <paramref name="after"/>
    /// keeps a page in place when responses arrive at the front of a newest-first list.
    /// A citation is located in the revision it names when it is written, so a quote of
    /// wording that has since changed is never stored with wrong offsets.
    /// </summary>
    private static async Task<Results<Ok<ResponsePage>, ProblemHttpResult>> GetResponsesAsync(
        SummariserSession session,
        SummariserInputService inputs,
        CancellationToken cancellationToken,
        int skip = 0,
        int take = 200,
        Guid? after = null
    )
    {
        var topic = session.Topic;

        using var activity = WebTelemetry.Source.Start().SetTopic(topic);

        var listing = await inputs.ListResponsesAsync(topic, cancellationToken);
        var live = listing.Responses;
        var from = Math.Clamp(skip, 0, live.Count);

        if (after is { } cursor)
        {
            var position = IndexOf(live, cursor);

            if (position < 0)
            {
                activity.RecordFailure("unknown_cursor");

                return TypedResults.Problem(
                    title: "No such response to page after",
                    detail: $"Response {cursor} is not a live response on this topic any more. It may "
                        + "have been withdrawn or hidden. Start again from the first page.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?>() { ["reason"] = "unknown_cursor" });
            }

            from = position + 1;
        }

        var count = Math.Clamp(take, 0, live.Count - from);
        var items = live.Skip(from).Take(count).ToList();

        var page = new ResponsePage()
        {
            Total = live.Count,
            Skip = from,
            InputRevision = listing.InputRevision,
            NextAfter = from + count < live.Count && count > 0 ? items[^1].Id : null,
            Items =
            [
                .. items.Select(r => new ResponseInfo()
                {
                    Id = r.Id,
                    Body = r.Current.Body,
                    Revision = r.RevisionNumber,
                    Author = r.Author,
                    CreatedAt = r.SubmittedAt,
                    Reactions = Counts(listing.Reactions.GetValueOrDefault(r.Id)),
                }),
            ],
        };

        activity?.SetTag("response.count", page.Items.Count);
        activity?.SetTag("response.total", page.Total);
        WebTelemetry.RequestServed(topic, "list_responses");

        return TypedResults.Ok(page);
    }

    /// <summary>Where the response with <paramref name="id"/> sits in <paramref name="responses"/>. Returns -1 when it is not there.</summary>
    private static int IndexOf(IReadOnlyList<Response> responses, Guid id)
    {
        for (var i = 0; i < responses.Count; i++)
        {
            if (responses[i].Id == id)
            {
                return i;
            }
        }

        return -1;
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
            References = [.. node.References.Select(Describe)],
            Children = [.. node.Children.Select(Describe)],
        };
    }

    private static ReferenceDetail Describe(SummaryNodeReference reference)
    {
        return new ReferenceDetail()
        {
            ResponseId = reference.ResponseId,
            Revision = reference.RevisionNumber,
            IsCurrentRevision = reference.IsOnCurrentRevision,
            Quote = reference.Quote,
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
                CommenterResponses =
                [
                    .. comment.CommenterResponses.Select(response => new CommenterResponseInfo()
                    {
                        ResponseId = response.ResponseId,
                        Body = response.Body,
                    }),
                ],
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
            "summary_not_agent_editable" or "stale_revision" or "live_summary_exists" or "summaries_reviewed" =>
                StatusCodes.Status409Conflict,
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
            Revision = summary.Revision,
            IsLive = summary.IsLive,
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
            Revision = summary.Revision,
            CreatedAt = summary.CreatedAt,
            IsDraft = summary.IsDraft,
            IsAgentEditable = summary.IsAgentEditable,
            IsLive = summary.IsLive,
            IsPublic = summary.IsPublic,
            CreatedBy = summary.CreatedBy,
            NodeCount = summary.Nodes.Count,
            MaxDepth = SummaryTree.Depth(summary.Roots),
        };
    }
}
