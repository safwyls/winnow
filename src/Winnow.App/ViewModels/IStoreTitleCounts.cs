namespace Winnow.App.ViewModels;

/// <summary>
/// Per-store title counts across the whole library. Counts are per tile,
/// not per release, so a game owned on two stores counts in both (§11.2).
/// </summary>
public interface IStoreTitleCounts
{
    /// <summary>
    /// Titles per store, keyed by the store as stored (<c>"steam"</c>,
    /// <c>"epic"</c>, <c>"gog"</c>). Empty before the library has loaded — which
    /// the caller must treat as "not known yet" and not as zero.
    /// </summary>
    IReadOnlyDictionary<string, int> TitlesByStore();
}
