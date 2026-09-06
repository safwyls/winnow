using Dapper;
using Winnow.App.Services;
using Winnow.Enrich.GamesDb.Storage;
using Winnow.Enrich.Igdb.Storage;
using Winnow.Enrich.Steam.Storage;
using Winnow.Enrich.SteamWeb.Storage;
using Winnow.Enrich.Updates.Storage;
using Xunit;

namespace Winnow.Tests;

public sealed class CacheTimestampBoundaryTests
{
    [Theory]
    [InlineData("steam-web")]
    [InlineData("gamesdb")]
    [InlineData("igdb")]
    [InlineData("updates")]
    [InlineData("steam-store")]
    [InlineData("epic")]
    public async Task Cache_rejects_unspecified_timestamp_without_inserting_or_overwriting(string cache)
    {
        using var db = new TempDatabase();
        Func<string, DateTime, Task> write = cache switch
        {
            "steam-web" => (payload, at) => new SqliteSteamWebMetadataCache(db.Factory).SetAsync(cache, "1", payload, at),
            "gamesdb" => (payload, at) => new SqliteGamesDbCache(db.Factory).SetAsync("1", payload, at),
            "igdb" => (payload, at) => new SqliteMetadataCache(db.Factory).SetAsync(cache, "1", payload, at),
            "updates" => (payload, at) => new SqliteUpdateSignalCache(db.Factory).SetAsync(cache, "1", payload, at),
            "steam-store" => (payload, at) => new SqliteStoreMetadataCache(db.Factory).SetAsync(cache, "1", payload, at),
            "epic" => (payload, at) => new SqliteEpicCatalogCache(db.Factory).SetAsync("1", payload, at),
            _ => throw new ArgumentOutOfRangeException(nameof(cache)),
        };
        var unspecified = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Unspecified);
        await Assert.ThrowsAsync<ArgumentException>(() => write("rejected", unspecified));
        using var connection = db.Factory.Open();
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT count(*) FROM metadata_cache;"));

        var utc = DateTime.SpecifyKind(unspecified, DateTimeKind.Utc).AddTicks(1234567);
        await write("original", utc.ToLocalTime());
        Assert.Equal(utc, connection.QuerySingle<DateTime>("SELECT fetched_at FROM metadata_cache;"));
        await Assert.ThrowsAsync<ArgumentException>(() => write("rejected", unspecified));
        Assert.Equal("original", connection.QuerySingle<string>("SELECT payload_json FROM metadata_cache;"));
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT count(*) FROM metadata_cache;"));
    }
}
