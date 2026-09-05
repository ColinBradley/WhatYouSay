namespace WhatYouSay.Data;

/// <summary>The one stored representation of a response body.</summary>
public static class ResponseBody
{
    /// <summary>
    /// A browser submits a <c>textarea</c> with CRLF line endings — that is the HTML spec,
    /// not a quirk — so what arrives is not what was typed. Storage, reads, quotes and
    /// offsets have to agree on one representation, and a stray carriage return is
    /// invisible in a diff when they don't: an agent quoting across a line break reaches
    /// for <c>\n</c>, misses, and gets a rejection it cannot see the cause of.
    /// </summary>
    public static string Normalise(string body) =>
        body.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
}
