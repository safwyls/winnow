namespace Winnow.Core.Matching;

/// <summary>
/// Structured result of <see cref="TitleNormalizer"/>. Keeps ordinals and edition
/// markers separate from the core so the matcher can weight them independently.
/// </summary>
/// <param name="Original">Title as supplied, untouched.</param>
/// <param name="Core">Space-joined comparable tokens (case-folded, de-accented, articles dropped, editions removed).</param>
/// <param name="Tokens">The core, tokenised.</param>
/// <param name="Ordinals">Numeric tokens (sequel numbers). Compared exactly, never fuzzily.</param>
/// <param name="RebuildEditions">Edition markers meaning a separate build (remaster, etc.). Disagreement vetoes a match.</param>
/// <param name="BundleEditions">Edition markers meaning same build + content (GOTY, etc.). Disagreement is a mild penalty.</param>
/// <param name="ParsedYear">Parenthesised four-digit year lifted from the title, e.g. "Prey (2006)".</param>
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
