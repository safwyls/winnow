using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Data;
using Winnow.Enrich.Steam;
using Winnow.Enrich.Steam.Storage;
using Xunit;

namespace Winnow.Tests.SteamStore;

public sealed class SteamLifecycleClientTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public string Players { get; set; } = """{"response":{"result":1,"player_count":0}}""";
        public string Reviews { get; set; } = """{"success":1,"query_summary":{"total_reviews":9000},"reviews":[]}""";
        public HttpStatusCode NewsStatus { get; set; } = HttpStatusCode.OK;
        public string News { get; set; } = """{"appnews":{"appid":42,"newsitems":[{"appid":42,"feedname":"steam_community_announcements","feed_type":1,"date":1609459200}]}}""";
        public List<Uri> Requests { get; } = [];
        public bool Offline { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri);
            if (Offline) throw new HttpRequestException("Canned offline response");
            var news = uri.AbsolutePath.Contains("GetNewsForApp");
            var body = news ? News : uri.AbsolutePath.Contains("GetNumberOfCurrentPlayers") ? Players :
                uri.AbsolutePath.Contains("appreviews") ? Reviews : """{"response":{"store_items":[{"appid":42,"id":42,"success":15,"visible":false,"name":""}]}}""";
            return Task.FromResult(new HttpResponseMessage(news ? NewsStatus : HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static ServiceProvider Host(Handler handler, StoreTestClock clock, IStoreMetadataCache? cache = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        if (cache is null) services.AddSingleton<IStoreMetadataCache, InMemoryStoreMetadataCache>();
        else services.AddSingleton(cache);
        services.AddSteamStoreEnrichment(o => { o.RequestsPerSecond = 1000; o.RetryBaseDelay = TimeSpan.FromMilliseconds(1); });
        services.AddHttpClient<ISteamStoreClient, SteamStoreClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHttpClient<ISteamLifecycleClient, SteamLifecycleClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Independent_sources_keep_original_timestamp_and_store_miss_is_unknown()
    {
        var clock = new StoreTestClock(new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero));
        var handler = new Handler();
        using var host = Host(handler, clock);
        var client = host.GetRequiredService<ISteamLifecycleClient>();
        var first = await client.GetAsync("42");
        Assert.Equal(4, first.Count);
        Assert.Equal(0, first.Single(s => s.Source == "steam-players").Signals.CurrentPlayers);
        Assert.Equal(0, first.Single(s => s.Source == "steam-reviews").Signals.RecentReviewCount);
        Assert.DoesNotContain(first, s => s.Signals.StoreListed.HasValue);
        Assert.Equal(new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc), first.Single(s => s.Source == "steam-patchnotes").Signals.LastDevelopmentAt);
        Assert.Contains(handler.Requests, u => u.Query.Contains("tags=patchnotes"));
        var calls = handler.Requests.Count;
        clock.Advance(TimeSpan.FromHours(1));
        var second = await client.GetAsync("42");
        Assert.Equal(calls, handler.Requests.Count);
        Assert.Equal(first[0].ObservedAt, second[0].ObservedAt);
    }

    [Fact]
    public async Task Full_recent_review_page_is_not_an_exact_count_and_403_is_cached()
    {
        var clock = new StoreTestClock(new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero));
        var handler = new Handler { NewsStatus = HttpStatusCode.Forbidden, Players = """{"response":{"result":1,"player_count":-1}}""",
            Reviews = JsonSerializer.Serialize(new { success = 1, reviews = Enumerable.Range(0, 100).Select(_ => new { timestamp_created = clock.Now.ToUnixTimeSeconds() }) }) };
        using var host = Host(handler, clock);
        var client = host.GetRequiredService<ISteamLifecycleClient>();
        var first = await client.GetAsync("42");
        Assert.Null(Assert.Single(first).Signals.RecentReviewCount);
        Assert.Equal(2, handler.Requests.Count(u => u.AbsolutePath.Contains("GetNewsForApp")));
        await client.GetAsync("42");
        Assert.Equal(2, handler.Requests.Count(u => u.AbsolutePath.Contains("GetNewsForApp")));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"appnews\":{\"appid\":99,\"newsitems\":[]}}")]
    public async Task Invalid_news_does_not_erase_successful_activity(string news)
    {
        using var host = Host(new Handler { News = news }, new(new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero)));
        var result = await host.GetRequiredService<ISteamLifecycleClient>().GetAsync("42");
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, s => s.Signals.LastCommunicationAt.HasValue || s.Signals.LastDevelopmentAt.HasValue);
    }

    [Fact]
    public async Task Persisted_review_evidence_keeps_dates_but_omits_prose_and_author_profiles()
    {
        var clock = new StoreTestClock(new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero));
        var handler = new Handler { Reviews = JsonSerializer.Serialize(new { success = 1,
            reviews = new[] { new { timestamp_created = clock.Now.AddDays(-1).ToUnixTimeSeconds(),
                review = "Unrelated review prose", author = new { steamid = "76561198000000001" } } } }) };
        using var host = Host(handler, clock);
        var client = host.GetRequiredService<ISteamLifecycleClient>();
        var review = (await client.GetAsync("42")).Single(s => s.Source == "steam-reviews");
        Assert.Equal(1, review.Signals.RecentReviewCount);
        Assert.DoesNotContain("Unrelated", review.RawJson);
        Assert.DoesNotContain("76561198000000001", review.RawJson);
        using var evidence = JsonDocument.Parse(review.RawJson);
        Assert.True(evidence.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(clock.Now.AddDays(-1).ToUnixTimeSeconds(), evidence.RootElement.GetProperty("timestamp_created")[0].GetInt64());
        Assert.Equal(review.RawJson, (await client.GetAsync("42")).Single(s => s.Source == "steam-reviews").RawJson);
    }

    [Fact]
    public async Task Cold_and_restarted_warm_SQLite_cache_contains_only_the_minimal_review_projection()
    {
        using var db = new TempDatabase();
        var clock = new StoreTestClock(new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero));
        var handler = new Handler { Reviews = ReviewBody(clock.Now.AddDays(-1).ToUnixTimeSeconds(), 1) };
        SteamLifecycleSnapshot first;
        using (var host = Host(handler, clock, new SqliteStoreMetadataCache(db.Factory)))
            first = (await host.GetRequiredService<ISteamLifecycleClient>().GetAsync("42"))
                .Single(result => result.Source == "steam-reviews");
        using var connection = db.Factory.Open();
        var persisted = connection.QuerySingle<string>("""
            SELECT payload_json FROM metadata_cache WHERE provider='steam-lifecycle-v1' AND provider_id='reviews:42';
            """);
        AssertMinimal(persisted);
        Assert.Equal(first.RawJson, persisted);
        clock.Advance(TimeSpan.FromHours(1));
        var offline = new Handler { Offline = true };
        var reopened = new SqliteConnectionFactory(db.DatabasePath, pooling: false);
        using var restarted = Host(offline, clock, new SqliteStoreMetadataCache(reopened));
        var warm = (await restarted.GetRequiredService<ISteamLifecycleClient>().GetAsync("42"))
            .Single(result => result.Source == "steam-reviews");
        Assert.Equal(first, warm);
        Assert.Empty(offline.Requests);
    }

    [Theory]
    [InlineData(1, -1, true)]
    [InlineData(100, -1, false)]
    [InlineData(100, -31, true)]
    public async Task Upgrade_sanitizes_all_legacy_rows_preserving_offline_fresh_evidence(
        int count, int days, bool complete)
    {
        using var db = new TempDatabase();
        var clock = new StoreTestClock(new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero));
        var at = clock.Now.AddHours(-1).UtcDateTime;
        var cache = new SqliteStoreMetadataCache(db.Factory);
        var raw = ReviewBody(clock.Now.AddDays(days).ToUnixTimeSeconds(), count);
        await cache.SetAsync(SteamLifecycleClient.CacheProvider, "reviews:42", raw, at);
        await cache.SetAsync(SteamLifecycleClient.CacheProvider, "reviews:unvisited", raw, at.AddDays(-10));
        await cache.SetAsync("unrelated", "reviews:42", raw, at);
        using var connection = db.Factory.Open();
        connection.Execute(UpgradeScript());
        var upgraded = (await cache.GetAsync(SteamLifecycleClient.CacheProvider, "reviews:42"))!.Value;
        Assert.Equal(at, upgraded.FetchedAt);
        AssertMinimal(upgraded.PayloadJson!);
        using (var projection = JsonDocument.Parse(upgraded.PayloadJson!))
            Assert.Equal(complete, projection.RootElement.GetProperty("complete").GetBoolean());
        // No requested-key dependency: old rows are cleaned even if never read again.
        var unvisited = (await cache.GetAsync(SteamLifecycleClient.CacheProvider, "reviews:unvisited"));
        if (days == -31)
        {
            Assert.NotNull(unvisited);
            AssertMinimal(unvisited.Value.PayloadJson!);
            Assert.Equal(at.AddDays(-10), unvisited.Value.FetchedAt);
        }
        else Assert.Null(unvisited); // Its dates were in the future relative to its observation time.
        Assert.Equal(raw, (await cache.GetAsync("unrelated", "reviews:42"))!.Value.PayloadJson);

        var handler = new Handler { Offline = true };
        using var host = Host(handler, clock, cache);
        var review = (await host.GetRequiredService<ISteamLifecycleClient>().GetAsync("42"))
            .Single(result => result.Source == "steam-reviews");
        Assert.Equal(at, review.ObservedAt);
        Assert.Equal(complete ? days < -30 ? 0 : count : (int?)null, review.Signals.RecentReviewCount);
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.Contains("appreviews"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"success\":1,\"reviews\":[{}]}")]
    [InlineData("{\"success\":1,\"reviews\":[\"invalid\"]}")]
    [InlineData("{\"success\":1,\"reviews\":[{\"timestamp_created\":-1}]}")]
    public async Task Upgrade_discards_malformed_raw_reviews(string raw)
    {
        using var db = new TempDatabase();
        var cache = new SqliteStoreMetadataCache(db.Factory);
        await cache.SetAsync(SteamLifecycleClient.CacheProvider, "reviews:42", raw, DateTime.UtcNow);
        using var connection = db.Factory.Open();
        connection.Execute(UpgradeScript());
        Assert.Null(await cache.GetAsync(SteamLifecycleClient.CacheProvider, "reviews:42"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":99,\"success\":1,\"timestamp_created\":[]}")]
    [InlineData("{\"success\":1,\"reviews\":[{}]}")]
    public async Task Invalid_or_future_cached_payload_refetches_without_persisting_raw_review_content(string cached)
    {
        using var db = new TempDatabase();
        var clock = new StoreTestClock(new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero));
        var cache = new SqliteStoreMetadataCache(db.Factory);
        await cache.SetAsync(SteamLifecycleClient.CacheProvider, "reviews:42", cached, clock.Now.UtcDateTime);
        var handler = new Handler { Reviews = ReviewBody(clock.Now.AddDays(-1).ToUnixTimeSeconds(), 1) };
        using var host = Host(handler, clock, cache);
        var review = (await host.GetRequiredService<ISteamLifecycleClient>().GetAsync("42"))
            .Single(result => result.Source == "steam-reviews");
        Assert.Equal(1, review.Signals.RecentReviewCount);
        Assert.Single(handler.Requests, uri => uri.AbsolutePath.Contains("appreviews"));
        AssertMinimal((await cache.GetAsync(SteamLifecycleClient.CacheProvider, "reviews:42"))!.Value.PayloadJson!);
    }

    private static string ReviewBody(long timestamp, int count) => JsonSerializer.Serialize(new
    {
        success = 1,
        reviews = Enumerable.Range(0, count).Select(_ => new
        {
            timestamp_created = timestamp,
            review = "Unrelated review prose",
            author = new { steamid = "76561198000000001", personaname = "A fixture profile" },
        }),
    });

    private static void AssertMinimal(string payload)
    {
        Assert.DoesNotContain("Unrelated", payload);
        Assert.DoesNotContain("76561198000000001", payload);
        Assert.DoesNotContain("personaname", payload);
        using var json = JsonDocument.Parse(payload);
        Assert.Equal(2, json.RootElement.GetProperty("version").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("reviews", out _));
        Assert.Equal(10, json.RootElement.EnumerateObject().Count());
    }

    private static string UpgradeScript()
    {
        using var stream = typeof(DatabaseInitializer).Assembly.GetManifestResourceStream(
            "Winnow.Data.Migrations.0037_minimal_lifecycle_review_cache.sql")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
