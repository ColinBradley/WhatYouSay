using Microsoft.AspNetCore.DataProtection;

namespace WhatYouSay.Web.Auth;

/// <summary>
/// The seam every admin check goes through, so no page reads an admin cookie itself. Backed by
/// a survey password cookie today; a collection password or real accounts change this class and
/// nothing else.
/// </summary>
public class AdminSession(IHttpContextAccessor accessor, IDataProtectionProvider protection)
{
    private const string Purpose = "WhatYouSay.Admin.v1";

    private static readonly TimeSpan sLifetime = TimeSpan.FromHours(12);

    private readonly ITimeLimitedDataProtector mProtector =
        protection.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public Task<bool> CanAdministerAsync(Guid surveyId)
    {
        var http = accessor.HttpContext;

        if (http is null || !http.Request.Cookies.TryGetValue(NameFor(surveyId), out var cookie))
        {
            return Task.FromResult(false);
        }

        try
        {
            // Throws when tampered with or past its lifetime.
            return Task.FromResult(mProtector.Unprotect(cookie) == surveyId.ToString("n"));
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }

    public void Grant(Guid surveyId)
    {
        var http = accessor.HttpContext;

        if (http is null)
        {
            return;
        }

        var value = mProtector.Protect(surveyId.ToString("n"), sLifetime);

        http.Response.Cookies.Append(NameFor(surveyId), value, new CookieOptions()
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.Add(sLifetime),
            IsEssential = true,
        });
    }

    public void Revoke(Guid surveyId)
    {
        accessor.HttpContext?.Response.Cookies.Delete(NameFor(surveyId));
    }

    private static string NameFor(Guid surveyId)
    {
        return $"wys_admin_{surveyId:n}";
    }
}
