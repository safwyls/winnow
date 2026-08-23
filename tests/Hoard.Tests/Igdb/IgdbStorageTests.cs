using Hoard.Enrich.Igdb;
using Hoard.Enrich.Igdb.Storage;
using Xunit;

namespace Hoard.Tests.Igdb;

/// <summary>
/// The SQLite-backed stores against the real schema (§6:
/// <c>metadata_cache(provider, provider_id, payload_json, fetched_at)</c> and
/// <c>settings(key, value)</c>), so the in-memory doubles the other IGDB tests
/// use are not the only thing ever exercised.
/// </summary>
public class IgdbStorageTests
{
    [Fact]
    public async Task Settings_round_trip_through_the_settings_table()
    {
        using var db = new TempDatabase();
        var store = new SqliteSettingsStore(db.Factory);

        Assert.Null(await store.GetAsync("igdb.client_id"));

        await store.SetAsync("igdb.client_id", "abc");
        Assert.Equal("abc", await store.GetAsync("igdb.client_id"));

        // Upsert, not a duplicate-key failure.
        await store.SetAsync("igdb.client_id", "def");
        Assert.Equal("def", await store.GetAsync("igdb.client_id"));

        await store.RemoveAsync("igdb.client_id");
        Assert.Null(await store.GetAsync("igdb.client_id"));
    }

    [Fact]
    public async Task Metadata_cache_round_trips_payload_and_fetched_at()
    {
        using var db = new TempDatabase();
        var cache = new SqliteMetadataCache(db.Factory);
        var fetchedAt = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        await cache.SetAsync(IgdbClient.CacheProvider, "steam-app:440", "{\"igdb_id\":225}", fetchedAt);

        var entry = await cache.GetAsync(IgdbClient.CacheProvider, "steam-app:440");
        Assert.NotNull(entry);
        Assert.Equal("{\"igdb_id\":225}", entry.Value.PayloadJson);
        Assert.Equal(fetchedAt, entry.Value.FetchedAt);
    }

    [Fact]
    public async Task Metadata_cache_stores_a_miss_as_a_null_payload()
    {
        using var db = new TempDatabase();
        var cache = new SqliteMetadataCache(db.Factory);

        await cache.SetAsync(IgdbClient.CacheProvider, "steam-app:999999", null, DateTime.UtcNow);

        var entry = await cache.GetAsync(IgdbClient.CacheProvider, "steam-app:999999");

        // Present but empty: "asked, and IGDB had nothing" — distinct from
        // "never asked", which returns no entry at all.
        Assert.NotNull(entry);
        Assert.Null(entry.Value.PayloadJson);
    }

    [Fact]
    public async Task Bulk_read_returns_only_the_rows_that_exist_and_chunks_large_batches()
    {
        using var db = new TempDatabase();
        var cache = new SqliteMetadataCache(db.Factory);
        var fetchedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Over SQLite's 999-parameter ceiling: the store must chunk rather than
        // throw, because a real library resolves in batches this size.
        var stored = Enumerable.Range(0, 1200).Select(i => "steam-app:" + i).ToArray();
        foreach (var id in stored)
        {
            await cache.SetAsync(IgdbClient.CacheProvider, id, "{}", fetchedAt);
        }

        var requested = stored.Concat(["steam-app:missing"]).ToArray();
        var entries = await cache.GetManyAsync(IgdbClient.CacheProvider, requested);

        Assert.Equal(1200, entries.Count);
        Assert.False(entries.ContainsKey("steam-app:missing"));
        Assert.Equal(fetchedAt, entries["steam-app:0"].FetchedAt);
    }

    [Fact]
    public async Task Cache_is_partitioned_by_provider()
    {
        using var db = new TempDatabase();
        var cache = new SqliteMetadataCache(db.Factory);

        await cache.SetAsync("igdb", "steam-app:440", "igdb-payload", DateTime.UtcNow);
        await cache.SetAsync("steam-appdetails", "steam-app:440", "steam-payload", DateTime.UtcNow);

        Assert.Equal("igdb-payload", (await cache.GetAsync("igdb", "steam-app:440"))!.Value.PayloadJson);
        Assert.Equal(
            "steam-payload", (await cache.GetAsync("steam-appdetails", "steam-app:440"))!.Value.PayloadJson);
    }

    [Fact]
    public async Task Igdb_client_persists_credentials_and_token_through_the_real_settings_table()
    {
        using var db = new TempDatabase();
        var settings = new SqliteSettingsStore(db.Factory);
        var cache = new SqliteMetadataCache(db.Factory);

        using (var first = new IgdbTestHost(
            IgdbTestHost.DefaultResponder(), settings: settings, cache: cache))
        {
            await first.Client.ResolveBySteamAppIdsAsync(["440", "570"]);
            Assert.Equal(1, first.Handler.CountFor("token"));
        }

        // Everything a restart needs is on disk: no re-mint, no re-fetch.
        using var second = new IgdbTestHost(
            IgdbTestHost.DefaultResponder(), settings: settings, cache: cache);
        var matches = await second.Client.ResolveBySteamAppIdsAsync(["440", "570"]);

        Assert.Equal(2, matches.Count);
        Assert.Empty(second.Handler.Requests);
    }
}
