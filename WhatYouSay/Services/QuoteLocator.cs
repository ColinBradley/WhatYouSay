using System.Text;

namespace WhatYouSay.Services;

public readonly record struct QuoteLocation
{
    public required int StartIndex { get; init; }

    public required int EndIndex { get; init; }
}

/// <summary>
/// Why a quote did not match, phrased for whoever has to fix the quote.
/// </summary>
public readonly record struct QuoteMismatch
{
    /// <summary>
    /// The response text the quote was probably reaching for, copied exactly, or null when
    /// nothing in the response resembles it. Safe to paste back as the corrected quote.
    /// </summary>
    public required string? Nearest { get; init; }

    public required string Detail { get; init; }
}

/// <summary>Finds where a quote sits inside a response body.</summary>
public static class QuoteLocator
{
    /// <summary>Shortest prefix worth anchoring on; below this every quote "nearly" matches.</summary>
    private const int MinimumAnchor = 12;

    public static QuoteLocation? Locate(string body, string quote)
    {
        if (string.IsNullOrEmpty(quote))
        {
            return null;
        }

        var start = body.IndexOf(quote, StringComparison.Ordinal);

        return start < 0
            ? null
            : new QuoteLocation { StartIndex = start, EndIndex = start + quote.Length };
    }

    /// <summary>Whether the stored offsets still select exactly the stored quote.</summary>
    public static bool Matches(string body, string quote, int startIndex, int endIndex) =>
        startIndex >= 0
        && endIndex <= body.Length
        && endIndex - startIndex == quote.Length
        && string.CompareOrdinal(body, startIndex, quote, 0, quote.Length) == 0;

    /// <summary>
    /// Explains a quote that <see cref="Locate"/> could not find. Smart quotes, en and em
    /// dashes and collapsed line breaks are the usual causes and are near-invisible in a
    /// diff, so the answer names the offending character rather than only reporting a miss.
    /// </summary>
    public static QuoteMismatch Diagnose(string body, string quote)
    {
        if (string.IsNullOrEmpty(quote))
        {
            return new QuoteMismatch { Nearest = null, Detail = "The quote is empty." };
        }

        var nearest = FindNearest(body, quote);

        if (nearest is null)
        {
            return new QuoteMismatch()
            {
                Nearest = null,
                Detail = "No text in this response resembles the quote.",
            };
        }

        return new QuoteMismatch { Nearest = nearest, Detail = Compare(quote, nearest) };
    }

    /// <summary>
    /// The response text the quote most likely meant, or null. Matching is done on a folded
    /// copy of both strings while an index map keeps the original offsets, so what comes
    /// back is always exact response text rather than the folded form.
    /// </summary>
    private static string? FindNearest(string body, string quote)
    {
        var (foldedBody, map) = Fold(body);
        var (foldedQuote, _) = Fold(quote);

        if (foldedQuote.Length == 0)
        {
            return null;
        }

        var at = foldedBody.IndexOf(foldedQuote, StringComparison.Ordinal);

        if (at >= 0)
        {
            return Slice(body, map, at, foldedQuote.Length);
        }

        // Nothing matched whole, so anchor on the longest prefix that does and return the
        // response text from there, letting the caller see where the two diverge.
        for (var length = foldedQuote.Length - 1; length >= MinimumAnchor; length--)
        {
            at = foldedBody.IndexOf(foldedQuote[..length], StringComparison.Ordinal);

            if (at >= 0)
            {
                return Slice(body, map, at, Math.Min(foldedQuote.Length, foldedBody.Length - at));
            }
        }

        return null;
    }

    private static string Slice(string body, int[] map, int start, int length)
    {
        var from = map[start];
        var end = start + length;
        var to = end < map.Length ? map[end] : body.Length;

        return body[from..to].TrimEnd();
    }

    /// <summary>
    /// Names the first place two strings diverge. Both are response-sized, so a character
    /// index is more use than a diff.
    /// </summary>
    private static string Compare(string quote, string nearest)
    {
        var shared = Math.Min(quote.Length, nearest.Length);

        for (var i = 0; i < shared; i++)
        {
            if (quote[i] != nearest[i])
            {
                return $"Differs at character {i}: you sent {Describe(quote[i])} where the "
                    + $"response has {Describe(nearest[i])}.";
            }
        }

        if (quote.Length > nearest.Length)
        {
            return $"Your quote runs {quote.Length - shared} characters past the response text.";
        }

        if (quote.Length < nearest.Length)
        {
            return $"Your quote stops {nearest.Length - shared} characters short of the response text.";
        }

        return "The two differ only in characters that are invisible here.";
    }

    private static string Describe(char value)
    {
        var name = value switch
        {
            '\r' => "carriage return",
            '\n' => "line feed",
            '\t' => "tab",
            '\'' => "straight apostrophe",
            '"' => "straight double quote",
            ' ' => "space",
            ' ' => "no-break space",
            _ => null,
        };

        return name is not null
            ? $"U+{(int)value:X4} ({name})"
            : $"U+{(int)value:X4} ('{value}')";
    }

    /// <summary>
    /// Folds the differences an agent gets wrong without noticing: curly punctuation, dash
    /// width, case, and whitespace runs. The map holds each folded character's index in the
    /// original, so a match can be reported as exact source text.
    /// </summary>
    private static (string Folded, int[] Map) Fold(string value)
    {
        var builder = new StringBuilder(value.Length);
        var map = new List<int>(value.Length);
        var inWhitespace = false;

        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];

            if (char.IsWhiteSpace(character))
            {
                if (!inWhitespace)
                {
                    builder.Append(' ');
                    map.Add(i);
                    inWhitespace = true;
                }

                continue;
            }

            inWhitespace = false;
            builder.Append(FoldCharacter(character));
            map.Add(i);
        }

        return (builder.ToString(), [.. map]);
    }

    private static char FoldCharacter(char value) =>
        value switch
        {
            '‘' or '’' or 'ʼ' or '´' => '\'',
            '“' or '”' => '"',
            '‐' or '‑' or '‒' or '–' or '—' or '−' => '-',
            _ => char.ToLowerInvariant(value),
        };
}
