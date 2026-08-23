using System.Globalization;
using System.Net;
using Hoard.Enrich.Steam;
using Hoard.Enrich.Steam.Http;
using Hoard.Enrich.Steam.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hoard.Tests.SteamStore;

/// <summary>
/// Behaviour of the keyless store client: batching, caching, and the soft-fail
/// rule that no failure mode of an undocumented endpoint may reach a caller.
/// </summary>
public class SteamStoreClientTests
{
    /// <summary>The library size §4.4 and the IGDB client both size their batching against.</summary>
    private const int LibrarySize = 616;

    // ── Batching ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Six_hundred_and_sixteen_appids_cost_seven_requests()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.DefaultResponder());
        var appIds = Library(LibrarySize);

        var items = await host.Client.GetItemsAsync(appIds);

        // 616 / 100 = 7 requests (six full batches and a remainder of 16) —
        // not 616. This is the whole reason GetItems is worth using.
        Assert.Equal(7, host.Handler.Requests.Count);
        Assert.Equal(7, host.Handler.CountFor(SteamStoreTestHost.GetItems));
        Assert.Equal(LibrarySize, items.Count);

        var perRequest = host.Handler.Requests.Select(r => r.RequestedAppIds.Count).ToArray();
        Assert.Equal([100, 100, 100, 100, 100, 100, 16], perRequest);

        // Every appid asked for exactly once, across all batches.
        var requested = host.Handler.Requests.SelectMany(r => r.RequestedAppIds).ToArray();
        Assert.Equal(LibrarySize, requested.Length);
        Assert.Equal(appIds.Order(StringComparer.Ordinal), requested.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Batch_size_is_configurable_and_bounded_below()
    {
        using var host = new SteamStoreTestHost(
            SteamStoreTestHost.DefaultResponder(), options => options.BatchSize = 0);

        await host.Client.GetItemsAsync(Library(3));

        // A nonsense batch size degrades to one-per-request rather than dividing by zero.
        Assert.Equal(3, host.Handler.Requests.Count);
    }

    [Fact]
    public async Task Duplicates_and_non_numeric_appids_never_reach_the_wire()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.DefaultResponder());

        var items = await host.Client.GetItemsAsync(
            ["440", "440", " 440 ", "", "  ", "not-an-appid", "-7", "0", "570"]);

        var requested = Assert.Single(host.Handler.Requests).RequestedAppIds;
        Assert.Equal(["440", "570"], requested);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task An_empty_request_makes_no_request_at_all()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.DefaultResponder());

        Assert.Empty(await host.Client.GetItemsAsync([]));
        Assert.Empty(await host.Client.GetItemsAsync(["", "nope"]));
        Assert.Empty(host.Handler.Requests);
    }

    // ── Caching ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_warm_library_costs_no_requests()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.DefaultResponder());
        var appIds = Library(LibrarySize);

        var cold = await host.Client.GetItemsAsync(appIds);
        Assert.Equal(7, host.Handler.Requests.Count);

        var warm = await host.Client.GetItemsAsync(appIds);

        Assert.Equal(7, host.Handler.Requests.Count);
        Assert.Equal(cold.Count, warm.Count);
        Assert.Equal(
            cold[appIds[0]].Tags.Select(t => (t.TagId, t.Rank)),
            warm[appIds[0]].Tags.Select(t => (t.TagId, t.Rank)));
    }

    [Fact]
    public async Task The_cache_holds_the_response_body_verbatim()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.CapturedResponder());

        await host.Client.GetItemsAsync([StoreFixtures.EldenRingAppId]);

        var entry = await host.Cache.GetAsync(
            SteamStoreClient.CacheProvider, SteamStoreClient.AppCacheKey(StoreFixtures.EldenRingAppId));

        Assert.NotNull(entry);
        var payload = entry!.Value.PayloadJson;
        Assert.NotNull(payload);

        // Tags are cached but not exposed, and neither is anything else in the
        // payload. Storing it verbatim is what makes that decision reversible
        // without a refetch.
        Assert.Contains("\"weight\":1077", payload, StringComparison.Ordinal);
        Assert.Contains("short_description", payload, StringComparison.Ordinal);
        Assert.Contains("steam_release_date", payload, StringComparison.Ordinal);
        Assert.Contains("steam_deck_compat_category", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expired_entry_is_refetched()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.DefaultResponder());

        await host.Client.GetItemsAsync(["440"]);
        host.Clock.Advance(TimeSpan.FromDays(6));
        await host.Client.GetItemsAsync(["440"]);
        Assert.Single(host.Handler.Requests);

        host.Clock.Advance(TimeSpan.FromDays(2));
        await host.Client.GetItemsAsync(["440"]);
        Assert.Equal(2, host.Handler.Requests.Count);
    }

    [Fact]
    public async Task A_per_call_ttl_overrides_the_configured_one()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.DefaultResponder());

        await host.Client.GetItemsAsync(["440"]);
        host.Clock.Advance(TimeSpan.FromMinutes(5));

        await host.Client.GetItemsAsync(["440"], cacheTtl: TimeSpan.FromMinutes(1));

        Assert.Equal(2, host.Handler.Requests.Count);
    }

    /// <summary>
    /// A store item Steam cannot serve — <c>success: 15</c> — is a real answer,
    /// so it is cached as a miss exactly the way IGDB caches an unmatched appid.
    /// The distinction that matters is the one below: a *failed request* is not.
    /// </summary>
    [Fact]
    public async Task A_non_store_app_is_cached_as_a_miss_and_not_asked_about_again()
    {
        var nonStore = new HashSet<string> { StoreFixtures.NonStoreAppId };
        using var host = new SteamStoreTestHost(SteamStoreTestHost.DefaultResponder(nonStore));

        var first = await host.Client.GetItemsAsync([StoreFixtures.NonStoreAppId, "440"]);
        Assert.DoesNotContain(StoreFixtures.NonStoreAppId, first.Keys);
        Assert.Contains("440", first.Keys);

        var entry = await host.Cache.GetAsync(
            SteamStoreClient.CacheProvider, SteamStoreClient.AppCacheKey(StoreFixtures.NonStoreAppId));
        Assert.NotNull(entry);
        Assert.Null(entry!.Value.PayloadJson);

        await host.Client.GetItemsAsync([StoreFixtures.NonStoreAppId]);
        Assert.Single(host.Handler.Requests);
    }

    /// <summary>
    /// A SHORT response is a shape anomaly, not a batch of misses.
    ///
    /// <para>docs/spikes/steam-store-tags.md:57-60 verified that appids with no
    /// store page come back INSIDE the array, so "the store has nothing for this
    /// appid" always arrives as a present-but-unprojectable item — the case the
    /// test above covers. An appid simply absent from a 200 is the endpoint
    /// behaving differently from the way it was verified to behave, and reading
    /// it as a miss means a 200 carrying 1 of 100 requested items caches the
    /// other 99 as "Steam has never heard of this game" for the full 7-day TTL.
    /// The items that DID arrive are still used and still cached — they are real
    /// data — but nothing negative is written for the ones that did not, so the
    /// next pass asks again.</para>
    /// </summary>
    [Fact]
    public async Task A_short_response_is_a_shape_anomaly_not_a_batch_of_misses()
    {
        using var host = new SteamStoreTestHost((request, _) =>
        {
            // Answer only the first appid of whatever was asked for.
            var trimmed = request.RequestedAppIds.Take(1).ToArray();
            return FakeStoreHandler.Json(
                HttpStatusCode.OK,
                StoreFixtures.Envelope(new
                {
                    store_items = trimmed.Select(id => new
                    {
                        id = long.Parse(id, CultureInfo.InvariantCulture),
                        appid = long.Parse(id, CultureInfo.InvariantCulture),
                        success = 1,
                        visible = true,
                        name = StoreFixtures.ExpectedName(id),
                    }),
                }));
        });

        var items = await host.Client.GetItemsAsync(["440", "570", "730"]);

        // What arrived is used and kept.
        Assert.Equal(["440"], items.Keys);
        Assert.Empty(items["440"].Tags);
        Assert.NotNull(await host.Cache.GetAsync(
            SteamStoreClient.CacheProvider, SteamStoreClient.AppCacheKey("440")));

        // What did not arrive is not written at all — not as a hit, and above
        // all not as a miss.
        foreach (var unanswered in (string[])["570", "730"])
        {
            Assert.Null(await host.Cache.GetAsync(
                SteamStoreClient.CacheProvider, SteamStoreClient.AppCacheKey(unanswered)));
        }

        // So the next pass re-asks for them rather than serving a cached
        // nothing for a week.
        await host.Client.GetItemsAsync(["570", "730"]);
        Assert.Equal(2, host.Handler.Requests.Count);
        Assert.Equal(["570", "730"], host.Handler.Requests[1].RequestedAppIds);
    }

    /// <summary>
    /// The degenerate short response — a 200 with no items at all — is the same
    /// rule, and must not turn a whole batch into cached misses either.
    /// </summary>
    [Fact]
    public async Task An_empty_response_caches_nothing()
    {
        using var host = new SteamStoreTestHost((_, _) => FakeStoreHandler.Json(
            HttpStatusCode.OK, StoreFixtures.Envelope(new { store_items = Array.Empty<object>() })));

        Assert.Empty(await host.Client.GetItemsAsync(["440", "570"]));

        Assert.Null(await host.Cache.GetAsync(
            SteamStoreClient.CacheProvider, SteamStoreClient.AppCacheKey("440")));
        Assert.Null(await host.Cache.GetAsync(
            SteamStoreClient.CacheProvider, SteamStoreClient.AppCacheKey("570")));
    }

    // ── Soft failure ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task A_non_200_degrades_to_empty_and_caches_no_miss(HttpStatusCode status)
    {
        using var host = new SteamStoreTestHost((_, _) => FakeStoreHandler.Json(status, "{}"));

        // No throw: an endpoint that closes tomorrow must not break the app.
        var items = await host.Client.GetItemsAsync(["440", "570"]);

        Assert.Empty(items);

        // Critically, nothing cached. A 403 the day Valve starts requiring a key
        // must not be recorded as "these games do not exist" for a week.
        foreach (var appId in new[] { "440", "570" })
        {
            Assert.Null(await host.Cache.GetAsync(
                SteamStoreClient.CacheProvider, SteamStoreClient.AppCacheKey(appId)));
        }
    }

    [Fact]
    public async Task A_failed_batch_is_retried_on_the_next_pass()
    {
        var fail = true;
        using var host = new SteamStoreTestHost((request, _) => fail
            ? FakeStoreHandler.Json(HttpStatusCode.Forbidden, "{}")
            : FakeStoreHandler.Json(HttpStatusCode.OK, StoreFixtures.GetItemsFor(request)));

        Assert.Empty(await host.Client.GetItemsAsync(["440"]));

        fail = false;
        var items = await host.Client.GetItemsAsync(["440"]);

        Assert.Equal(StoreFixtures.ExpectedName("440"), items["440"].Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"response\":")]
    [InlineData("[]")]
    [InlineData("{\"response\":{\"store_items\":\"nope\"}}")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    public async Task Malformed_bodies_degrade_to_empty(string body)
    {
        using var host = new SteamStoreTestHost((_, _) => FakeStoreHandler.Json(HttpStatusCode.OK, body));

        Assert.Empty(await host.Client.GetItemsAsync(["440"]));
        Assert.Empty((await host.Client.GetTagListAsync()).Names);
        Assert.Null(await host.Cache.GetAsync(
            SteamStoreClient.CacheProvider, SteamStoreClient.AppCacheKey("440")));
    }

    [Fact]
    public async Task A_dead_network_degrades_to_empty()
    {
        using var host = new SteamStoreTestHost(
            (_, _) => throw new HttpRequestException("no such host"),
            options => options.MaxRetryAttempts = 1);

        Assert.Empty(await host.Client.GetItemsAsync(["440"]));
        Assert.Empty((await host.Client.GetTagListAsync()).Names);
    }

    /// <summary>
    /// The one exception to soft-fail: the caller asking to stop is not an
    /// enrichment failure, and swallowing it into an empty result would make a
    /// cancelled backfill look like a completed one.
    /// </summary>
    [Fact]
    public async Task Caller_cancellation_still_propagates()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.DefaultResponder());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => host.Client.GetItemsAsync(["440"], ct: cts.Token));
    }

    // ── Rate limiting and retry ──────────────────────────────────────────────

    [Fact]
    public async Task A_429_is_retried_rather_than_surfaced()
    {
        using var host = new SteamStoreTestHost((request, prior) => prior == 0
            ? FakeStoreHandler.TooManyRequests(TimeSpan.FromSeconds(1))
            : FakeStoreHandler.Json(HttpStatusCode.OK, StoreFixtures.GetItemsFor(request)));

        var items = await host.Client.GetItemsAsync(["440"]);

        Assert.Equal(StoreFixtures.ExpectedName("440"), items["440"].Name);
        Assert.Equal(2, host.Handler.Requests.Count);
    }

    /// <summary>
    /// <c>Retry-After</c> is honoured but capped: §4.2 reports Steam sending
    /// 60–120 s, and a mistaken or hostile header must not be able to park a
    /// background job for an hour.
    /// </summary>
    [Fact]
    public async Task An_absurd_retry_after_is_capped()
    {
        using var host = new SteamStoreTestHost(
            (request, prior) => prior == 0
                ? FakeStoreHandler.TooManyRequests(TimeSpan.FromHours(1))
                : FakeStoreHandler.Json(HttpStatusCode.OK, StoreFixtures.GetItemsFor(request)),
            options => options.MaxRetryDelay = TimeSpan.FromMilliseconds(50));

        var started = DateTime.UtcNow;
        var items = await host.Client.GetItemsAsync(["440"]);

        Assert.Equal(StoreFixtures.ExpectedName("440"), items["440"].Name);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(30), "the one-hour Retry-After was not capped");
    }

    [Fact]
    public async Task Exhausted_retries_still_degrade_rather_than_throw()
    {
        using var host = new SteamStoreTestHost(
            (_, _) => FakeStoreHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"),
            options => options.MaxRetryAttempts = 2);

        Assert.Empty(await host.Client.GetItemsAsync(["440"]));

        // The original attempt plus two retries; then it gives up quietly.
        Assert.Equal(3, host.Handler.Requests.Count);
    }

    [Fact]
    public void The_rate_limiter_is_shared_across_the_whole_module()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.DefaultResponder());

        // A per-client limiter would multiply the ceiling by the number of clients.
        Assert.Same(host.Resolve<SteamStoreRateLimiter>(), host.Resolve<SteamStoreRateLimiter>());
    }

    [Fact]
    public async Task The_configured_rate_is_actually_enforced()
    {
        using var host = new SteamStoreTestHost(
            SteamStoreTestHost.DefaultResponder(), options => options.RequestsPerSecond = 2);
        var limiter = host.Resolve<SteamStoreRateLimiter>();

        Assert.Equal(2, limiter.AvailablePermits);
        await host.Client.GetItemsAsync(["440"]);
        Assert.Equal(1, limiter.AvailablePermits);
    }

    // ── Tag ranks ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tag_rank_follows_the_order_steam_returned()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.DefaultResponder());

        var tags = (await host.Client.GetItemsAsync(["440"]))["440"].Tags;

        Assert.Equal(20, tags.Count);
        Assert.Equal(StoreFixtures.ExpectedTagIds("440"), tags.Select(t => t.TagId));
        Assert.Equal(Enumerable.Range(1, 20), tags.Select(t => t.Rank));
    }

    /// <summary>
    /// Rank is derived from weight, not from array position, so an unsorted
    /// response cannot silently invert "top tag".
    /// </summary>
    [Fact]
    public async Task Rank_is_recomputed_when_weights_arrive_out_of_order()
    {
        using var host = new SteamStoreTestHost((_, _) => FakeStoreHandler.Json(
            HttpStatusCode.OK,
            StoreFixtures.Envelope(new
            {
                store_items = new[]
                {
                    new
                    {
                        id = 440,
                        appid = 440,
                        success = 1,
                        visible = true,
                        name = "Team Fortress 2",
                        tags = new[]
                        {
                            new { tagid = 11L, weight = 100 },
                            new { tagid = 22L, weight = 900 },
                            new { tagid = 33L, weight = 500 },
                        },
                    },
                },
            })));

        var tags = (await host.Client.GetItemsAsync(["440"]))["440"].Tags;

        Assert.Equal([22L, 33L, 11L], tags.Select(t => t.TagId));
        Assert.Equal([1, 2, 3], tags.Select(t => t.Rank));
    }

    [Fact]
    public async Task Ties_keep_the_order_steam_sent_them_in()
    {
        using var host = new SteamStoreTestHost((_, _) => FakeStoreHandler.Json(
            HttpStatusCode.OK,
            StoreFixtures.Envelope(new
            {
                store_items = new[]
                {
                    new
                    {
                        id = 440,
                        appid = 440,
                        success = 1,
                        visible = true,
                        name = "Team Fortress 2",
                        tags = new[]
                        {
                            new { tagid = 11L, weight = 343 },
                            new { tagid = 22L, weight = 343 },
                        },
                    },
                },
            })));

        // TF2 really does have two tags at weight 343; a tie must be stable,
        // not arbitrary, or rank would wobble between runs.
        var tags = (await host.Client.GetItemsAsync(["440"]))["440"].Tags;

        Assert.Equal([11L, 22L], tags.Select(t => t.TagId));
        Assert.Equal([1, 2], tags.Select(t => t.Rank));
    }

    [Fact]
    public async Task An_untagged_app_is_a_hit_with_no_tags()
    {
        using var host = new SteamStoreTestHost((_, _) => FakeStoreHandler.Json(
            HttpStatusCode.OK,
            StoreFixtures.Envelope(new
            {
                store_items = new[]
                {
                    new { id = 440, appid = 440, success = 1, visible = true, name = "Team Fortress 2" },
                },
            })));

        var items = await host.Client.GetItemsAsync(["440"]);

        // The name is the point; tags are a bonus that may simply be absent.
        Assert.Equal("Team Fortress 2", items["440"].Name);
        Assert.Empty(items["440"].Tags);
    }

    [Fact]
    public async Task An_item_with_no_usable_name_is_a_miss()
    {
        using var host = new SteamStoreTestHost((_, _) => FakeStoreHandler.Json(
            HttpStatusCode.OK,
            StoreFixtures.Envelope(new
            {
                store_items = new[]
                {
                    new { id = 440, appid = 440, success = 1, visible = true, name = "   " },
                },
            })));

        // A blank name would overwrite "App 440" with something worse.
        Assert.Empty(await host.Client.GetItemsAsync(["440"]));
    }

    // ── Tag vocabulary ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_vocabulary_is_fetched_once_and_then_served_from_cache()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.CapturedResponder());

        var first = await host.Client.GetTagListAsync();
        var second = await host.Client.GetTagListAsync();

        Assert.Single(host.Handler.Requests);
        Assert.Equal(first.VersionHash, second.VersionHash);
        Assert.Equal(first.Names.Count, second.Names.Count);

        // Long TTL: a month of calls costs one request.
        host.Clock.Advance(TimeSpan.FromDays(29));
        await host.Client.GetTagListAsync();
        Assert.Single(host.Handler.Requests);

        host.Clock.Advance(TimeSpan.FromDays(2));
        await host.Client.GetTagListAsync();
        Assert.Equal(2, host.Handler.Requests.Count);
    }

    [Fact]
    public async Task The_vocabulary_request_is_the_one_the_spike_verified()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.CapturedResponder());

        await host.Client.GetTagListAsync();

        var request = Assert.Single(host.Handler.Requests);
        Assert.EndsWith("/IStoreService/GetTagList/v1/", request.Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("""{"language":"english"}""", request.InputJson);
    }

    // ── Composition ──────────────────────────────────────────────────────────

    /// <summary>
    /// The module's whole reason for existing: it comes up and works with no
    /// credentials, no settings and no user action.
    /// </summary>
    [Fact]
    public async Task The_module_works_with_nothing_configured()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IStoreMetadataCache>(new InMemoryStoreMetadataCache());
        services.AddSteamStoreEnrichment();
        services.AddHttpClient<ISteamStoreClient, SteamStoreClient>()
            .ConfigurePrimaryHttpMessageHandler(
                () => new FakeStoreHandler(SteamStoreTestHost.DefaultResponder()));

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ISteamStoreClient>();

        Assert.Equal(StoreFixtures.ExpectedName("440"), (await client.GetItemsAsync(["440"]))["440"].Name);
    }

    [Fact]
    public void Registrations_defer_to_anything_already_in_the_container()
    {
        var cache = new InMemoryStoreMetadataCache();
        var options = new SteamStoreOptions { BatchSize = 7 };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IStoreMetadataCache>(cache);
        services.AddSingleton(options);

        services.AddSteamStoreEnrichment(o => o.BatchSize = 999);

        using var provider = services.BuildServiceProvider();
        Assert.Same(cache, provider.GetRequiredService<IStoreMetadataCache>());
        Assert.Same(options, provider.GetRequiredService<SteamStoreOptions>());
        Assert.Equal(7, provider.GetRequiredService<SteamStoreOptions>().BatchSize);
    }

    private static string[] Library(int count)
        => Enumerable.Range(0, count)
            .Select(i => (1000 + i).ToString(CultureInfo.InvariantCulture))
            .ToArray();
}
