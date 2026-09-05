using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;

namespace WhatYouSay.Services;

public class ResponseService(WhatYouSayContext db)
{
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

        db.Responses.Add(new Response()
        {
            // Deliberately v4 and not CreateVersion7. A v7 Guid embeds a Unix timestamp,
            // so it would both restore submission order and leak roughly when someone
            // answered — undoing the whole point of not recording CreatedAt.
            Id = Guid.NewGuid(),
            TopicId = topic.Id,
            Body = ResponseBody.Normalise(body),
            Author = topic.IsAnonymous ? null : NullIfBlank(author),
            AuthTokenHash = Secrets.HashToken(token),
            CreatedAt = topic.IsAnonymous ? null : DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);

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

        return await db.Responses.FirstOrDefaultAsync(
            r => r.TopicId == topicId && r.AuthTokenHash == hash && !r.IsDeleted,
            cancellationToken);
    }

    /// <summary>Editable until frozen, so summary quotes cannot rot.</summary>
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

        response.Body = ResponseBody.Normalise(body);
        response.Author = topic.IsAnonymous ? null : NullIfBlank(author);
        response.UpdatedAt = topic.IsAnonymous ? null : DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

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

        await db.SaveChangesAsync(cancellationToken);

        WhatYouSayTelemetry.ResponseWithdrawn(topic);
    }

    public async Task<IReadOnlyList<Response>> ListAsync(
        Topic topic,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = WhatYouSayTelemetry.Source.Start().SetTopic(topic);

        var query = db.Responses.Where(r => r.TopicId == topic.Id && !r.IsDeleted);

        // Anonymous topics have no timestamps to order by, so they fall back to the
        // random Guid. Insertion order would otherwise leak through the SQLite rowid.
        query = topic.IsAnonymous
            ? query.OrderBy(r => r.Id)
            : query.OrderBy(r => r.CreatedAt);

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

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
