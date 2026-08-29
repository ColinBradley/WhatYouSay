namespace WhatYouSay.Web.Auth;

/// <summary>
/// Holds the plaintext responder token. Only its hash is stored, so this cookie is the
/// only way back to your own response — it authorises editing while the survey is open and
/// will authorise reacting to summary points. Lose it and you lose both, which is the
/// price of having no accounts.
/// </summary>
public static class ResponderCookie
{
    public static string NameFor(Guid surveyId) => $"wys_resp_{surveyId:n}";

    public static string? Read(HttpContext http, Guid surveyId) =>
        http.Request.Cookies.TryGetValue(NameFor(surveyId), out var token) && !string.IsNullOrEmpty(token)
            ? token
            : null;

    public static void Write(HttpContext http, Guid surveyId, string token) =>
        http.Response.Cookies.Append(NameFor(surveyId), token, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true
        });

    public static void Clear(HttpContext http, Guid surveyId) =>
        http.Response.Cookies.Delete(NameFor(surveyId));
}
