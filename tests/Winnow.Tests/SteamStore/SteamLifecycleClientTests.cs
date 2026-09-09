using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri);
            var news = uri.AbsolutePath.Contains("GetNewsForApp");
            var body = news ? News : uri.AbsolutePath.Contains("GetNumberOfCurrentPlayers") ? Players :
                uri.AbsolutePath.Contains("appreviews") ? Reviews : """{"response":{"store_items":[{"appid":42,"id":42,"success":15,"visible":false,"name":""}]}}""";
            return Task.FromResult(new HttpResponseMessage(news ? NewsStatus : HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static ServiceProvider Host(Handler handler, StoreTestClock clock)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IStoreMetadataCache, InMemoryStoreMetadataCache>();
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
}
