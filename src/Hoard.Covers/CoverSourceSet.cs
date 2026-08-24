namespace Hoard.Covers;

/// <summary>
/// The identity of the set of sources that could have answered for a key.
///
/// <para>A negative marker means "every source declined", and that sentence is
/// only true relative to the sources that existed when it was written. Steam's
/// capsule alone declines for roughly 96 of a 616-game library — mostly delisted
/// games, which is exactly the population this product exists to surface — and
/// IGDB has cover art for two thirds of them. Registering IGDB afterwards must
/// therefore reopen those 96 questions. Without an identity on the marker it
/// would not: <see cref="CoverDiskCache.IsKnownMissing"/> would keep suppressing
/// the retry for the full <see cref="CoverCacheOptions.NegativeTtl"/> and the
/// user would have to delete their cache by hand to see the fix, re-downloading
/// the 520 covers that were never in question.</para>
///
/// <para>So the identity is stamped into the marker file and compared on read.
/// Change the source set and every prior marker stops matching — one at a time,
/// as each key is asked about, with no startup sweep and no bulk re-download.
/// It is the same trick <see cref="CoverDiskCache.FloorVariantVersion"/> plays
/// with the floor variant, applied to the question rather than the answer.</para>
/// </summary>
public static class CoverSourceSet
{
    /// <summary>Identity used when no registered source will look at the key at all.</summary>
    public const string NoSources = "(none)";

    /// <summary>Longest identity that will be read back off disk. A guard, not a limit.</summary>
    internal const int MaxLength = 512;

    /// <summary>
    /// The identity of every source that <see cref="ICoverSource.CanHandle"/>s
    /// <paramref name="key"/>, in a canonical order.
    /// </summary>
    public static string Identity(IEnumerable<ICoverSource> sources, CoverKey key)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var ids = new List<string>(4);
        foreach (var source in sources)
        {
            if (source.CanHandle(key))
            {
                ids.Add(source.SourceSetId);
            }
        }

        return Identity(ids);
    }

    /// <summary>
    /// Joins source identities into the canonical form. Sorted, because
    /// registration order decides which source <em>wins</em> and has no bearing
    /// on whether they all declined — reordering Steam and IGDB must not throw
    /// away a month of correctly recorded negatives.
    /// </summary>
    public static string Identity(IEnumerable<string> sourceIds)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);

        var ids = sourceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
        {
            return NoSources;
        }

        Array.Sort(ids, StringComparer.Ordinal);
        var joined = string.Join('+', ids);
        return joined.Length <= MaxLength ? joined : joined[..MaxLength];
    }
}
