using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

public class ResponseService
{
    private readonly WhatYouSayContext mDb;

    public ResponseService(WhatYouSayContext db)
    {
        mDb = db;
    }

    /// <summary>
    /// Stores a new response owned by <paramref name="responderToken"/>.
    /// One token may own any number of responses on a topic, so pass the token from the
    /// responder's existing cookie where there is one. Only the token's hash is stored.
    /// </summary>
    public async Task<Response> SubmitAsync(
        Topic topic,
        string responderToken,
        string body,
        string? author,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        if (!topic.IsAcceptingResponses)
        {
            activity.RecordFailure("topic_closed");

            throw new InvalidOperationException("This topic is no longer accepting responses.");
        }

        var response = new Response()
        {
            // Deliberately v4 and not CreateVersion7. A v7 Guid embeds a Unix timestamp,
            // so it would both restore submission order and leak roughly when someone
            // answered — undoing the whole point of not recording a time.
            Id = Guid.NewGuid(),
            TopicId = topic.Id,
            Author = topic.IsAnonymous ? null : NullIfBlank(author),
            AuthTokenHash = Secrets.HashToken(responderToken),
        };

        response.Revise(body, Now(topic));
        mDb.Responses.Add(response);

        await mDb.SaveChangesAsync(cancellationToken);

        WhatYouSayTelemetry.ResponseSubmitted(topic);

        return response;
    }

    /// <summary>
    /// The live responses that <paramref name="responderToken"/> owns on the topic, in the
    /// topic's listing order. Each comes with its revisions loaded.
    /// </summary>
    public async Task<IReadOnlyList<Response>> ListOwnAsync(
        Topic topic,
        string responderToken,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        var hash = Secrets.HashToken(responderToken);

        var query = mDb.Responses
            .Include(r => r.Revisions)
            .Where(r => r.TopicId == topic.Id && r.AuthTokenHash == hash && !r.IsDeleted);

        return await InListingOrder(query, topic).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// One of the responder's own responses, with its revisions loaded. Returns null unless
    /// the response is live, belongs to the topic and is owned by <paramref name="responderToken"/>.
    /// </summary>
    public async Task<Response?> FindOwnAsync(
        Guid topicId,
        Guid responseId,
        string responderToken,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        var hash = Secrets.HashToken(responderToken);

        return await mDb.Responses
            .Include(r => r.Revisions)
            .FirstOrDefaultAsync(
                r => r.Id == responseId && r.TopicId == topicId && r.AuthTokenHash == hash && !r.IsDeleted,
                cancellationToken);
    }

    /// <summary>
    /// Changes the wording and author. A changed wording becomes a new revision, so citations
    /// of the old one keep their text.
    /// Requires the response's revisions to be loaded, as <see cref="FindOwnAsync"/> does.
    /// </summary>
    public async Task EditAsync(
        Topic topic,
        Response response,
        string body,
        string? author,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        RequireEditable(response, activity);

        response.Revise(body, Now(topic));
        response.Author = topic.IsAnonymous ? null : NullIfBlank(author);

        await mDb.SaveChangesAsync(cancellationToken);

        WhatYouSayTelemetry.ResponseEdited(topic);
    }

    /// <summary>Withdraws one response, leaving the responder's other responses as they were.</summary>
    public async Task WithdrawAsync(
        Topic topic,
        Response response,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        RequireEditable(response, activity);

        response.IsDeleted = true;

        await mDb.SaveChangesAsync(cancellationToken);

        WhatYouSayTelemetry.ResponseWithdrawn(topic);
    }

    /// <summary>Every live response on the topic, in the topic's listing order.</summary>
    public async Task<IReadOnlyList<Response>> ListAsync(
        Topic topic,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        var query = mDb.Responses
            .Include(r => r.Revisions)
            .Where(r => r.TopicId == topic.Id && !r.IsDeleted);

        return await InListingOrder(query, topic).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Orders responses newest first by when each was first submitted, so an edit never moves
    /// a response. Equal times fall back to the id, so the order stays stable between reads.
    /// A topic that records no timing orders by the random id alone, because insertion order
    /// would otherwise leak through the SQLite rowid.
    /// </summary>
    internal static IOrderedQueryable<Response> InListingOrder(IQueryable<Response> query, Topic topic)
    {
        if (!topic.RecordsTiming)
        {
            return query.OrderBy(r => r.Id);
        }

        return query
            .OrderByDescending(r => r.Revisions
                .Where(v => v.Number == ResponseRevision.First)
                .Select(v => v.CreatedAt)
                .FirstOrDefault())
            .ThenBy(r => r.Id);
    }

    private static void RequireEditable(Response response, System.Diagnostics.Activity? activity)
    {
        if (!response.IsFrozen)
        {
            return;
        }

        activity.RecordFailure("response_frozen");

        throw new InvalidOperationException(
            "The topic's admin has frozen this response, so it can't be changed until they allow "
            + "editing again.");
    }

    /// <summary>Null on a topic that records no timing.</summary>
    private static DateTimeOffset? Now(Topic topic)
    {
        return topic.RecordsTiming ? DateTimeOffset.UtcNow : null;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
