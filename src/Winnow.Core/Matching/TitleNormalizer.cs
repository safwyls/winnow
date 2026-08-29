using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Winnow.Core.Matching;

/// <summary>
/// Turns a raw store title into a <see cref="NormalizedTitle"/> (§5.3 step 2).
/// Pure, deterministic, no IO. Shared by the soft matcher and
/// <see cref="Winnow.Core.Queries.DemoConsolidation"/> so both agree on title identity.
///
/// <para>Pipeline (order matters): strip marks, lift parenthesised year,
/// fold accents (NFD), lower-case, join apostrophes/dots, expand &amp;,
/// collapse non-alnum to space, fold roman numerals, extract edition markers,
/// drop articles, fold spelled-out cardinals.</para>
/// </summary>
public static partial class TitleNormalizer
{
    /// <summary>Marks deleted outright (never turned into spaces or letters).</summary>
    private const string MarkChars = "™®©℗℠";

    /// <summary>Deleted so the surrounding letters join up.</summary>
    private const string JoinChars = "'’‘ʼ`.";

    /// <summary>Roman numerals only fold inside a plausible sequel range.</summary>
    private const int MaxRomanOrdinal = 30;

    /// <summary>Edition markers meaning "a separate build" (§9 pitfall 5). Longest phrase wins.</summary>
    private static readonly string[][] RebuildEditionPhrases =
    [
        ["special", "edition"],
        ["remastered", "edition"],
        ["remastered"],
        ["remaster"],
        ["anniversary", "edition"],
        ["anniversary"],
        ["definitive", "edition"],
        ["enhanced", "edition"],
        ["legendary", "edition"],
        ["hd", "remaster"],
        ["hd", "edition"],
        ["hd"],
        ["redux"],
        ["remake"],
        ["reforged"],
        ["classic"],
    ];

    /// <summary>Edition markers meaning "same build, more content". Disagreement is a small penalty, not a veto.</summary>
    private static readonly string[][] BundleEditionPhrases =
    [
        ["game", "of", "the", "year", "edition"],
        ["game", "of", "the", "year"],
        ["goty", "edition"],
        ["goty"],
        ["digital", "deluxe", "edition"],
        ["digital", "deluxe"],
        ["complete", "edition"],
        ["deluxe", "edition"],
        ["deluxe"],
        ["gold", "edition"],
        ["ultimate", "edition"],
        ["premium", "edition"],
        ["collectors", "edition"],
        ["standard", "edition"],
        ["day", "one", "edition"],
        ["directors", "cut"],
        // Fallback: an unrecognised "<something> Edition" still ends in this
        // token. Treated as a bundle marker rather than left in the core, where
        // it would inflate similarity between two unrelated "… Edition" titles.
        ["edition"],
    ];

    private static readonly string[] Articles = ["the"];
    private static readonly string[] LeadingArticles = ["a", "an"];

    /// <summary>
    /// Spelled-out cardinals folded to arabic so ordinal comparison is consistent
    /// with roman numeral folding. Ceiling is 20; "zero" is absent (it is a name).
    /// </summary>
    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.Ordinal)
    {
        ["one"] = 1,
        ["two"] = 2,
        ["three"] = 3,
        ["four"] = 4,
        ["five"] = 5,
        ["six"] = 6,
        ["seven"] = 7,
        ["eight"] = 8,
        ["nine"] = 9,
        ["ten"] = 10,
        ["eleven"] = 11,
        ["twelve"] = 12,
        ["thirteen"] = 13,
        ["fourteen"] = 14,
        ["fifteen"] = 15,
        ["sixteen"] = 16,
        ["seventeen"] = 17,
        ["eighteen"] = 18,
        ["nineteen"] = 19,
        ["twenty"] = 20,
    };

    [GeneratedRegex(@"\((1[89]\d{2}|20\d{2})\)", RegexOptions.CultureInvariant)]
    private static partial Regex ParenthesisedYearRegex { get; }

    public static NormalizedTitle Normalize(string? title)
    {
        var original = title ?? string.Empty;

        var stripped = RemoveChars(original, MarkChars);

        int? parsedYear = null;
        var yearMatch = ParenthesisedYearRegex.Match(stripped);
        if (yearMatch.Success)
        {
            parsedYear = int.Parse(yearMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            stripped = ParenthesisedYearRegex.Replace(stripped, " ");
        }

        var tokens = Tokenize(FoldAccents(stripped));
        tokens = FoldRomanNumerals(tokens);

        var rebuild = ExtractEditions(tokens, RebuildEditionPhrases, out tokens);
        var bundle = ExtractEditions(tokens, BundleEditionPhrases, out tokens);

        tokens = DropArticles(tokens);

        // After the edition pass, so "Day One Edition" is lifted out whole
        // rather than shredded into "day 1 edition"; after articles, so the
        // leading-word guard sees "Two Thrones", not "The Two Thrones".
        tokens = FoldNumberWords(tokens);

        var ordinals = new List<int>();
        foreach (var token in tokens)
        {
            if (IsAsciiDigits(token) && int.TryParse(token, CultureInfo.InvariantCulture, out var value))
            {
                ordinals.Add(value);
            }
        }

        return new NormalizedTitle(
            Original: original,
            Core: string.Join(' ', tokens),
            Tokens: tokens,
            Ordinals: ordinals,
            RebuildEditions: rebuild,
            BundleEditions: bundle,
            ParsedYear: parsedYear);
    }

    /// <summary>Normalises a publisher name, stripping legal-form suffixes (LLC, Inc, etc.).</summary>
    public static string NormalizePublisher(string? publisher)
    {
        var tokens = Tokenize(FoldAccents(RemoveChars(publisher ?? string.Empty, MarkChars)));
        var kept = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            if (token is "inc" or "llc" or "ltd" or "limited" or "corp" or "corporation"
                or "co" or "sa" or "sarl" or "srl" or "gmbh" or "ab" or "as" or "plc"
                or "the" or "publishing" or "interactive")
            {
                continue;
            }

            kept.Add(token);
        }

        // Everything was noise (e.g. publisher literally "The Publishing Co") —
        // keep the un-pruned form rather than claiming an empty-string match.
        return kept.Count == 0 ? string.Join(' ', tokens) : string.Join(' ', kept);
    }

    private static string RemoveChars(string value, string chars)
    {
        if (value.AsSpan().IndexOfAny(chars) < 0)
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!chars.Contains(c, StringComparison.Ordinal))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static string FoldAccents(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static List<string> Tokenize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var raw in value)
        {
            var c = char.ToLowerInvariant(raw);

            if (JoinChars.Contains(c, StringComparison.Ordinal))
            {
                continue;
            }

            if (c == '&')
            {
                sb.Append(" and ");
                continue;
            }

            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return [.. sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)];
    }

    /// <summary>
    /// Folds roman numerals to arabic within <see cref="MaxRomanOrdinal"/>.
    /// Guards: leading single-letter tokens are never folded (names like "I Am Setsuna"),
    /// and bare "x" is never folded (it is a name more often than "10").
    /// </summary>
    private static List<string> FoldRomanNumerals(List<string> tokens)
    {
        var result = new List<string>(tokens.Count);
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Length == 1 && (i == 0 || token == "x"))
            {
                result.Add(token);
                continue;
            }

            var value = TryParseRoman(token);
            result.Add(value is > 0 and <= MaxRomanOrdinal
                ? value.Value.ToString(CultureInfo.InvariantCulture)
                : token);
        }

        return result;
    }

    /// <summary>
    /// Folds spelled-out cardinals to arabic. Leading token is never folded
    /// (it is a name, not a sequel number).
    /// </summary>
    private static List<string> FoldNumberWords(List<string> tokens)
    {
        var result = new List<string>(tokens.Count);
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            result.Add(i > 0 && NumberWords.TryGetValue(token, out var value)
                ? value.ToString(CultureInfo.InvariantCulture)
                : token);
        }

        return result;
    }

    private static int? TryParseRoman(string token)
    {
        if (token.Length is 0 or > 15)
        {
            return null;
        }

        var total = 0;
        var previous = 0;
        for (var i = token.Length - 1; i >= 0; i--)
        {
            var digit = token[i] switch
            {
                'i' => 1,
                'v' => 5,
                'x' => 10,
                'l' => 50,
                'c' => 100,
                'd' => 500,
                'm' => 1000,
                _ => 0,
            };

            if (digit == 0)
            {
                return null;
            }

            total += digit < previous ? -digit : digit;
            previous = Math.Max(previous, digit);
        }

        // Round-trip guard: rejects "iiii", "vv", "ic" and other non-canonical
        // letter soup that the accumulator above would otherwise happily total.
        return ToRoman(total) == token ? total : null;
    }

    private static string ToRoman(int value)
    {
        if (value is <= 0 or > 3999)
        {
            return string.Empty;
        }

        ReadOnlySpan<int> values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] symbols = ["m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i"];

        var sb = new StringBuilder();
        for (var i = 0; i < values.Length; i++)
        {
            while (value >= values[i])
            {
                sb.Append(symbols[i]);
                value -= values[i];
            }
        }

        return sb.ToString();
    }

    /// <summary>Removes known edition phrases (longest first, anywhere in the title) and returns the markers found.</summary>
    private static IReadOnlyList<string> ExtractEditions(
        List<string> tokens, string[][] phrases, out List<string> remaining)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        var current = tokens;

        // Longest phrase first so "special edition" wins over bare "edition".
        foreach (var phrase in phrases.OrderByDescending(p => p.Length).ThenBy(p => string.Join(' ', p), StringComparer.Ordinal))
        {
            while (true)
            {
                var at = IndexOfRun(current, phrase);
                if (at < 0)
                {
                    break;
                }

                found.Add(string.Join(' ', phrase));
                var next = new List<string>(current.Count - phrase.Length);
                next.AddRange(current.Take(at));
                next.AddRange(current.Skip(at + phrase.Length));
                current = next;
            }
        }

        remaining = current;
        return [.. found];
    }

    private static int IndexOfRun(List<string> tokens, string[] phrase)
    {
        // An edition marker is only a marker when something is left in front of
        // it. "Classic" on its own IS the title (Doom Classic, Tetris Classic),
        // and stripping it to nothing would make every such title match every
        // other.
        for (var i = 1; i + phrase.Length <= tokens.Count; i++)
        {
            var hit = true;
            for (var j = 0; j < phrase.Length; j++)
            {
                if (!string.Equals(tokens[i + j], phrase[j], StringComparison.Ordinal))
                {
                    hit = false;
                    break;
                }
            }

            if (hit)
            {
                return i;
            }
        }

        return -1;
    }

    private static List<string> DropArticles(List<string> tokens)
    {
        var result = new List<string>(tokens.Count);
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            // "The" is dropped wherever it appears ("The Legend of Zelda",
            // "Lord of the Rings"); "a"/"an" only in front, because "a" turns up
            // mid-title as a real word far too often to delete blindly.
            if (Articles.Contains(token, StringComparer.Ordinal)
                || (i == 0 && LeadingArticles.Contains(token, StringComparer.Ordinal)))
            {
                continue;
            }

            result.Add(token);
        }

        // A title that is nothing BUT articles ("The The") keeps them: an empty
        // core matches nothing and would be dropped by the title floor anyway,
        // but silently emptying a real title is worse than keeping it odd.
        return result.Count == 0 ? tokens : result;
    }

    private static bool IsAsciiDigits(string token)
    {
        foreach (var c in token)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return token.Length > 0;
    }
}
