using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WhatYouSay.Data;

/// <summary>
/// SQLite refuses to ORDER BY a DateTimeOffset: EF's default mapping keeps the offset in
/// the text, so rows with different offsets would sort wrongly. Normalising to UTC makes
/// the column sort lexicographically, which is chronologically.
/// </summary>
public class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, string>(
    value => value.ToUniversalTime().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
    text => DateTimeOffset.Parse(
        text,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));
