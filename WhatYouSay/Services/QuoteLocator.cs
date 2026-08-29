namespace WhatYouSay.Services;

public readonly record struct QuoteLocation(int StartIndex, int EndIndex);

/// <summary>
/// Finds where a quote sits inside a response body. Used when seeding, by the grounding
/// validation that rejects citations an agent cannot substantiate, and by rendering to
/// check an offset still lines up before slicing.
/// </summary>
public static class QuoteLocator
{
    public static QuoteLocation? Locate(string body, string quote)
    {
        if (string.IsNullOrEmpty(quote))
        {
            return null;
        }

        var start = body.IndexOf(quote, StringComparison.Ordinal);

        return start < 0 ? null : new QuoteLocation(start, start + quote.Length);
    }

    /// <summary>Whether the stored offsets still select exactly the stored quote.</summary>
    public static bool Matches(string body, string quote, int startIndex, int endIndex) =>
        startIndex >= 0
        && endIndex <= body.Length
        && endIndex - startIndex == quote.Length
        && string.CompareOrdinal(body, startIndex, quote, 0, quote.Length) == 0;
}
