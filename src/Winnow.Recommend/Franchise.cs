using Winnow.Core.Queries;

namespace Winnow.Recommend;

/// <summary>
/// The franchise-ish grouping key behind the one-per-franchise shelf cap.
///
/// <para><b>Why it exists.</b> The measured library holds 14 unplayed
/// "Infinity Blade: …" entries, 5 "Star Wars …", 5 "Sid Meier's Civilization
/// IV: …" — rank them honestly by score and a shelf becomes one franchise
/// five times, which is a broken feed even when every individual score is
/// right. This is NOT an identity decision (that is Winnow.Resolve's job and
/// merge-queue territory): two rows sharing a franchise key remain two works,
/// two candidates, two potential recommendations — the key only stops them
/// occupying one shelf on the same day.</para>
///
/// <para><b>The rule.</b> Take the title up to the first colon, slugify it
/// with the same fold the facet vocabulary uses (which also strips ™ and ®),
/// then drop a trailing arabic- or roman-numeral token: "Half-Life 2:
/// Deathmatch" → <c>half_life</c>, "Sid Meier's Civilization IV: Colonization"
/// → <c>sid_meier_s_civilization</c>, "Left 4 Dead 2" → <c>left_4_dead</c>
/// (interior digits survive). Deliberately conservative: a false split (two
/// franchise entries not grouped) costs one slightly samey shelf, while a
/// false merge (two unrelated games grouped) silently suppresses a valid
/// recommendation — so only the two cheap, high-precision folds are applied,
/// and single-token titles are never trimmed.</para>
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

    /// <summary>
    /// "2", "IV", "xiii" — the tokens sequels append. Roman detection is
    /// limited to i/v/x so real words ("mix", "civ") are never eaten; no
    /// franchise numbers itself past the tens in roman numerals anyway.
    /// </summary>
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
