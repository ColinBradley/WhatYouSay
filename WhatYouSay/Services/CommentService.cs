using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

/// <summary>One comment, with the commenter's own responses to the topic.</summary>
public record NodeCommentView
{
    public required int Id { get; init; }

    public required int NodeId { get; init; }

    public required string NodeText { get; init; }

    public required string Body { get; init; }

    public required string? Author { get; init; }

    public required bool IsHidden { get; init; }

    public required bool IsMine { get; init; }

    /// <summary>
    /// The commenter's live responses to the topic, in the topic's listing order. Empty when
    /// the commenter has none. A comment concerns its node, not any one of these responses.
    /// </summary>
    public required IReadOnlyList<CommenterResponse> CommenterResponses { get; init; }
}

/// <summary>The current wording of one response by the author of a comment.</summary>
public record CommenterResponse
{
    public required Guid ResponseId { get; init; }

    public required string Body { get; init; }
}

public class CommentService
{
    private readonly WhatYouSayContext mDb;

    public CommentService(WhatYouSayContext db)
    {
        mDb = db;
    }

    public async Task<int> AddAsync(
        Topic topic,
        int nodeId,
        string commenterToken,
        string body,
        string? author,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        var trimmed = body.Trim();

        if (trimmed.Length == 0)
        {
            activity.RecordFailure("empty_comment");

            throw new InvalidOperationException("A comment needs something in it.");
        }

        await RequireNodeAsync(mDb, topic, nodeId, activity, cancellationToken);

        var comment = new NodeComment()
        {
            NodeId = nodeId,
            AuthorTokenHash = Secrets.HashToken(commenterToken),
            Author = topic.IsAnonymous || string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
            Body = trimmed,
            CreatedAt = topic.RecordsTiming ? DateTimeOffset.UtcNow : null,
        };

        mDb.NodeComments.Add(comment);

        await mDb.SaveChangesAsync(cancellationToken);

        WhatYouSayTelemetry.CommentAdded(topic);

        return comment.Id;
    }

    /// <summary>
    /// Hides a comment, or brings it back. Callers pass <paramref name="asAdmin"/> having
    /// already checked the password; anyone else may only reach their own.
    /// </summary>
    public async Task SetHiddenAsync(
        Topic topic,
        int commentId,
        string? viewerToken,
        bool hidden,
        bool asAdmin,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        var comment = await mDb.NodeComments
            .FirstOrDefaultAsync(
                c => c.Id == commentId && c.Node.Summary.TopicId == topic.Id,
                cancellationToken);

        if (comment is null)
        {
            activity.RecordFailure("unknown_comment");

            throw new InvalidOperationException($"No comment {commentId} on this topic.");
        }

        var mine = viewerToken is not null
            && comment.AuthorTokenHash == Secrets.HashToken(viewerToken);

        if (!asAdmin && !mine)
        {
            activity.RecordFailure("not_the_author");

            throw new InvalidOperationException("Only the admin or the comment's author can hide it.");
        }

        comment.IsHidden = hidden;

        await mDb.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Every comment on a summary, in reading order. <paramref name="includeHidden"/> is for
    /// the admin and for showing people their own; the agent never sees a hidden one.
    /// </summary>
    public async Task<IReadOnlyList<NodeCommentView>> ListAsync(
        Topic topic,
        Guid summaryId,
        string? viewerToken,
        bool includeHidden = false,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        var mineHash = viewerToken is null ? null : Secrets.HashToken(viewerToken);

        var comments = await mDb.NodeComments
            .Where(c => c.Node.SummaryId == summaryId
                && (includeHidden || !c.IsHidden || c.AuthorTokenHash == mineHash))
            .OrderBy(c => c.Node.Ordinal)
            .ThenBy(c => c.NodeId)
            .ThenBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                c.NodeId,
                NodeText = c.Node.Text,
                c.Body,
                c.Author,
                c.IsHidden,
                c.AuthorTokenHash,
            })
            .ToListAsync(cancellationToken);

        // An admin reading an objection wants the commenter's own words beside it.
        // The responses are read in a second query rather than joined, because a commenter
        // may own several and a join would repeat the comment for each.
        var commenters = comments.Select(c => c.AuthorTokenHash).Distinct().ToList();

        var owned = await ResponseService.InListingOrder(
                mDb.Responses.Where(r => r.TopicId == topic.Id
                    && !r.IsDeleted
                    && commenters.Contains(r.AuthTokenHash)),
                topic)
            .Select(r => new
            {
                r.AuthTokenHash,
                Response = new CommenterResponse()
                {
                    ResponseId = r.Id,
                    Body = r.Revisions
                        .Where(v => v.Number == r.RevisionNumber)
                        .Select(v => v.Body)
                        .First(),
                },
            })
            .ToListAsync(cancellationToken);

        var byCommenter = owned.ToLookup(o => o.AuthTokenHash, o => o.Response);

        return
        [
            .. comments.Select(c => new NodeCommentView()
            {
                Id = c.Id,
                NodeId = c.NodeId,
                NodeText = c.NodeText,
                Body = c.Body,
                Author = c.Author,
                IsHidden = c.IsHidden,
                IsMine = mineHash != null && c.AuthorTokenHash == mineHash,
                CommenterResponses = [.. byCommenter[c.AuthorTokenHash]],
            }),
        ];
    }

    internal static async Task RequireNodeAsync(
        WhatYouSayContext mDb,
        Topic topic,
        int nodeId,
        System.Diagnostics.Activity? activity,
        CancellationToken cancellationToken
    )
    {
        // Readers no longer see an archived node, so feedback for one can only come from a
        // page that is out of date. It is refused rather than stored where nobody sees it.
        var belongs = await mDb.SummaryNodes.AnyAsync(
            n => n.Id == nodeId && n.Summary.TopicId == topic.Id && n.ArchivedAt == null,
            cancellationToken);

        if (!belongs)
        {
            activity.RecordFailure("unknown_node");

            throw new InvalidOperationException(
                $"Node {nodeId} is not in the current tree of a summary on this topic.");
        }
    }
}
