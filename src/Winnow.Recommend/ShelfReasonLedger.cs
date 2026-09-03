using System.Globalization;

// One surface's memory of what has been said. A shelf is rendered in score
// order, card by card, and without this memory each card picks its phrasing
// and its supporting fact independently of its neighbours.
//
// TASK-71 (variant deduplication, 2026-09-02). The phrasing of a card is
// chosen by hashing its release id, which is deterministic and per-card and
// knows nothing about the shelf. Two cards can land on the same variant of
// the same clause; observed on the "Patched while you were away" shelf where
// Stationeers and PEAK drew the identical sentence. ClaimVariant is the
// shelf's memory of which (signal, clause, variant) triple has been spoken.
// The hash still chooses first; a card only moves off its own pick when that
// pick is taken, so the first card on a shelf always renders what it would
// have rendered without the ledger.
//
// TASK-76 (fact deduplication, 2026-09-02). The variant ledger tracks which
// phrasing was used, not which fact the phrasing cites. A single facet kept
// appearing on every card even though every sentence differed. Observed on
// the same six-card shelf:
//
//   Stormworks      '...landing in Sandbox, a kind of game you keep coming back to.'
//   Stationeers     '...and you have real hours in Sandbox games.'
//   Project Gorgon  '...and Sandbox is one of your deepest piles.'
//
// Three of six named the same facet. Every sentence was true and all three
// variants differed, which is exactly why the variant ledger missed it. The
// same ledger now also counts citations: CanCite asks whether the surface
// has room for one more card making this claim, and Cite records one, but
// only once the card has actually said it, so a clause the character budget
// dropped cannot silence the next card. No card is ever given something
// false to say; a card whose strongest fact is already spent reaches for its
// next-strongest, and when it has none left it says less.
//
// Measured on the six-card shelf seeded from the report, before and after:
//   before:  6 of 6 cards named Sandbox
//   after:   2 name Sandbox, 2 fall to the dormancy clause, 2 say less
//
// The ledger is not thread-safe and does not need to be: one surface is
// rendered by one caller in one loop. Determinism survives because the
// surface is filled in a stable order (score, then release id), so the same
// library renders the same shelf on every reload.
namespace Winnow.Recommend;

internal sealed class ShelfReasonLedger
{
    private readonly HashSet<(ReasonSignal Signal, ReasonClause Clause, string Variant)> _spoken = [];
    private readonly Dictionary<string, int> _cited = new(StringComparer.Ordinal);
    private readonly int _citationCap;

    public ShelfReasonLedger(int citationCap) => _citationCap = Math.Max(1, citationCap);

    /// <summary>
    /// How many cards on a surface of <paramref name="surfaceCards"/> may cite
    /// the same supporting fact. Derived per surface because the two surfaces
    /// differ in size (a shelf holds 6, the flat feed holds 20) and a flat cap
    /// would silence the feed. Clamps its own inputs, so a zero or negative
    /// parameter cannot divide by zero or silence every clause.
    /// </summary>
    public static int CapFor(int surfaceCards, RecommendationTuning tuning)
        => Math.Max(
            Math.Max(1, tuning.FactCitationFloor),
            surfaceCards / Math.Max(1, tuning.FactCitationCards));

    /// <summary>
    /// Returns the first unclaimed variant at or after <paramref name="seed"/>,
    /// wrapping around the list. Null when every variant is already spoken, so
    /// the caller can fall back to a different list before repeating.
    /// </summary>
    public string? ClaimVariant(
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

    /// <summary>
    /// Whether this surface has room for one more card citing the fact
    /// identified by (<paramref name="signal"/>, <paramref name="evidence"/>).
    /// Claims that <see cref="Citation"/> maps to null (demotion disclosures)
    /// always pass: those clauses explain why a card ranks where it does, and
    /// withholding one for variety would hide ranking information.
    /// </summary>
    public bool CanCite(ReasonSignal signal, ReasonEvidence evidence)
    {
        var citation = Citation(signal, evidence);
        return citation is null
            || !_cited.TryGetValue(citation, out var used)
            || used < _citationCap;
    }

    /// <summary>
    /// Records one use of a fact. Called only after the card has rendered the
    /// clause, so a clause the character budget dropped does not count against
    /// the cap and cannot silence the next card.
    /// </summary>
    public void Cite(ReasonSignal signal, ReasonEvidence evidence)
    {
        if (Citation(signal, evidence) is { } citation)
        {
            _cited[citation] = _cited.GetValueOrDefault(citation) + 1;
        }
    }

    /// <summary>
    /// Returns a key identifying the claim this (signal, evidence) pair makes,
    /// or null for claims that are never withheld. The unit is the claim, not
    /// the signal and not the phrasing. Taste match includes the descriptor
    /// name because that is the one thing a reader tracks from card to card:
    /// Sandbox and Roguelike are two different claims, two Sandbox cards are
    /// one claim twice. Every other signal is keyed on its signal alone; the
    /// number the clause happens to cite is colour, not the claim. Demotion
    /// disclosures (mode mismatch, fresh play, shown recently) return null,
    /// meaning never withheld: those clauses tell the user why a card ranks
    /// where it does, and withholding one for variety would hide ranking
    /// information rather than repeat it.
    /// </summary>
    private static string? Citation(ReasonSignal signal, ReasonEvidence evidence) => signal switch
    {
        // Demotion disclosures. These say why a card ranks where it does, so
        // withholding one for variety would hide ranking information.
        ReasonSignal.None
            or ReasonSignal.OnlineOnlyMismatch
            or ReasonSignal.SoloOnlyMismatch
            or ReasonSignal.PlayedRecently
            or ReasonSignal.ShownRecently => null,

        // The one claim that names something the reader tracks from card to
        // card, so two descriptors are two different claims.
        ReasonSignal.TasteMatch => string.Create(
            CultureInfo.InvariantCulture,
            $"taste:{evidence.TasteFacetName?.Trim().ToUpperInvariant() ?? string.Empty}"),

        _ => string.Create(CultureInfo.InvariantCulture, $"signal:{(int)signal}"),
    };
}
