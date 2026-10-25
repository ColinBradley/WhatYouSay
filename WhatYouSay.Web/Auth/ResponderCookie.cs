namespace WhatYouSay.Web.Auth;

/// <summary>
/// Holds the plaintext responder token. Only its hash is stored, so this cookie is the
/// only way back to your own responses — it authorises editing them, and it keys this browser's
/// reactions and comments. Lose it and you can no longer edit those responses or take back
/// those reactions, which is the price of having no accounts.
/// One token covers every response this browser gives to a topic, so withdrawing one
/// response leaves the cookie in place.
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
}
