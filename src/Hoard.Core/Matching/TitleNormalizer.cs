using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hoard.Core.Matching;

/// <summary>
/// Turns a raw store title into a <see cref="NormalizedTitle"/> (§5.3 step 2,
/// "normalised title"). Pure, deterministic, allocation-cheap, no IO.
///
/// <para><b>Why this lives in Core rather than in Hoard.Resolve.</b> It began as
/// the soft matcher's private machinery, but it is now read by two callers on
/// opposite sides of the app: the matcher (§5.3 step 2) and the read model's
/// demo consolidation (<see cref="Hoard.Core.Queries.DemoConsolidation"/>,
/// which runs inside the derived-bucket query). §5.3 turns on there being ONE
/// answer to "is this the same title?" — a second normaliser in the query layer
/// would be a second, silently diverging opinion about sequel ordinals and
/// edition markers, which is the machinery that stops <c>Portal Demo</c> from
/// binding to <c>Portal 2</c>. It is pure and BCL-only, so it sits inside
/// Core's charter unchanged, and the dependency graph stays acyclic: nothing
/// had to start depending on Hoard.Resolve to reuse it.</para>
///
/// <para>The pipeline, in order — the order matters:</para>
/// <list type="number">
///   <item>strip trademark/registered/service marks (<c>Assassin's Creed® IV Black Flag™</c>);</item>
///   <item>lift a parenthesised year out into <see cref="NormalizedTitle.ParsedYear"/>;</item>
///   <item>fold accents via NFD + non-spacing-mark removal (<c>Pokémon</c> → <c>pokemon</c>);</item>
///   <item>lower-case invariantly;</item>
///   <item>delete apostrophes and full stops so <c>Assassin's</c> → <c>assassins</c> and
///         <c>S.T.A.L.K.E.R.</c> → <c>stalker</c>, rather than shattering into letters;</item>
///   <item>expand <c>&amp;</c> to <c>and</c>;</item>
///   <item>every other non-alphanumeric becomes a space — this is what collapses subtitle
///         separators (<c>:</c>, <c>-</c>, <c>–</c>, <c>—</c>, <c>|</c>, <c>~</c>);</item>
///   <item>fold roman numerals to arabic (<c>The Witcher III</c> ≡ <c>The Witcher 3</c>);</item>
///   <item>extract and remove edition markers, longest phrase first;</item>
///   <item>drop articles;</item>
///   <item>fold spelled-out cardinals to arabic (<c>Episode One</c> ≡ <c>Episode 1</c>) —
///         last, so <c>Day One Edition</c> is lifted out as an edition marker before
///         its <c>one</c> can be read as a number.</item>
/// </list>
///
/// <para><b>Why NFD and not NFKD.</b> NFKD would expand <c>™</c> to the letters
/// <c>TM</c> and glue them onto the previous word. The marks are deleted in
/// step 1 precisely so that never happens.</para>
/// </summary>
public static partial class TitleNormalizer
{
    /// <summary>Marks deleted outright (never turned into spaces or letters).</summary>
    private const string MarkChars = "™®©℗℠";

    /// <summary>Deleted so the surrounding letters join up.</summary>
    private const string JoinChars = "'’‘ʼ`.";

    /// <summary>Roman numerals only fold inside a plausible sequel range.</summary>
    private const int MaxRomanOrdinal = 30;

    /// <summary>
    /// Edition markers meaning "a separate build". Skyrim Special Edition is not
    /// Skyrim: different executable, different achievement set, different mod
    /// ecosystem (§9 pitfall 5). Longest phrase wins, so <c>special edition</c>
    /// is matched before the bare <c>edition</c> fallback.
    /// </summary>
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

    /// <summary>
    /// Edition markers meaning "same build, more content". A disagreement here
    /// costs a small penalty, not a veto — The Witcher 3 and The Witcher 3 GOTY
    /// are a merge the user plausibly wants, and it is their call.
    /// </summary>
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
    /// Spelled-out cardinals, folded to arabic so they reach
    /// <see cref="NormalizedTitle.Ordinals"/> and are compared exactly.
    ///
    /// <para>Without this the ordinal veto was inconsistent with itself:
    /// <c>Half-Life 2: Episode II</c> vs <c>Episode III</c> was caught, because
    /// roman numerals fold, while <c>Episode One</c> vs <c>Episode Two</c> both
    /// reduced to ordinals <c>[2]</c> — the "2" of Half-Life 2 — scored 0.84 on
    /// title similarity and queued two games nobody would confuse.</para>
    ///
    /// <para>Twenty is the ceiling for the same reason
    /// <see cref="MaxRomanOrdinal"/> is thirty: past that the word is prose, not
    /// a sequel number. <c>zero</c> is deliberately absent — <c>Katana Zero</c>,
    /// <c>Zero Escape</c> and <c>Ground Zero</c> are names.</para>
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

    /// <summary>
    /// Publisher names carry legal-form noise that varies per store feed
    /// ("Bethesda Softworks" / "Bethesda Softworks LLC"). Normalise the same way
    /// as a title and drop those suffixes before comparing.
    /// </summary>
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
    /// Folds roman numerals to arabic so <c>Dark Souls III</c> and
    /// <c>Dark Souls 3</c> normalise identically — and, more importantly, so
    /// <c>Dark Souls II</c> and <c>Dark Souls III</c> produce ordinals 2 and 3
    /// that can be compared exactly.
    ///
    /// <para>Three guards keep the fold from firing on ordinary words. Values above
    /// <see cref="MaxRomanOrdinal"/> are left alone, which rules out <c>mix</c>
    /// (= 1009) and bare <c>l</c>/<c>c</c>/<c>d</c>/<c>m</c>; and two guards
    /// cover the single-letter numerals, which are also just letters:</para>
    /// <list type="bullet">
    ///   <item><b>Leading position.</b> A one-letter token at the front of a
    ///     title is a word or a brand mark, never a sequel number:
    ///     <c>I Am Setsuna</c>, <c>V Rising</c>, <c>X Rebirth</c>.</item>
    ///   <item><b>Bare <c>x</c>, anywhere.</b> <c>X</c> is used as a name at
    ///     least as often as it is used for ten — <c>Mega Man X</c> is a
    ///     different series from <c>Mega Man</c>, not its tenth entry. Folding
    ///     it made <c>Mega Man X</c> and <c>Mega Man 10</c> normalise to the
    ///     same string, scoring 1.00 title similarity with no veto able to
    ///     separate them, because at that point nothing distinguishable is
    ///     left. <c>v</c> folds because it does not collide this way:
    ///     <c>Grand Theft Auto V</c> and <c>Grand Theft Auto 5</c> are one game
    ///     written two ways, and no <c>Grand Theft Auto 5</c> sequel exists to
    ///     be confused with.
    ///     <para>The cost is real and deliberate: <c>Final Fantasy X</c> no
    ///     longer matches <c>Final Fantasy 10</c>, and instead of a wrong
    ///     merge candidate the user simply never sees that pair. §5.3 is
    ///     precision over recall, always.</para></item>
    /// </list>
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
    /// Folds a spelled-out cardinal to arabic, so <c>Episode One</c> and
    /// <c>Episode 1</c> produce the same ordinal and <c>Episode One</c> and
    /// <c>Episode Two</c> produce different ones.
    ///
    /// <para>The leading token is never folded, for the same reason the roman
    /// fold leaves a leading <c>i</c> alone: at the front of a title the word
    /// is the name. <c>Five Nights at Freddy's</c>, <c>Two Point Hospital</c>,
    /// <c>Seven Kingdoms</c>, <c>One Piece</c> and <c>Nine Sols</c> all start
    /// with a number word and none of them is a sequel number.</para>
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

    /// <summary>
    /// Removes every occurrence of a known edition phrase, longest first, and
    /// returns the canonical markers found. Phrases are matched as contiguous
    /// token runs anywhere in the title, not just at the end — GOG puts them in
    /// the middle often enough ("Fallout 2 Classic Edition Bundle").
    /// </summary>
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
