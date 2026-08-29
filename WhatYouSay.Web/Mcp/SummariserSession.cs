using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using WhatYouSay.Auth;
using WhatYouSay.Data;

namespace WhatYouSay.Web.Mcp;

/// <summary>
/// Resolves the one survey a caller may touch, from the bearer token. The token is
/// survey-scoped, so no tool takes a survey argument and none can reach another survey.
/// </summary>
public class SummariserSession(IHttpContextAccessor accessor, WhatYouSayContext db)
{
    private Survey? mSurvey;

    public async Task<Survey> RequireSurveyAsync(CancellationToken cancellationToken)
    {
        if (mSurvey is not null)
        {
            return mSurvey;
        }

        var header = accessor.HttpContext?.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(header)
            || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException(
                "Send the survey's summariser token as 'Authorization: Bearer <token>'.");
        }

        var hash = Secrets.HashToken(header["Bearer ".Length..].Trim());

        mSurvey = await db.Surveys.FirstOrDefaultAsync(s => s.SummariserTokenHash == hash, cancellationToken)
            ?? throw new McpException("That summariser token does not match any survey.");

        return mSurvey;
    }
}
