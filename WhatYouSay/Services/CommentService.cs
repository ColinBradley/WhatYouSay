using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

/// <summary>One comment, with the commenter's own response where they gave one.</summary>
public record NodeCommentView
{
    public required int Id { get; init; }

    public required int NodeId { get; init; }

    public required string NodeText { get; init; }

    public required string Body { get; init; }

    public required string? Author { get; init; }

    public required bool IsHidden { get; init; }

    public required bool IsMine { get; init; }

    /// <summary>Null when the commenter never responded to this topic.</summary>
    public required string? ResponseBody { get; init; }
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
            CreatedAt = topic.IsAnonymous ? null : DateTimeOffset.UtcNow,
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

        // Left-joined to the commenter's own response: an admin reading an objection wants
        // the words it is about beside it, and a commenter who never responded has none.
        var query =
            from comment in mDb.NodeComments
            join response in mDb.Responses
                    .Where(r => r.TopicId == topic.Id && !r.IsDeleted)
                on comment.AuthorTokenHash equals response.AuthTokenHash into responses
            from response in responses.DefaultIfEmpty()
            where comment.Node.SummaryId == summaryId
                && (includeHidden || !comment.IsHidden || comment.AuthorTokenHash == mineHash)
            orderby comment.Node.Ordinal, comment.NodeId, comment.Id
            select new NodeCommentView()
            {
                Id = comment.Id,
                NodeId = comment.NodeId,
                NodeText = comment.Node.Text,
                Body = comment.Body,
                Author = comment.Author,
                IsHidden = comment.IsHidden,
                IsMine = mineHash != null && comment.AuthorTokenHash == mineHash,
                ResponseBody = response == null ? null : response.Body,
            };

        return await query.ToListAsync(cancellationToken);
    }

    internal static async Task RequireNodeAsync(
        WhatYouSayContext mDb,
        Topic topic,
        int nodeId,
        System.Diagnostics.Activity? activity,
        CancellationToken cancellationToken
    )
    {
        var belongs = await mDb.SummaryNodes.AnyAsync(
            n => n.Id == nodeId && n.Summary.TopicId == topic.Id,
            cancellationToken);

        if (!belongs)
        {
            activity.RecordFailure("unknown_node");

            throw new InvalidOperationException($"Node {nodeId} is not on a summary of this topic.");
        }
    }
}
