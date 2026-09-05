using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;
using WhatYouSay.Web.Telemetry;

namespace WhatYouSay.Web.Api;

/// <summary>Why a request could not be tied to a topic, and what to tell the caller.</summary>
public enum SummariserRefusal
{
    None,
    MissingToken,
    UnknownToken,
    WrongTopic,
}

/// <summary>
/// Resolves the one topic a caller may touch. The topic is named in the path and the
/// token in the header, so the two vary independently: a longer-lived or differently
/// scoped credential later on does not change any URL.
/// </summary>
public class SummariserSession(WhatYouSayContext db)
{
    private const string BearerPrefix = "Bearer ";

    private Topic? mTopic;

    /// <summary>Set once <see cref="AuthenticateAsync"/> has succeeded.</summary>
    public Topic Topic =>
        mTopic ?? throw new InvalidOperationException("The request has not been authenticated.");

    /// <summary>The topic code the resolved token is actually scoped to.</summary>
    public string? ScopedCode { get; private set; }

    public async Task<SummariserRefusal> AuthenticateAsync(
        HttpRequest request,
        string code,
        CancellationToken cancellationToken
    )
    {
        using var activity = WebTelemetry.Source.Start();

        var token = TokenFromHeader(request);

        if (string.IsNullOrWhiteSpace(token))
        {
            return SummariserRefusal.MissingToken;
        }

        var hash = Secrets.HashToken(token);

        var topic = await db.Topics.FirstOrDefaultAsync(
            s => s.SummariserTokenHash == hash,
            cancellationToken
        );

        if (topic is null)
        {
            return SummariserRefusal.UnknownToken;
        }

        this.ScopedCode = topic.Code;
        activity.SetTopic(topic);

        if (!string.Equals(topic.Code, code, StringComparison.OrdinalIgnoreCase))
        {
            return SummariserRefusal.WrongTopic;
        }

        mTopic = topic;

        return SummariserRefusal.None;
    }

    private static string? TokenFromHeader(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();

        return header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? header[BearerPrefix.Length..].Trim()
            : null;
    }
}
