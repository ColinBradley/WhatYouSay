namespace WhatYouSay.Telemetry;

/// <summary>
/// Strips the survey code out of a request path before it reaches a trace store.
///
/// ASP.NET Core instrumentation records the real path, so without this every span for an
/// anonymous survey would carry "/surveys/allco26" next to a timestamp — a per-survey
/// submission log, which is exactly the leak anonymous mode gives up timestamps to avoid.
/// The code is stripped from every survey path rather than only anonymous ones, because
/// telling them apart needs a database lookup per span, and the route template keeps the
/// part that helps debugging.
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
