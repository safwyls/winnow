namespace Winnow.Core.Matching;

/// <summary>
/// The structured result of running a raw store title through
/// <see cref="TitleNormalizer"/>.
///
/// <para>Normalisation deliberately does NOT flatten everything into one
/// string. Two of the four things it pulls out — the sequel ordinal and the
/// edition marker — are the signals that separate genuinely different games
/// whose titles are almost identical (§5.3, §9 pitfall 5). Fold them into the
/// core string and <c>Portal</c> / <c>Portal 2</c> come out 0.86 similar, which
/// is exactly how a fuzzy matcher talks itself into a wrong merge.</para>
/// </summary>
/// <param name="Original">The title as supplied, untouched. Shown in the merge-confirm UI.</param>
/// <param name="Core">
/// Space-joined comparable tokens: case-folded, de-accented, de-punctuated,
/// articles dropped, roman numerals folded to arabic, edition suffix removed,
/// parenthesised year removed. This is the only part string similarity sees.
/// </param>
/// <param name="Tokens">The core, tokenised. Used for the token-overlap half of the similarity.</param>
/// <param name="Ordinals">
/// Numeric tokens of the core, in order — the sequel number. <c>Portal 2</c>
/// yields <c>[2]</c>, <c>Portal</c> yields <c>[]</c>, <c>Left 4 Dead 2</c>
/// yields <c>[4, 2]</c>. Compared exactly, never fuzzily.
/// </param>
/// <param name="RebuildEditions">
/// Edition markers that denote a SEPARATE BUILD — Special Edition, Remastered,
/// Anniversary. These are different <c>Release</c>s with different achievement
/// sets and mod ecosystems; merging them is a bug (§9 pitfall 5), so a
/// disagreement here vetoes the pair outright.
/// </param>
/// <param name="BundleEditions">
/// Edition markers that denote the SAME BUILD plus content — GOTY, Complete,
/// Deluxe, Director's Cut. A disagreement is a mild penalty, not a veto: the
/// user may well want The Witcher 3 and The Witcher 3 GOTY treated as one.
/// </param>
/// <param name="ParsedYear">
/// A parenthesised four-digit year lifted out of the title, as in
/// <c>Prey (2006)</c> — the disambiguation convention IGDB and Wikipedia both
/// use. Only parenthesised years are lifted; a bare trailing year is left in
/// place so <c>Madden NFL 2004</c> and <c>Madden NFL 2005</c> stay distinct.
/// </param>
public sealed record NormalizedTitle(
    string Original,
    string Core,
    IReadOnlyList<string> Tokens,
    IReadOnlyList<int> Ordinals,
    IReadOnlyList<string> RebuildEditions,
    IReadOnlyList<string> BundleEditions,
    int? ParsedYear)
{
    /// <summary>True when normalisation left nothing comparable behind.</summary>
    public bool IsEmpty => Core.Length == 0;
}
