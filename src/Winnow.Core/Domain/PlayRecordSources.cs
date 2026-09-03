namespace Winnow.Core.Domain;

/// <summary>
/// Provenance vocabulary for <see cref="PlayRecord.Source"/>. Readers name
/// themselves; this names the one case where the stored row is not a single
/// reader's report.
/// </summary>
public static class PlayRecordSources
{
    /// <summary>Marks a row whose minutes were carried forward rather than observed.</summary>
    public const string CarriedSuffix = "+carried";

    /// <summary>
    /// Labels a record whose minutes were carried forward from an earlier,
    /// higher reading rather than observed by <paramref name="observedSource"/>
    /// directly — the resolver refusing to write a cumulative counter backwards
    /// when the reporting source could not see the whole total. The row
    /// combines one source's context with another's figure, so it is labelled
    /// rather than attributed to either source alone.
    /// </summary>
    public static string Carried(string observedSource) => observedSource + CarriedSuffix;

    /// <summary>Whether <paramref name="source"/> was produced by <see cref="Carried"/>.</summary>
    public static bool IsCarried(string source)
        => source is not null && source.EndsWith(CarriedSuffix, StringComparison.Ordinal);
}
