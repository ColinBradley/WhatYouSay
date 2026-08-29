using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WhatYouSay.Data;

/// <summary>
/// SQLite refuses to ORDER BY a DateTimeOffset, because EF's default mapping keeps the
/// offset in the text and rows with different offsets would sort wrongly. Normalising to
/// UTC and writing a fixed-width ISO-8601 string makes the column sort lexicographically,
/// which is exactly chronological, and keeps it readable in a sqlite3 session — the same
/// reason enums are stored as strings.
/// </summary>
public class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, string>(
    value => value.ToUniversalTime().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
    text => DateTimeOffset.Parse(
        text,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));
