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
    /// Stores a response and returns the plaintext token for the responder's cookie. Only
    /// the hash is kept, so this is the one moment the token is knowable.
    /// </summary>
    public async Task<string> SubmitAsync(
        Topic topic,
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

        var token = Secrets.NewToken();

        var response = new Response()
        {
            // Deliberately v4 and not CreateVersion7. A v7 Guid embeds a Unix timestamp,
            // so it would both restore submission order and leak roughly when someone
            // answered — undoing the whole point of not recording a time.
            Id = Guid.NewGuid(),
            TopicId = topic.Id,
            Author = topic.IsAnonymous ? null : NullIfBlank(author),
            AuthTokenHash = Secrets.HashToken(token),
        };

        response.Revise(body, Now(topic));
        mDb.Responses.Add(response);

        await mDb.SaveChangesAsync(cancellationToken);

        WhatYouSayTelemetry.ResponseSubmitted(topic);

        return token;
    }

    public async Task<Response?> FindOwnAsync(
        Guid topicId,
        string token,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start();

        var hash = Secrets.HashToken(token);

        return await mDb.Responses
            .Include(r => r.Revisions)
            .FirstOrDefaultAsync(
                r => r.TopicId == topicId && r.AuthTokenHash == hash && !r.IsDeleted,
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

    public async Task<IReadOnlyList<Response>> ListAsync(
        Topic topic,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        var query = mDb.Responses
            .Include(r => r.Revisions)
            .Where(r => r.TopicId == topic.Id && !r.IsDeleted);

        // Anonymous topics have no timestamps to order by, so they fall back to the
        // random Guid. Insertion order would otherwise leak through the SQLite rowid.
        query = topic.IsAnonymous
            ? query.OrderBy(r => r.Id)
            : query.OrderBy(r => r.Revisions
                .Where(v => v.Number == ResponseRevision.First)
                .Select(v => v.CreatedAt)
                .FirstOrDefault());

        return await query.ToListAsync(cancellationToken);
    }

    private static void RequireEditable(Response response, System.Diagnostics.Activity? activity)
    {
        if (!response.IsFrozen)
        {
            return;
        }

        activity.RecordFailure("response_frozen");

        throw new InvalidOperationException(
            "This response was frozen when the topic closed, so it can no longer be changed.");
    }

    /// <summary>Null on an anonymous topic, which records no timing.</summary>
    private static DateTimeOffset? Now(Topic topic)
    {
        return topic.IsAnonymous ? null : DateTimeOffset.UtcNow;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
