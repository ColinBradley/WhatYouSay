namespace WhatYouSay.Web.Auth;

/// <summary>
/// Holds the plaintext responder token. Only its hash is stored, so this cookie is the
/// only way back to your own response — it authorises editing while the topic is open and
/// will authorise reacting to summary nodes. Lose it and you lose both, which is the
/// price of having no accounts.
/// </summary>
public static class ResponderCookie
{
    public static string NameFor(Guid topicId) =>
        $"wys_resp_{topicId:n}";

    public static string? Read(HttpContext http, Guid topicId) =>
        http.Request.Cookies.TryGetValue(NameFor(topicId), out var token) && !string.IsNullOrEmpty(token)
            ? token
            : null;

    public static void Write(HttpContext http, Guid topicId, string token) =>
        http.Response.Cookies.Append(NameFor(topicId), token, new CookieOptions()
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,
        });

    public static void Clear(HttpContext http, Guid topicId) =>
        http.Response.Cookies.Delete(NameFor(topicId));
}
