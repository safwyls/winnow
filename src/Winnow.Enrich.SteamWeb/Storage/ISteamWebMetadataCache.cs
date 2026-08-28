using System.Collections.Concurrent;

namespace Winnow.Enrich.SteamWeb.Storage;

/// <summary>One row of <c>metadata_cache</c>. A null payload is a cached miss.</summary>
/// <param name="PayloadJson">The stored payload, or null when the provider had no record.</param>
/// <param name="FetchedAt">When the row was written (UTC).</param>
public readonly record struct SteamWebCacheEntry(string? PayloadJson, DateTime FetchedAt);

/// <summary>
/// The <c>metadata_cache(provider, provider_id, payload_json, fetched_at)</c>
/// table (§6), as this module uses it. §4.2: cache aggressively — a hit inside
/// the TTL must never reach the network.
///
/// <para>Narrow on purpose. This module stores exactly one row per account, so
/// it needs no bulk read and no miss semantics; the IGDB and Steam-store modules
/// keep their own equally narrow views of the same table for the same reason.</para>
/// </summary>
public interface ISteamWebMetadataCache
{
    /// <summary>
    /// The stored entry, or null when absent. Freshness is the caller's
    /// decision — the store does not know any TTL.
    /// </summary>
    Task<SteamWebCacheEntry?> GetAsync(string provider, string providerId, CancellationToken ct = default);

    /// <summary>Upserts one row.</summary>
    Task SetAsync(
        string provider, string providerId, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default);
}

/// <summary>
/// Non-persistent <see cref="ISteamWebMetadataCache"/>. Useful for tests and for
/// hosts that deliberately run without a database; production wires
/// <see cref="SqliteSteamWebMetadataCache"/>.
/// </summary>
public sealed class InMemorySteamWebMetadataCache : ISteamWebMetadataCache
{
    private readonly ConcurrentDictionary<(string Provider, string Id), SteamWebCacheEntry> _entries = new();

    public Task<SteamWebCacheEntry?> GetAsync(string provider, string providerId, CancellationToken ct = default)
        => Task.FromResult<SteamWebCacheEntry?>(
            _entries.TryGetValue((provider, providerId), out var entry) ? entry : null);

    public Task SetAsync(
        string provider, string providerId, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default)
    {
        _entries[(provider, providerId)] = new SteamWebCacheEntry(payloadJson, fetchedAt.ToUniversalTime());
        return Task.CompletedTask;
    }
}
