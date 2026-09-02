using Winnow.Enrich.Steam.Model;

namespace Winnow.Enrich.Steam;

/// <summary>
/// Winnow's window onto Steam's undocumented, keyless store-frontend endpoints.
/// Fallback to IGDB; every method is total (yields empty on failure).
/// </summary>
public interface ISteamStoreClient
{
    /// <summary>
    /// Store names and tag ranks for Steam appids.
    ///
    /// <para>Appids are batched <see cref="SteamStoreOptions.BatchSize"/> at a
    /// time (100 by default), so a 616-game library costs 7 requests rather than
    /// 616. Cached appids — hits and cached misses alike — are removed before
    /// batching, so a warm library costs none at all.</para>
    ///
    /// <para>The name is what M1 wants: it replaces the <c>App &lt;appid&gt;</c>
    /// placeholders that carry <c>works.name_is_provisional = 1</c>.</para>
    /// </summary>
    /// <param name="appIds">Steam appids as strings. Duplicates and non-numeric entries are dropped.</param>
    /// <param name="cacheTtl">Overrides <see cref="SteamStoreOptions.CacheTtl"/> for this call.</param>
    /// <returns>Appid → item, containing only appids the store served successfully.</returns>
    Task<IReadOnlyDictionary<string, SteamStoreItem>> GetItemsAsync(
        IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default);

    /// <summary>
    /// Projects store items out of <c>metadata_cache</c> and touches the
    /// network on no path, not for a miss, not for a stale row, not for an
    /// appid absent from the cache entirely. Age is deliberately not consulted:
    /// the caller is asking what Steam already said, not what Steam says now.
    ///
    /// <para>This is what makes the Steam relation sweep free. The
    /// <c>type</c> and <c>related_items</c> blocks arrive with the query
    /// <c>BuildGetItemsQuery</c> has always sent, so every body already written
    /// carries them and re-projecting the cache recovers the parent pointers
    /// with no new HTTP request. Verified 2026-09-02.</para>
    /// </summary>
    /// <param name="appIds">Steam appids as strings. Duplicates and non-numeric entries are dropped.</param>
    /// <returns>Appid → item, for cached bodies that still project. Cached misses are absent.</returns>
    Task<IReadOnlyDictionary<string, SteamStoreItem>> GetCachedItemsAsync(
        IEnumerable<string> appIds, CancellationToken ct = default);

    /// <summary>
    /// The store's tagid → name vocabulary (446 entries when the spike ran), in
    /// one request, cached for <see cref="SteamStoreOptions.TagListCacheTtl"/>.
    ///
    /// <para>Provided so <see cref="SteamStoreTag.TagId"/> is resolvable; nothing
    /// in Winnow consumes it yet, by decision. Returns
    /// <see cref="SteamTagVocabulary.Empty"/> rather than throwing when the
    /// endpoint cannot be reached, and prefers a stale cached snapshot over an
    /// empty one.</para>
    /// </summary>
    Task<SteamTagVocabulary> GetTagListAsync(TimeSpan? cacheTtl = null, CancellationToken ct = default);

    /// <summary>
    /// Steam's categoryid → name vocabulary (72 entries when verified live on
    /// 2026-08-25), in one keyless request, cached for
    /// <see cref="SteamStoreOptions.StoreCategoryCacheTtl"/>.
    ///
    /// <para>The companion to the category ids
    /// <see cref="SteamStoreItem.Categories"/> already carries — and those ids
    /// need no flag to obtain, so between them this endpoint and the store items
    /// already in the cache supply the whole "Features" and "Hardware support"
    /// side of a filter panel for one request a month.</para>
    ///
    /// <para>Undocumented like its neighbours, so total like its neighbours:
    /// returns <see cref="SteamStoreCategoryVocabulary.Empty"/> rather than
    /// throwing, and prefers a stale cached snapshot over an empty one.</para>
    /// </summary>
    Task<SteamStoreCategoryVocabulary> GetStoreCategoriesAsync(
        TimeSpan? cacheTtl = null, CancellationToken ct = default);
}
