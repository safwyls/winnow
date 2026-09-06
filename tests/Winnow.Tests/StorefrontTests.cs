using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.Stores;
using Xunit;

namespace Winnow.Tests;

public sealed class StorefrontTests
{
    private const string Epic = """{"fn":"fortnite","min":"hades","bad":"../../login","wrong":12}""";
    private const string Gog = """{"id":1207658871,"links":{"product_card":"https://www.gog.com/game/panzer_general_2"},"changelog":"<h4>Internal Update</h4><ul><li>Cloud Saves support</li></ul><script>bad()</script>"}""";

    [Fact]
    public async Task Namespace_lookup_fills_a_bulk_map_miss_without_guessing_from_the_title()
    {
        const string ns = "bec822fb982843c3be794d440728336b";
        using var db = new TempDatabase();
        var cache = new StorefrontCache(db.Factory);
        var payload = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "storefront", "epic-namespace-moonlighter.json"));
        using var handler = new FixtureHandler(n => Response(n == 1 ? Epic : payload));
        using var http = new HttpClient(handler);
        var client = new StorefrontClient(http, cache, TimeProvider.System);
        await client.RefreshEpicNamespacesAsync(["fn", ns, ns, "not/a/namespace", "unsafe\""]);
        Assert.Equal(2, handler.Urls.Count);
        Assert.Contains("catalogNs(namespace:\"" + ns + "\")", Uri.UnescapeDataString(handler.Urls[1]));
        Assert.DoesNotContain("Moonlighter", handler.Urls[1], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://store.epicgames.com/p/moonlighter", (await cache.ReadAllAsync())["epic:" + ns].StoreUrl);
        await client.RefreshEpicNamespacesAsync([ns]);
        Assert.Equal(2, handler.Urls.Count);
    }

    [Fact]
    public async Task Sync_uses_persisted_namespaces_for_bulk_map_misses()
    {
        const string ns = "bec822fb982843c3be794d440728336b";
        using var db = new TempDatabase();
        using (var lease = db.Factory.Lease())
            await lease.Connection.ExecuteAsync("""
                INSERT INTO works(id,name) VALUES(1,'A user-renamed title');
                INSERT INTO releases(id,work_id,name) VALUES(1,1,'A user-renamed title');
                INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'epic','catalog-id');
                INSERT INTO ownerships(id,release_id,store) VALUES(1,1,'epic');
                """);
        await new SqliteEpicLaunchKeyStore(db.Factory).SaveAsync([new("catalog-id", ns, "Eagle")]);
        var cache = new StorefrontCache(db.Factory);
        var payload = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "storefront", "epic-namespace-moonlighter.json"));
        using var handler = new FixtureHandler(n => Response(n == 1 ? Epic : payload));
        using var http = new HttpClient(handler);
        await new StorefrontSyncService(db.Factory, new(http, cache, TimeProvider.System), NullLogger<StorefrontSyncService>.Instance,
            new SqliteEpicLaunchKeys(db.Factory)).SyncAsync();
        Assert.Equal(2, handler.Urls.Count);
        Assert.Equal("https://store.epicgames.com/p/moonlighter", (await cache.ReadAllAsync())["epic:" + ns].StoreUrl);
    }

    [Theory]
    [InlineData("""{"data":{"Catalog":{"catalogNs":null}}}""")]
    [InlineData("""{"data":{"Catalog":{"catalogNs":{"mappings":null}}}}""")]
    [InlineData("""{"data":{"Catalog":{"catalogNs":{"mappings":[]}}}}""")]
    public async Task Namespace_lookup_caches_confirmed_absence(string payload)
    {
        using var db = new TempDatabase();
        var cache = new StorefrontCache(db.Factory);
        using var handler = new FixtureHandler(n => Response(n == 1 ? Epic : payload));
        using var http = new HttpClient(handler);
        var client = new StorefrontClient(http, cache, TimeProvider.System);
        await client.RefreshEpicNamespacesAsync(["missing"]);
        await client.RefreshEpicNamespacesAsync(["missing"]);
        Assert.Equal(2, handler.Urls.Count);
        Assert.False((await cache.ReadAllAsync()).ContainsKey("epic:missing"));
    }

    [Fact]
    public async Task Graphql_errors_do_not_replace_a_stale_mapping()
    {
        using var db = new TempDatabase();
        var cache = new StorefrontCache(db.Factory);
        var payload = """{"data":{"Catalog":{"catalogNs":{"mappings":[{"pageSlug":"example","pageType":"productHome"}]}}}}""";
        await cache.SaveAsync("epic", Epic, DateTime.UtcNow);
        await cache.SaveAsync("epic-namespace:example", payload, DateTime.UtcNow.AddDays(-2));
        using var handler = new FixtureHandler(_ => Response("""{"errors":[{"message":"temporarily unavailable"}],"data":{"Catalog":{"catalogNs":null}}}"""));
        using var http = new HttpClient(handler);
        await new StorefrontClient(http, cache, TimeProvider.System).RefreshEpicNamespacesAsync(["example"]);
        Assert.Equal("https://store.epicgames.com/p/example", (await cache.ReadAllAsync())["epic:example"].StoreUrl);
    }

    [Theory]
    [InlineData("offer", "dlc", "productHome", "base-game", "https://store.epicgames.com/p/base-game")]
    [InlineData("productHome", "one", "productHome", "two", null)]
    [InlineData("productHome", "../bad", "offer", "dlc", null)]
    public void Namespace_mapping_requires_one_valid_product_home(string type1, string slug1, string type2, string slug2, string? expected)
    {
        var payload = JsonSerializer.Serialize(new { data = new { Catalog = new { catalogNs = new { mappings = new[] {
            new { pageType = type1, pageSlug = slug1 }, new { pageType = type2, pageSlug = slug2 } } } } } });
        Assert.True(StorefrontClient.TryParseEpicNamespace(payload, out var url));
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"data\":{\"Catalog\":{\"catalogNs\":{\"mappings\":7}}}}")]
    public void Malformed_namespace_envelopes_are_rejected(string payload)
        => Assert.False(StorefrontClient.TryParseEpicNamespace(payload, out _));

    [Fact]
    public async Task Sync_uses_owned_store_ids_and_warms_the_read_only_projection()
    {
        using var db = new TempDatabase();
        using (var lease = db.Factory.Lease())
            await lease.Connection.ExecuteAsync("""
                INSERT INTO works(id,name) VALUES(1,'Owned GOG'),(2,'Owned Epic'),(3,'Unowned GOG');
                INSERT INTO releases(id,work_id,name) VALUES(1,1,'Owned GOG'),(2,2,'Owned Epic'),(3,3,'Unowned GOG');
                INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'gog','1207658871'),(2,'epic','catalog-id'),(3,'gog','99');
                INSERT INTO ownerships(id,release_id,store) VALUES(1,1,'gog'),(2,2,'epic');
                """);
        var cache = new StorefrontCache(db.Factory);
        using var handler = new FixtureHandler(n => Response(n == 1 ? Epic : Gog));
        using var http = new HttpClient(handler);
        await new SqliteEpicLaunchKeyStore(db.Factory).SaveAsync([new("catalog-id", "fn", "Fortnite")]);
        var sync = new StorefrontSyncService(db.Factory, new(http, cache, TimeProvider.System), NullLogger<StorefrontSyncService>.Instance,
            new SqliteEpicLaunchKeys(db.Factory));
        await sync.SyncAsync();
        Assert.Equal(2, handler.Urls.Count);
        var library = new LibraryViewModel(new LibraryQueryRepository(db.Factory),
            new OwnershipRepository(db.Factory), new ReleaseRepository(db.Factory), new WorkRepository(db.Factory),
            new UpdateEventRepository(db.Factory), epicLaunchKeys: new SqliteEpicLaunchKeys(db.Factory), storefrontCache: cache);
        await library.LoadCommand.ExecuteAsync(null);
        Assert.Equal("https://store.epicgames.com/p/fortnite", library.VisibleTiles.Single(t => t.Store == "epic").PlayableEntry.Storefront?.StoreUrl);
        Assert.Equal("https://www.gog.com/game/panzer_general_2", library.VisibleTiles.Single(t => t.Store == "gog").PlayableEntry.Storefront?.StoreUrl);
        Assert.Equal(2, handler.Urls.Count);
        Assert.DoesNotContain(handler.Urls, uri => uri.Contains("/99?", StringComparison.Ordinal));
        var stored = await cache.ReadAllAsync();
        Assert.NotNull(stored["gog:1207658871"].PatchNotes);
        Assert.Equal("https://store.epicgames.com/p/fortnite", stored["epic:fn"].StoreUrl);
        await sync.SyncAsync();
        Assert.Equal(2, handler.Urls.Count);
    }

    [Fact]
    public async Task Epic_fetch_is_shared_cached_and_projects_only_valid_slugs()
    {
        using var db = new TempDatabase();
        var cache = new StorefrontCache(db.Factory);
        using var handler = new FixtureHandler(_ => Response(Epic));
        using var http = new HttpClient(handler);
        var client = new StorefrontClient(http, cache, TimeProvider.System);
        await client.RefreshEpicAsync();
        await client.RefreshEpicAsync();
        Assert.Single(handler.Urls);
        Assert.Equal(StorefrontClient.EpicMappingUrl, handler.Urls[0]);
        var stored = await cache.ReadAllAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal("https://store.epicgames.com/p/hades", stored["epic:min"].StoreUrl);
        Assert.False(stored.ContainsKey("epic:bad"));
    }

    [Fact]
    public async Task Gog_fetch_supplies_store_page_and_safe_readable_notes()
    {
        using var db = new TempDatabase();
        var cache = new StorefrontCache(db.Factory);
        using var handler = new FixtureHandler(_ => Response(Gog));
        using var http = new HttpClient(handler);
        await new StorefrontClient(http, cache, TimeProvider.System).RefreshGogAsync("1207658871");
        Assert.Equal("https://api.gog.com/products/1207658871?expand=changelog", Assert.Single(handler.Urls));
        var details = (await cache.ReadAllAsync())["gog:1207658871"];
        Assert.Equal("https://www.gog.com/game/panzer_general_2", details.StoreUrl);
        Assert.Contains("Cloud Saves support", details.PatchNotes);
        Assert.DoesNotContain("bad()", details.PatchNotes);
        Assert.DoesNotContain("<", details.PatchNotes);
        Assert.Contains("\n", details.PatchNotes);
    }

    [Theory]
    [InlineData("bad id")]
    [InlineData("1?other=2")]
    [InlineData("")]
    public async Task Invalid_product_ids_make_no_request(string id)
    {
        using var db = new TempDatabase();
        using var handler = new FixtureHandler(_ => Response(Gog));
        using var http = new HttpClient(handler);
        await new StorefrontClient(http, new(db.Factory), TimeProvider.System).RefreshGogAsync(id);
        Assert.Empty(handler.Urls);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("not json")]
    public async Task Malformed_responses_preserve_stale_cache(string payload)
    {
        using var db = new TempDatabase();
        var cache = new StorefrontCache(db.Factory);
        await cache.SaveAsync("epic", Epic, DateTime.UtcNow.AddDays(-2));
        using var handler = new FixtureHandler(_ => Response(payload));
        using var http = new HttpClient(handler);
        await new StorefrontClient(http, cache, TimeProvider.System).RefreshEpicAsync();
        Assert.Equal(Epic, (await cache.GetAsync("epic"))!.Payload);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Definitive_missing_response_is_negatively_cached(HttpStatusCode status)
    {
        using var db = new TempDatabase();
        var cache = new StorefrontCache(db.Factory);
        using var handler = new FixtureHandler(_ => new(status));
        using var http = new HttpClient(handler);
        var client = new StorefrontClient(http, cache, TimeProvider.System);
        await client.RefreshEpicAsync();
        await client.RefreshEpicAsync();
        Assert.Single(handler.Urls);
        Assert.Empty(await cache.ReadAllAsync());
    }

    [Fact]
    public async Task Network_failure_keeps_stale_response()
    {
        using var db = new TempDatabase();
        var cache = new StorefrontCache(db.Factory);
        await cache.SaveAsync("epic", Epic, DateTime.UtcNow.AddDays(-2));
        using var handler = new FixtureHandler(_ => throw new HttpRequestException());
        using var http = new HttpClient(handler);
        await new StorefrontClient(http, cache, TimeProvider.System).RefreshEpicAsync();
        Assert.Equal(2, (await cache.ReadAllAsync()).Count);
    }

    [Fact]
    public async Task Forbidden_preserves_prior_data_and_suppresses_repeated_requests()
    {
        using var db = new TempDatabase();
        var cache = new StorefrontCache(db.Factory);
        await cache.SaveAsync("epic", Epic, DateTime.UtcNow.AddDays(-2));
        using var handler = new FixtureHandler(_ => new(HttpStatusCode.Forbidden));
        using var http = new HttpClient(handler);
        var client = new StorefrontClient(http, cache, TimeProvider.System);
        await client.RefreshEpicAsync();
        await client.RefreshEpicAsync();
        Assert.Single(handler.Urls);
        Assert.Equal(2, (await cache.ReadAllAsync()).Count);
    }

    [Fact]
    public async Task Wrong_product_response_is_not_cached()
    {
        using var db = new TempDatabase();
        var cache = new StorefrontCache(db.Factory);
        using var handler = new FixtureHandler(_ => Response(Gog));
        using var http = new HttpClient(handler);
        await new StorefrontClient(http, cache, TimeProvider.System).RefreshGogAsync("99");
        Assert.Null(await cache.GetAsync("gog:99"));
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        using var db = new TempDatabase();
        using var cts = new CancellationTokenSource();
        using var handler = new FixtureHandler(_ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });
        using var http = new HttpClient(handler);
        var client = new StorefrontClient(http, new(db.Factory), TimeProvider.System);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RefreshEpicAsync(cts.Token));
    }

    [Fact]
    public async Task Polly_retries_429_with_retry_after_and_rate_limits_each_attempt()
    {
        using var budget = new StorefrontBudget();
        using var fixture = new FixtureHandler(n => n == 1
            ? new(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero) } }
            : Response(Epic));
        using var handler = new StorefrontHandler(budget) { InnerHandler = fixture };
        using var http = new HttpClient(handler);
        var start = System.Diagnostics.Stopwatch.StartNew();
        using var response = await http.GetAsync(StorefrontClient.EpicMappingUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, fixture.Urls.Count);
        Assert.True(start.Elapsed >= TimeSpan.FromMilliseconds(500), "Retry must spend the shared one-per-second permit.");
    }

    [Theory]
    [InlineData("https://evil.example/game")]
    [InlineData("https://www.gog.com@evil.example/game")]
    [InlineData("javascript:bad()")]
    public void Untrusted_product_links_are_not_drawn(string url)
    {
        var details = StorefrontClient.ParseGog(JsonSerializer.Serialize(new { links = new { product_card = url } }));
        Assert.Null(details.StoreUrl);
    }

    [Fact]
    public void Details_offer_store_link_and_readable_gog_notes_and_hide_missing_epic_link()
    {
        var storefront = StorefrontClient.ParseGog(Gog);
        var entry = new TileEntry { OwnershipId = 1, ReleaseId = 1, WorkId = 1, Store = "gog", PlaytimeMinutes = 0,
            GogProductId = "1207658871", Storefront = storefront };
        var tile = new GameTileViewModel([entry], GameGrouping.Of(1, [entry], null, 0, BucketThresholds.Default), "Panzer General 2", DateTime.UtcNow);
        var details = new GameDetailsViewModel(tile, "", [], DateTime.UtcNow);
        Assert.Contains(details.Links, link => link.Label == "Store page" && link.Uri == storefront.StoreUrl);
        Assert.True(details.HasGogPatchNotes);
        Assert.Contains("Cloud Saves support", details.GogPatchNotes);
        Assert.Empty(StoreActions.LinksFor("epic", null, null));
        Assert.Single(StoreActions.LinksFor("epic", null, null, new("https://store.epicgames.com/p/hades", null)));
    }

    private static HttpResponseMessage Response(string payload) => new(HttpStatusCode.OK) { Content = new StringContent(payload) };
    private sealed class FixtureHandler(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(response(Urls.Count));
        }
    }
}
