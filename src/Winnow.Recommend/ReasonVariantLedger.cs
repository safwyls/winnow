namespace Winnow.Recommend;

// Tracks which variants one shelf has already used.
//
// The phrasing of a card is chosen by hashing its release id, which is
// deterministic and per-card and knows nothing about its neighbours, so two
// cards on one shelf can land on the same variant. Observed 2026-09-02 on the
// "Patched while you were away" shelf: Stationeers and PEAK both read "This is
// not the game you put down, an update arrived after you left, and you have
// real hours in [facet] games."
//
// The ledger is passed down one shelf's render and remembers which variant of
// which (signal, clause) has already been spoken. The hash still chooses
// first; a card is only moved off its own pick when that pick is taken, and
// then only as far as the next unclaimed variant in the same list. So the
// first card on a shelf always renders exactly what it rendered before this
// existed, and a shelf whose cards already differ is untouched.
//
// This is deterministic per feed build rather than per card: the shelf is
// filled in a stable order (score, then release id), so the same library
// renders the same shelf on every reload. It is NOT stable against a card
// ahead of it changing position, which is the price of not repeating and is
// the right way round — a user notices two identical sentences side by side
// and does not notice that a sentence differs from yesterday's.
//
// Not thread-safe, and does not need to be: one shelf is rendered by one
// caller in one loop.
internal sealed class ReasonVariantLedger
{
    private readonly HashSet<(ReasonSignal Signal, ReasonClause Clause, string Variant)> _spoken = [];

    /// <summary>
    /// Returns the first unclaimed variant at or after <paramref name="seed"/>,
    /// wrapping around the list. Null when every variant is already spoken, so
    /// the caller can fall back to a different list before repeating.
    /// </summary>
    public string? Claim(
        ReasonSignal signal, ReasonClause clause, IReadOnlyList<string> variants, int seed)
    {
        if (variants.Count == 0)
        {
            return null;
        }

        var start = ((seed % variants.Count) + variants.Count) % variants.Count;
        for (var step = 0; step < variants.Count; step++)
        {
            var variant = variants[(start + step) % variants.Count];
            if (_spoken.Add((signal, clause, variant)))
            {
                return variant;
            }
        }

        return null;
    }
}
