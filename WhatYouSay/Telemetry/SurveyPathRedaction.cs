namespace WhatYouSay.Telemetry;

/// <summary>
/// Strips the survey code out of a request path before it reaches a trace store, so spans
/// for an anonymous survey do not sit next to a timestamp. Applied to every survey path,
/// since telling anonymous ones apart needs a database lookup per span.
/// </summary>
public static class SurveyPathRedaction
{
    private const string SurveyPrefix = "/surveys/";

    private const string SurveyPlaceholder = "/surveys/{code}";

    private const string ApiPrefix = "/api/surveys/";

    private const string ApiPlaceholder = "/api/surveys/{code}";

    /// <summary>Null when there is nothing to redact, so callers can skip the write.</summary>
    public static string? Redact(string path) =>
        RedactFirstSegment(path, ApiPrefix, ApiPlaceholder)
            ?? RedactFirstSegment(path, SurveyPrefix, SurveyPlaceholder);

    private static string? RedactFirstSegment(string path, string prefix, string placeholder)
    {
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = path[prefix.Length..];

        if (rest.Length == 0)
        {
            return null;
        }

        // Everything past the code is kept: /surveys/abc/summary/x -> /surveys/{code}/summary/x
        var nextSlash = rest.IndexOf('/');

        return nextSlash < 0 ? placeholder : placeholder + rest[nextSlash..];
    }
}
