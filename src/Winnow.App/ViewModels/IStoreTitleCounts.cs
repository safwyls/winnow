namespace Winnow.App.ViewModels;

/// <summary>
/// Per-store title counts across the whole library. Counts are per tile,
/// not per release, so a game owned on two stores counts in both (§11.2).
///
/// <para>Identity links are deliberately NOT resolved here. The question is
/// "how many titles are on this store", and a game owned on Steam and on
/// Epic is genuinely on both. Resolving would make the per-store numbers
/// stop adding up to the library total, and design-system §11.2 says
/// plainly that the count is per tile.</para>
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
