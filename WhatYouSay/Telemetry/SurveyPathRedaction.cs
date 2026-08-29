namespace WhatYouSay.Telemetry;

/// <summary>
/// Strips the survey code out of a request path before it reaches a trace store, so spans
/// for an anonymous survey do not carry "/surveys/allco26" next to a timestamp. Applied to
/// every survey path, since telling anonymous ones apart needs a database lookup per span.
/// </summary>
public static class SurveyPathRedaction
{
    private const string Prefix = "/surveys/";

    private const string Placeholder = "/surveys/{code}";

    /// <summary>Null when there is nothing to redact, so callers can skip the write.</summary>
    public static string? Redact(string path)
    {
        if (!path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = path[Prefix.Length..];

        if (rest.Length == 0)
        {
            return null;
        }

        // Everything past the code is kept: /surveys/abc/summary/x -> /surveys/{code}/summary/x
        var nextSlash = rest.IndexOf('/');

        return nextSlash < 0 ? Placeholder : Placeholder + rest[nextSlash..];
    }
}
