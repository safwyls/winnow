namespace Winnow.Enrich.Updates.Storage;

/// <summary>One row of <c>metadata_cache</c>. A null payload is a cached miss.</summary>
/// <param name="PayloadJson">The stored payload, or null when the provider had no record.</param>
/// <param name="FetchedAt">When the row was written (UTC).</param>
public readonly record struct UpdateCacheEntry(string? PayloadJson, DateTime FetchedAt);

/// <summary>
/// The <c>metadata_cache(provider, provider_id, payload_json, fetched_at)</c>
/// table (§6), as this module uses it. Three distinct things live here under
/// three <c>provider</c> values:
///
/// <list type="bullet">
/// <item><c>steam-news</c> — per-appid "this app has no news feed" negatives,
/// the cache that makes a 403 cost one request per quarter instead of one per
/// sweep.</item>
/// <item><c>steamcmd</c> — verbatim build-info bodies, so a re-triggered cascade
/// inside the TTL costs the volunteer service nothing.</item>
/// <item><c>update-poll</c> — per-appid poll state: the news high-water mark, the
/// last build timestamp seen, the watch-list deadline, and (as
/// <c>fetched_at</c>) when the app was last polled.</item>
/// </list>
///
/// <para><b>Poll state lives here rather than in <c>settings</c> deliberately.</b>
/// It is per-appid and carries a natural "when did we last look" timestamp,
/// which is exactly the shape <c>metadata_cache</c> already has and exactly the
/// shape <c>settings(key, value)</c> does not — putting a few hundred
/// <c>updates.poll.app.570</c> keys beside the IGDB credentials would be using a
/// scalar table as a row store. Neither needs a new table, which is the
/// constraint that mattered.</para>
///
/// <para>Losing this table is safe by construction: clearing the cache resets
/// every high-water mark, the next sweep re-observes the same newest items, and
/// the <c>ux_update_events_identity</c> index (migration 0004) turns the re-write
/// into a no-op rather than a duplicate. State loss costs requests, never
/// correctness.</para>
///
/// <para>A deliberate peer of the IGDB and Steam-store equivalents rather than a
/// shared type: §5.1 keeps <c>Enrich.*</c> modules independent, and this one has
/// to work when neither of those is configured.</para>
/// </summary>
public interface IUpdateSignalCache
{
    /// <summary>
    /// The stored entry, or null when absent. Freshness is the caller's
    /// decision — the store knows no TTL.
    /// </summary>
    Task<UpdateCacheEntry?> GetAsync(string provider, string providerId, CancellationToken ct = default);

    /// <summary>Bulk form of <see cref="GetAsync"/>; one round trip for a whole batch.</summary>
    Task<IReadOnlyDictionary<string, UpdateCacheEntry>> GetManyAsync(
        string provider, IEnumerable<string> providerIds, CancellationToken ct = default);

    /// <summary>Upserts one row. A null <paramref name="payloadJson"/> records a miss.</summary>
    Task SetAsync(
        string provider, string providerId, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default);
}
