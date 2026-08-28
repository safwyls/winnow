using Dapper;
using Winnow.Data;
using Winnow.Ingest.Epic.Web;

namespace Winnow.App.Services;

/// <summary>
/// Persists Epic catalog answers in the §6 <c>metadata_cache</c> table, beside
/// IGDB's, steamcmd's and the Steam store's.
///
/// <para><b>Why this lives in the composition root rather than in the Epic
/// module.</b> <c>Winnow.Ingest.Epic</c> deliberately does not reference
/// <c>Winnow.Data</c> — its §5.1 job is to read a source and emit candidates, and
/// dragging the data layer into it to cache one lookup would be a poor trade for
/// a boundary that has held all the way through. So the module ships an
/// in-memory default and declares the seam; the host, which already references
/// both, fills it in. This is the same arrangement the interface documents.</para>
///
/// <para><b>Why it is worth persisting at all, when the library cache is not.</b>
/// The two answer different kinds of question. The owned library is account
/// state with a six-hour TTL, so a restart costs one refetch and nothing is
/// gained by keeping it. A catalog answer is a property of the product — what
/// this catalog item is called and what kind of thing it is — and does not change
/// on any timescale a launch cycle cares about. Holding it only in memory would
/// mean an authenticated request per Epic work per launch, forever, to relearn
/// <c>public,games,applications</c>.</para>
///
/// <para><b>A null payload is a cached MISS, not an empty row.</b> The column is
/// nullable and stores exactly that: the service answered and does not recognise
/// this catalog item. Absence of the row is the different thing — never asked.
/// The client relies on both, and this class must not collapse them.</para>
///
/// <para>Nothing account-identifying is stored: the payload is a catalog id, a
/// namespace, an artifact codename, a title and category paths.</para>
/// </summary>
public sealed class SqliteEpicCatalogCache : IEpicCatalogCache
{
    /// <summary>
    /// <c>metadata_cache.provider</c> for these rows. Namespaced away from
    /// <c>epic</c> so a future cache of a different Epic endpoint cannot collide
    /// with it on the same <c>(provider, provider_id)</c> primary key — the
    /// provider_id here is a catalog item id, and another endpoint keyed by the
    /// same id would silently overwrite these.
    /// </summary>
    public const string Provider = "epic-catalog";

    private readonly ISqliteConnectionFactory _factory;

    public SqliteEpicCatalogCache(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<EpicCatalogCacheEntry?> GetAsync(string catalogItemId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var row = await lease.Connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
            SELECT payload_json AS PayloadJson, fetched_at AS FetchedAt
            FROM metadata_cache
            WHERE provider = @Provider AND provider_id = @catalogItemId;
            """,
            new { Provider, catalogItemId },
            transaction: lease.Transaction,
            cancellationToken: ct));

        return row is null ? null : new EpicCatalogCacheEntry(row.PayloadJson, row.FetchedAt);
    }

    public async Task SetAsync(
        string catalogItemId, string? payloadJson, DateTime fetchedAt, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO metadata_cache (provider, provider_id, payload_json, fetched_at)
            VALUES (@Provider, @catalogItemId, @payloadJson, @fetchedAt)
            ON CONFLICT(provider, provider_id) DO UPDATE SET
                payload_json = excluded.payload_json,
                fetched_at   = excluded.fetched_at;
            """,
            new { Provider, catalogItemId, payloadJson, fetchedAt = fetchedAt.ToUniversalTime() },
            transaction: lease.Transaction,
            cancellationToken: ct));
    }

    private sealed class Row
    {
        public string? PayloadJson { get; init; }

        public DateTime FetchedAt { get; init; }
    }
}
