namespace WhatYouSay.Telemetry;

/// <summary>
/// Strips the topic code out of a request path before it reaches a trace store, so spans
/// for an anonymous topic do not sit next to a timestamp. Applied to every topic path,
/// since telling anonymous ones apart needs a database lookup per span.
/// </summary>
public static class TopicPathRedaction
{
    private const string TopicPrefix = "/topics/";

    private const string TopicPlaceholder = "/topics/{code}";

    private const string ApiPrefix = "/api/topics/";

    private const string ApiPlaceholder = "/api/topics/{code}";

    /// <summary>Null when there is nothing to redact, so callers can skip the write.</summary>
    public static string? Redact(string path) =>
        RedactFirstSegment(path, ApiPrefix, ApiPlaceholder)
            ?? RedactFirstSegment(path, TopicPrefix, TopicPlaceholder);

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

        // Everything past the code is kept: /topics/abc/summary/x -> /topics/{code}/summary/x
        var nextSlash = rest.IndexOf('/');

        return nextSlash < 0 ? placeholder : placeholder + rest[nextSlash..];
    }
}
