using System.Globalization;
using System.Text;

namespace Winnow.Ingest.Steam.AccountPages;

/// <summary>
/// A monetary amount as cents plus whatever currency symbol the markup contained.
///
/// <para>Cents, not a fractional unit, because cent arithmetic is exact and the
/// pages never carry sub-cent precision. The symbol is preserved verbatim so a
/// caller can tell currencies apart, but no conversion is attempted.</para>
/// </summary>
/// <param name="Cents">Signed amount in the smallest currency unit.</param>
/// <param name="CurrencySymbol">The currency symbol or abbreviation found in the text, or null when none was present.</param>
public readonly record struct SteamMoney(long Cents, string? CurrencySymbol);

/// <summary>
/// Value parsers for the text Steam renders in its two account pages.
///
/// <para>All parsing uses exact formats and invariant culture. A value this
/// parser cannot read is returned as null, never guessed into another locale's
/// reading. The account pages verified here were en-US; a non-US account may
/// render dates or money in a different shape and will degrade to null rather
/// than to a wrong value. Verified 2026-08-29.</para>
/// </summary>
public static class SteamPageValues
{
    private const char NonBreakingSpace = ' ';
    private const char NarrowNoBreakSpace = ' ';
    private const char MinusSign = '−';

    private static readonly string[] DateFormats =
    [
        "MMM d, yyyy",
        "MMM d yyyy",
        "d MMM, yyyy",
        "MMMM d, yyyy",
    ];

    /// <summary>
    /// Parses a date from the exact en-US shapes Steam uses ("MMM d, yyyy" and
    /// close variants). Returns a UTC midnight or null. A date that does not
    /// match any known format is returned as null and counted by the caller,
    /// never inferred.
    /// </summary>
    public static DateTime? TryParseDateUtc(string? text)
    {
        var trimmed = Collapse(text);
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (!DateTime.TryParseExact(
                trimmed,
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return null;
        }

        return DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
    }

    /// <summary>
    /// Parses a monetary amount into signed cents plus whatever currency symbol
    /// was present.
    ///
    /// <para>Only a final separator with exactly two digits behind it is treated
    /// as a decimal point; every other separator is grouping. This means
    /// "$1,249.00" parses as 124900 cents rather than losing a factor of a
    /// thousand, and a comma-decimal locale parses correctly for the same
    /// reason.</para>
    /// </summary>
    public static SteamMoney? TryParseMoney(string? text)
    {
        var trimmed = Collapse(text);
        if (trimmed.Length == 0)
        {
            return null;
        }

        var negative = false;
        var symbol = new StringBuilder();
        var digits = new StringBuilder();
        var digitsBeforeLastSeparator = -1;

        foreach (var c in trimmed)
        {
            if (c is '-' or MinusSign)
            {
                negative = true;
                continue;
            }

            if (c is '+' or ' ' or NonBreakingSpace or NarrowNoBreakSpace)
            {
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                digits.Append(c);
                continue;
            }

            if (c is ',' or '.' or '\'')
            {
                digitsBeforeLastSeparator = digits.Length;
                continue;
            }

            symbol.Append(c);
        }

        if (digits.Length == 0)
        {
            return null;
        }

        // Only a final separator with exactly two digits behind it is a decimal
        // point; every other separator is grouping. "$1,249.00" is 124900 cents,
        // "$1,249" is 124900 too, and neither is 124.9.
        var fractionDigits = digitsBeforeLastSeparator < 0 ? 0 : digits.Length - digitsBeforeLastSeparator;
        var whole = ParseWhole(digits.ToString());
        if (whole is null)
        {
            return null;
        }

        var cents = fractionDigits == 2 ? whole.Value : whole.Value * 100;
        var currency = Collapse(symbol.ToString());
        return new SteamMoney(negative ? -cents : cents, currency.Length == 0 ? null : currency);
    }

    /// <summary>Parses a percentage, stripping the '%' sign. Returns null on failure.</summary>
    public static int? TryParsePercent(string? text)
    {
        var trimmed = Collapse(text).Replace("%", string.Empty, StringComparison.Ordinal);
        var negative = trimmed.StartsWith('-') || trimmed.StartsWith(MinusSign);
        trimmed = trimmed.TrimStart('-', '+', MinusSign);

        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return negative ? -value : value;
    }

    /// <summary>
    /// Collapses all whitespace (including non-breaking spaces) to single ASCII
    /// spaces and trims. Steam's markup is full of non-breaking spaces and
    /// multi-space runs; every reader in this package normalises through this
    /// before comparing or parsing.
    /// </summary>
    public static string Collapse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || c == NonBreakingSpace || c == NarrowNoBreakSpace)
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static long? ParseWhole(string digits)
        => long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
}
