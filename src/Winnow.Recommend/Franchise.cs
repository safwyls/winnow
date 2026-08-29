using Winnow.Core.Queries;

namespace Winnow.Recommend;

/// <summary>
/// Grouping key for the one-per-franchise shelf cap. Slugifies the title up to the first
/// colon, then drops a trailing numeral token. Not an identity decision — two rows sharing
/// a key remain separate works; the key only prevents them occupying the same shelf.
/// </summary>
internal static class Franchise
{
    public static string KeyFor(string title)
    {
        var colon = title.IndexOf(':', StringComparison.Ordinal);
        var head = colon > 0 ? title[..colon] : title;
        var slug = Facet.Slugify(head);
        if (slug.Length == 0)
        {
            return title;
        }

        var lastSeparator = slug.LastIndexOf('_');
        if (lastSeparator > 0 && IsNumeralToken(slug.AsSpan(lastSeparator + 1)))
        {
            slug = slug[..lastSeparator];
        }

        return slug;
    }

    /// <summary>Matches arabic digits or roman numerals (i/v/x only, to avoid eating real words).</summary>
    private static bool IsNumeralToken(ReadOnlySpan<char> token)
    {
        if (token.Length == 0)
        {
            return false;
        }

        var allDigits = true;
        var allRoman = true;
        foreach (var c in token)
        {
            allDigits &= char.IsAsciiDigit(c);
            allRoman &= c is 'i' or 'v' or 'x';
        }

        return allDigits || allRoman;
    }
}
