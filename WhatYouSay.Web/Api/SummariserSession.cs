using Microsoft.EntityFrameworkCore;
using WhatYouSay.Auth;
using WhatYouSay.Data;
using WhatYouSay.Telemetry;
using WhatYouSay.Web.Telemetry;

namespace WhatYouSay.Web.Api;

/// <summary>Why a request could not be tied to a survey, and what to tell the caller.</summary>
public enum SummariserRefusal
{
    None,
    MissingToken,
    UnknownToken,
    WrongSurvey,
}

/// <summary>
/// Resolves the one survey a caller may touch. The survey is named in the path and the
/// token in the header, so the two vary independently: a longer-lived or differently
/// scoped credential later on does not change any URL.
/// </summary>
public class SummariserSession(WhatYouSayContext db)
{
    private const string BearerPrefix = "Bearer ";

    private Survey? mSurvey;

    /// <summary>Set once <see cref="AuthenticateAsync"/> has succeeded.</summary>
    public Survey Survey =>
        mSurvey ?? throw new InvalidOperationException("The request has not been authenticated.");

    /// <summary>The survey code the resolved token is actually scoped to.</summary>
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

        var survey = await db.Surveys.FirstOrDefaultAsync(
            s => s.SummariserTokenHash == hash,
            cancellationToken
        );

        if (survey is null)
        {
            return SummariserRefusal.UnknownToken;
        }

        this.ScopedCode = survey.Code;
        activity.SetSurvey(survey);

        if (!string.Equals(survey.Code, code, StringComparison.OrdinalIgnoreCase))
        {
            return SummariserRefusal.WrongSurvey;
        }

        mSurvey = survey;

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
