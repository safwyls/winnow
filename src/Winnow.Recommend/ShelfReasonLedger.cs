namespace Winnow.Recommend;

/// <summary>Remembers primary wording within one shelf or flat feed, rendered in stable order.</summary>
internal sealed class ShelfReasonLedger
{
    private readonly HashSet<(ReasonSignal Signal, string Variant)> _spoken = [];

    /// <summary>
    /// Returns the first unclaimed variant at or after <paramref name="seed"/>,
    /// wrapping around the list. Null when every variant is already spoken, so
    /// the caller can fall back to a different list before repeating.
    /// </summary>
    public string? ClaimVariant(
        ReasonSignal signal, IReadOnlyList<string> variants, int seed)
    {
        if (variants.Count == 0)
        {
            return null;
        }

        var start = ((seed % variants.Count) + variants.Count) % variants.Count;
        for (var step = 0; step < variants.Count; step++)
        {
            var variant = variants[(start + step) % variants.Count];
            if (_spoken.Add((signal, variant)))
            {
                return variant;
            }
        }

        return null;
    }

}
