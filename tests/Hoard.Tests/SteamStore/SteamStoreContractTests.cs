using System.Globalization;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Hoard.Tests.SteamStore;

/// <summary>
/// The early-warning system for two <b>undocumented</b> endpoints.
///
/// <para><c>IStoreBrowseService/GetItems</c> and <c>IStoreService/GetTagList</c>
/// appear in no Valve documentation and in no
/// <c>ISteamWebAPIUtil/GetSupportedAPIList</c> response — the spike checked. They
/// are publicly callable today under no stability promise, and Hoard depends on
/// their exact shape.</para>
///
/// <para>These tests pin that shape against the bytes captured live on
/// 2026-08-23 (tests/fixtures/steam-store/). They do not detect a change on
/// their own — nothing can, without calling the live API — but the moment
/// someone recaptures a fixture, every field the client relies on that Valve has
/// moved, renamed or dropped fails here <i>loudly</i>, instead of the client
/// quietly soft-failing to "no data" in production for weeks.</para>
///
/// <para>The complement is <see cref="ShapeChangeTests"/>: proof that when the
/// shape does change, the client degrades instead of throwing.</para>
/// </summary>
public class SteamStoreContractTests
{
    // ── GetItems envelope ────────────────────────────────────────────────────

    [Fact]
    public void GetItems_wraps_results_in_response_store_items()
    {
        using var document = JsonDocument.Parse(StoreFixtures.GetItemsResponse());

        Assert.True(document.RootElement.TryGetProperty("response", out var response));
        Assert.Equal(JsonValueKind.Object, response.ValueKind);
        Assert.True(response.TryGetProperty("store_items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);

        // 4 requested (1245620, 570, 440, 760), 4 returned — including the one
        // that is not a store item.
        Assert.Equal(4, items.GetArrayLength());
    }

    [Fact]
    public void GetItems_successful_item_carries_the_fields_the_client_reads()
    {
        var item = Item(StoreFixtures.EldenRingAppId);

        Assert.Equal(1, item.GetProperty("success").GetInt32());
        Assert.True(item.GetProperty("visible").GetBoolean());
        Assert.Equal("ELDEN RING", item.GetProperty("name").GetString());

        // id and appid agree for a real store item. They do not always — see below.
        Assert.Equal(1245620, item.GetProperty("id").GetInt64());
        Assert.Equal(1245620, item.GetProperty("appid").GetInt64());
    }

    /// <summary>
    /// The invariant the whole batching design rests on: <c>id</c> echoes the
    /// requested appid even when the item failed, but <c>appid</c> does not.
    ///
    /// <para>The spike recorded <c>{"success":15,"visible":false,"name":""}</c>
    /// for appid 760 and did not note that the same item carries
    /// <c>"appid": 0</c>. Correlating a batch on <c>appid</c> would therefore
    /// silently attribute every miss in a batch to app 0 — hence
    /// <c>SteamStoreJson.TryReadStoreItems</c> keys on <c>id</c>.</para>
    /// </summary>
    [Fact]
    public void GetItems_failed_item_keeps_its_id_but_zeroes_its_appid()
    {
        var item = Item(StoreFixtures.NonStoreAppId);

        Assert.Equal(760, item.GetProperty("id").GetInt64());
        Assert.Equal(0, item.GetProperty("appid").GetInt64());
        Assert.Equal(15, item.GetProperty("success").GetInt32());
        Assert.False(item.GetProperty("visible").GetBoolean());
        Assert.Equal(string.Empty, item.GetProperty("name").GetString());

        // No tags key at all — not an empty array.
        Assert.False(item.TryGetProperty("tags", out _));
        Assert.False(item.TryGetProperty("tagids", out _));
    }

    [Fact]
    public void GetItems_returns_a_batch_inside_a_single_200()
    {
        // Per-item failure is graceful: one dud does not fail its neighbours.
        var ids = Items().Select(i => i.GetProperty("id").GetInt64()).ToArray();

        Assert.Equal([1245620L, 570L, 440L, 760L], ids);
        Assert.Equal(3, Items().Count(i => i.GetProperty("success").GetInt32() == 1));
    }

    // ── GetItems tags ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(StoreFixtures.EldenRingAppId)]
    [InlineData(StoreFixtures.DotaAppId)]
    [InlineData(StoreFixtures.TeamFortressAppId)]
    public void GetItems_tags_are_objects_of_tagid_and_weight_capped_at_twenty(string appId)
    {
        var tags = Item(appId).GetProperty("tags");

        Assert.Equal(JsonValueKind.Array, tags.ValueKind);

        // Steam publishes a top-20 list; include_tag_count: 100 returns the same 20.
        Assert.Equal(20, tags.GetArrayLength());

        foreach (var tag in tags.EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Number, tag.GetProperty("tagid").ValueKind);
            Assert.Equal(JsonValueKind.Number, tag.GetProperty("weight").ValueKind);
            Assert.True(tag.GetProperty("weight").GetInt64() > 0);
        }
    }

    [Theory]
    [InlineData(StoreFixtures.EldenRingAppId)]
    [InlineData(StoreFixtures.DotaAppId)]
    [InlineData(StoreFixtures.TeamFortressAppId)]
    public void GetItems_tags_arrive_in_descending_weight_order(string appId)
    {
        // This is where rank comes from. Ties happen (TF2 has two tags at 343),
        // so the ordering is non-increasing, not strictly decreasing.
        var weights = Item(appId).GetProperty("tags")
            .EnumerateArray()
            .Select(t => t.GetProperty("weight").GetInt64())
            .ToArray();

        Assert.Equal(weights.OrderByDescending(w => w).ToArray(), weights);
    }

    [Theory]
    [InlineData(StoreFixtures.EldenRingAppId)]
    [InlineData(StoreFixtures.DotaAppId)]
    [InlineData(StoreFixtures.TeamFortressAppId)]
    public void GetItems_tagids_mirrors_tags_in_the_same_order(string appId)
    {
        var item = Item(appId);
        var fromTags = item.GetProperty("tags").EnumerateArray()
            .Select(t => t.GetProperty("tagid").GetInt64()).ToArray();
        var fromTagIds = item.GetProperty("tagids").EnumerateArray()
            .Select(t => t.GetInt64()).ToArray();

        Assert.Equal(fromTags, fromTagIds);
    }

    [Fact]
    public void GetItems_top_tag_for_elden_ring_is_souls_like()
    {
        // The §4.3 "highest-signal metadata" claim, end to end: the rank-1 tag
        // resolves through the vocabulary to the word a human would use.
        var topTagId = Item(StoreFixtures.EldenRingAppId)
            .GetProperty("tags")[0].GetProperty("tagid").GetInt64();

        Assert.Equal(StoreFixtures.SoulsLikeTagId, topTagId);
        Assert.Equal("Souls-like", TagNames()[topTagId]);
    }

    /// <summary>
    /// Steam mixes numeric encodings inside one object, which is why the parser
    /// reads numbers from strings everywhere. If this ever stops being true it is
    /// still safe — but it is worth knowing it was true.
    /// </summary>
    [Fact]
    public void GetItems_encodes_some_numbers_as_strings()
    {
        var price = Item(StoreFixtures.EldenRingAppId)
            .GetProperty("best_purchase_option")
            .GetProperty("final_price_in_cents");

        Assert.Equal(JsonValueKind.String, price.ValueKind);
        Assert.Equal("5999", price.GetString());
    }

    // ── GetTagList ───────────────────────────────────────────────────────────

    [Fact]
    public void GetTagList_wraps_a_version_hash_and_a_tag_array()
    {
        using var document = JsonDocument.Parse(StoreFixtures.TagListResponse());
        var response = document.RootElement.GetProperty("response");

        Assert.Equal(JsonValueKind.String, response.GetProperty("version_hash").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(response.GetProperty("version_hash").GetString()));

        var tags = response.GetProperty("tags");
        Assert.Equal(JsonValueKind.Array, tags.ValueKind);

        // 446 when captured. Asserted as a floor rather than an equality: the
        // vocabulary legitimately grows, and a *collapse* is the failure mode
        // worth catching.
        Assert.True(tags.GetArrayLength() >= 400, $"tag vocabulary shrank to {tags.GetArrayLength()}");
    }

    [Fact]
    public void GetTagList_carries_the_tags_section_4_3_names_by_example()
    {
        var names = TagNames();

        Assert.Equal("Souls-like", names[StoreFixtures.SoulsLikeTagId]);
        Assert.Equal("Roguelike Deckbuilder", names[StoreFixtures.RoguelikeDeckbuilderTagId]);
        Assert.Equal("RPG", names[122]);
        Assert.Equal("Strategy", names[9]);
    }

    // ── The client's projection of the captured bytes ────────────────────────

    [Fact]
    public async Task Client_reads_names_and_ranks_out_of_the_captured_response()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.CapturedResponder());

        var items = await host.Client.GetItemsAsync([
            StoreFixtures.EldenRingAppId,
            StoreFixtures.DotaAppId,
            StoreFixtures.TeamFortressAppId,
            StoreFixtures.NonStoreAppId,
        ]);

        // The name is what M1 needs — the thing that replaces "App 1245620".
        Assert.Equal("ELDEN RING", items[StoreFixtures.EldenRingAppId].Name);
        Assert.Equal("Dota 2", items[StoreFixtures.DotaAppId].Name);
        Assert.Equal("Team Fortress 2", items[StoreFixtures.TeamFortressAppId].Name);

        // A non-store app is absent from the results, not an error.
        Assert.DoesNotContain(StoreFixtures.NonStoreAppId, items.Keys);

        var eldenRing = items[StoreFixtures.EldenRingAppId].Tags;
        Assert.Equal(20, eldenRing.Count);
        Assert.Equal(StoreFixtures.SoulsLikeTagId, eldenRing[0].TagId);
        Assert.Equal(Enumerable.Range(1, 20), eldenRing.Select(t => t.Rank));
    }

    [Fact]
    public async Task Client_reads_the_vocabulary_out_of_the_captured_response()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.CapturedResponder());

        var vocabulary = await host.Client.GetTagListAsync();

        Assert.Equal("711684454", vocabulary.VersionHash);
        Assert.True(vocabulary.Names.Count >= 400);
        Assert.Equal("Souls-like", vocabulary.NameFor(StoreFixtures.SoulsLikeTagId));
        Assert.Equal("Roguelike Deckbuilder", vocabulary.NameFor(StoreFixtures.RoguelikeDeckbuilderTagId));
        Assert.Null(vocabulary.NameFor(-1));
    }

    /// <summary>
    /// One request, the whole batch — the property that turns a ~100k-app
    /// backfill into ~1000 requests instead of ~100k, and a 616-game library
    /// into 7.
    /// </summary>
    [Fact]
    public async Task Client_sends_one_request_for_the_whole_captured_batch()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.CapturedResponder());

        await host.Client.GetItemsAsync([
            StoreFixtures.EldenRingAppId,
            StoreFixtures.DotaAppId,
            StoreFixtures.TeamFortressAppId,
            StoreFixtures.NonStoreAppId,
        ]);

        var request = Assert.Single(host.Handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(SteamStoreTestHost.GetItems, request.Endpoint);
        Assert.EndsWith("/IStoreBrowseService/GetItems/v1/", request.Uri.AbsolutePath, StringComparison.Ordinal);
    }

    /// <summary>
    /// The request shape the spike verified, rebuilt by the client. If any of
    /// this drifts, we are no longer sending the query that was proven to work.
    /// </summary>
    [Fact]
    public async Task Client_sends_the_input_json_query_the_spike_verified()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.CapturedResponder());

        await host.Client.GetItemsAsync([StoreFixtures.EldenRingAppId]);

        using var query = JsonDocument.Parse(host.Handler.Requests[0].InputJson);
        var root = query.RootElement;

        Assert.Equal(1245620, root.GetProperty("ids")[0].GetProperty("appid").GetInt64());

        var context = root.GetProperty("context");
        Assert.Equal("english", context.GetProperty("language").GetString());
        Assert.Equal("US", context.GetProperty("country_code").GetString());
        Assert.Equal(1, context.GetProperty("steam_realm").GetInt32());

        var data = root.GetProperty("data_request");
        Assert.Equal(20, data.GetProperty("include_tag_count").GetInt32());
        Assert.True(data.GetProperty("include_basic_info").GetBoolean());
        Assert.True(data.GetProperty("include_assets").GetBoolean());
        Assert.True(data.GetProperty("include_release").GetBoolean());
        Assert.True(data.GetProperty("include_platforms").GetBoolean());

        // No key= parameter anywhere: these endpoints are keyless, verified by
        // contrast against IStoreService/GetAppList which 403s without one.
        Assert.DoesNotContain("key=", host.Handler.Requests[0].Uri.Query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>§4.3: set a descriptive User-Agent so Valve can attribute the traffic.</summary>
    [Fact]
    public async Task Client_identifies_itself_on_every_request()
    {
        using var host = new SteamStoreTestHost(SteamStoreTestHost.CapturedResponder());

        await host.Client.GetItemsAsync([StoreFixtures.EldenRingAppId]);
        await host.Client.GetTagListAsync();

        Assert.Equal(2, host.Handler.Requests.Count);
        Assert.All(host.Handler.Requests, r =>
        {
            Assert.NotNull(r.UserAgent);
            Assert.Contains("Hoard", r.UserAgent, StringComparison.Ordinal);
            Assert.Contains("hoard-app", r.UserAgent, StringComparison.Ordinal);
        });
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static IEnumerable<JsonElement> Items()
    {
        // Cloned out of the document so the caller is not reading freed memory
        // once the JsonDocument is disposed.
        using var document = JsonDocument.Parse(StoreFixtures.GetItemsResponse());
        return document.RootElement.GetProperty("response").GetProperty("store_items")
            .EnumerateArray()
            .Select(e => e.Clone())
            .ToArray();
    }

    private static JsonElement Item(string appId)
    {
        var id = long.Parse(appId, CultureInfo.InvariantCulture);
        return Items().Single(i => i.GetProperty("id").GetInt64() == id);
    }

    private static IReadOnlyDictionary<long, string> TagNames()
    {
        using var document = JsonDocument.Parse(StoreFixtures.TagListResponse());
        return document.RootElement.GetProperty("response").GetProperty("tags")
            .EnumerateArray()
            .ToDictionary(t => t.GetProperty("tagid").GetInt64(), t => t.GetProperty("name").GetString()!);
    }
}

/// <summary>
/// The other half of the contract story: what the client does when the shape
/// pinned above stops holding.
///
/// <para>The spike's instruction is explicit — "treat a shape change as
/// expected: fail soft to IGDB, never error the enrichment pass". Every case
/// here degrades to empty and, critically, writes nothing to the cache: a
/// changed envelope must not be recorded as "Steam has never heard of these
/// games" for a whole TTL.</para>
/// </summary>
public class ShapeChangeTests
{
    [Fact]
    public async Task Renamed_envelope_degrades_to_empty_and_caches_nothing()
    {
        var mutated = StoreFixtures.GetItemsResponse().Replace("store_items", "items", StringComparison.Ordinal);
        using var host = new SteamStoreTestHost(
            (_, _) => FakeStoreHandler.Json(HttpStatusCode.OK, mutated));

        var items = await host.Client.GetItemsAsync([StoreFixtures.EldenRingAppId]);

        Assert.Empty(items);
        await AssertNothingCached(host, StoreFixtures.EldenRingAppId);
    }

    [Fact]
    public async Task Missing_response_wrapper_degrades_to_empty()
    {
        using var host = new SteamStoreTestHost(
            (_, _) => FakeStoreHandler.Json(HttpStatusCode.OK, """{"store_items":[{"id":1245620}]}"""));

        Assert.Empty(await host.Client.GetItemsAsync([StoreFixtures.EldenRingAppId]));
        await AssertNothingCached(host, StoreFixtures.EldenRingAppId);
    }

    /// <summary>
    /// An empty <c>store_items</c> for a non-empty request is a shape change
    /// wearing a 200. Believing it would cache a library's worth of misses.
    /// </summary>
    [Fact]
    public async Task Empty_store_items_for_a_real_request_is_not_believed()
    {
        using var host = new SteamStoreTestHost(
            (_, _) => FakeStoreHandler.Json(HttpStatusCode.OK, """{"response":{"store_items":[]}}"""));

        Assert.Empty(await host.Client.GetItemsAsync([StoreFixtures.EldenRingAppId]));
        await AssertNothingCached(host, StoreFixtures.EldenRingAppId);
    }

    /// <summary>
    /// Tags disappearing is a *partial* change: the name still arrives, so the
    /// M1 job keeps working and only the unbuilt-on half degrades. This is why
    /// the name and the tags are read independently.
    /// </summary>
    [Fact]
    public async Task Renamed_tags_key_still_yields_the_name()
    {
        var mutated = StoreFixtures.GetItemsResponse()
            .Replace("\"tags\":", "\"tag_list\":", StringComparison.Ordinal);
        using var host = new SteamStoreTestHost(
            (_, _) => FakeStoreHandler.Json(HttpStatusCode.OK, mutated));

        var items = await host.Client.GetItemsAsync([StoreFixtures.EldenRingAppId]);

        Assert.Equal("ELDEN RING", items[StoreFixtures.EldenRingAppId].Name);
        Assert.Empty(items[StoreFixtures.EldenRingAppId].Tags);
    }

    [Fact]
    public async Task Renamed_tag_vocabulary_degrades_to_empty()
    {
        var mutated = StoreFixtures.TagListResponse()
            .Replace("\"tags\":", "\"tag_list\":", StringComparison.Ordinal);
        using var host = new SteamStoreTestHost(
            (_, _) => FakeStoreHandler.Json(HttpStatusCode.OK, mutated));

        var vocabulary = await host.Client.GetTagListAsync();

        Assert.Empty(vocabulary.Names);
        Assert.Equal(string.Empty, vocabulary.VersionHash);
    }

    /// <summary>
    /// A vocabulary already on disk outlives the endpoint changing shape: tag
    /// names do not change meaning, and the alternative is unresolvable ids.
    /// </summary>
    [Fact]
    public async Task Stale_vocabulary_survives_a_shape_change()
    {
        var broken = false;
        using var host = new SteamStoreTestHost(
            (_, _) => FakeStoreHandler.Json(
                HttpStatusCode.OK,
                broken
                    ? StoreFixtures.TagListResponse().Replace("\"tags\":", "\"nope\":", StringComparison.Ordinal)
                    : StoreFixtures.TagListResponse()));

        Assert.Equal("Souls-like", (await host.Client.GetTagListAsync()).NameFor(StoreFixtures.SoulsLikeTagId));

        broken = true;
        host.Clock.Advance(TimeSpan.FromDays(365));

        var afterChange = await host.Client.GetTagListAsync();
        Assert.Equal("Souls-like", afterChange.NameFor(StoreFixtures.SoulsLikeTagId));
        Assert.Equal(2, host.Handler.CountFor(SteamStoreTestHost.GetTagList));
    }

    private static async Task AssertNothingCached(SteamStoreTestHost host, string appId)
    {
        var entry = await host.Cache.GetAsync(
            Hoard.Enrich.Steam.SteamStoreClient.CacheProvider,
            Hoard.Enrich.Steam.SteamStoreClient.AppCacheKey(appId));

        Assert.Null(entry);
    }
}
