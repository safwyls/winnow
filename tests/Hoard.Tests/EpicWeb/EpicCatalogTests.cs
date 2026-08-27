using System.Net;
using Hoard.Core.Queries;
using Hoard.Ingest.Epic.Web;
using Hoard.Ingest.Epic.Web.Model;
using Xunit;

namespace Hoard.Tests.EpicWeb;

/// <summary>
/// The catalog service — the endpoint that answers the two questions the library
/// service cannot: what is this owned entitlement called, and what kind of thing
/// is it.
///
/// <para>Nothing here opens a socket. Every response is a canned fixture, per the
/// enrichment charter's rule.</para>
/// </summary>
public sealed class EpicCatalogTests
{
    private static readonly string[] Everything =
    [
        EpicFixturesWeb.FezCatalogItemId,
        EpicFixturesWeb.DlcCatalogItemId,
        EpicFixturesWeb.EngineCatalogItemId,
        EpicFixturesWeb.AssetPackCatalogItemId,
        EpicFixturesWeb.UntitledCatalogItemId,
        EpicFixturesWeb.UncategorisedCatalogItemId,
        EpicFixturesWeb.UnknownToCatalogCatalogItemId,
    ];

    // ── The bug this whole file exists for ───────────────────────────────────

    [Fact]
    public async Task The_catalog_service_names_entitlements_the_library_endpoint_returns_nameless()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());
        await host.SignInAsync();

        // The library endpoint's own answer: not one title among them. This is
        // what put "App 16a66a9f5630407d923429470bd5c967" in the grid.
        var library = await host.Client.GetOwnedLibraryAsync();
        Assert.All(library.Items, item => Assert.Null(item.Title));

        var catalog = await host.Catalog.GetItemsAsync(Everything);

        Assert.Equal("Fez", catalog[EpicFixturesWeb.FezCatalogItemId].Title);
        Assert.Equal("Unreal Engine", catalog[EpicFixturesWeb.EngineCatalogItemId].Title);
        Assert.Equal("Infinity Blade: Effects", catalog[EpicFixturesWeb.AssetPackCatalogItemId].Title);
    }

    [Fact]
    public async Task Categories_come_back_and_feed_the_existing_game_filter()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());
        await host.SignInAsync();

        var catalog = await host.Catalog.GetItemsAsync(Everything);

        // Games.
        Assert.True(catalog[EpicFixturesWeb.FezCatalogItemId].IsGame);

        // Not games — an engine build and a marketplace asset pack, which is
        // most of what the API half was adding to the grid.
        Assert.False(catalog[EpicFixturesWeb.EngineCatalogItemId].IsGame);
        Assert.False(catalog[EpicFixturesWeb.AssetPackCatalogItemId].IsGame);
        Assert.False(catalog[EpicFixturesWeb.UntitledCatalogItemId].IsGame);

        // And the verdict is the SAME rule the local scan applies, not a copy.
        Assert.Equal(
            EpicGameFilter.IsGame(catalog[EpicFixturesWeb.EngineCatalogItemId].Categories),
            catalog[EpicFixturesWeb.EngineCatalogItemId].IsGame);
    }

    [Fact]
    public async Task A_DLC_that_looks_like_a_base_game_by_category_is_still_reported_as_a_game()
    {
        // The trap the spike names: "Borderlands 3 Bounty of Blood" carries
        // application/games/applications and is indistinguishable from a base
        // game by category. Its only marker is a non-empty mainGameItem.
        //
        // It stays a GAME for the non-game filter's purposes, deliberately.
        // Hiding on parentage would also hide LEGO Fortnite: Odyssey, which
        // carries a mainGameItem pointing at Fortnite and 408 minutes of the
        // user's recorded time.
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());
        await host.SignInAsync();

        var dlc = (await host.Catalog.GetItemsAsync(Everything))[EpicFixturesWeb.DlcCatalogItemId];

        Assert.True(dlc.IsGame);
        Assert.True(dlc.IsDlc);
        Assert.Equal(EpicFixturesWeb.FezCatalogItemId, dlc.MainGameCatalogItemId);
    }

    [Fact]
    public async Task Both_spellings_of_the_parent_id_are_read()
    {
        // The live response carries mainGameItem (an object) on some entries and
        // only mainGameItemList (an array) on others. Reading one spelling would
        // silently classify half a library's DLC as base games.
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());
        await host.SignInAsync();
        var catalog = await host.Catalog.GetItemsAsync(Everything);

        Assert.Equal(
            EpicFixturesWeb.FezCatalogItemId,
            catalog[EpicFixturesWeb.DlcCatalogItemId].MainGameCatalogItemId);

        // An empty mainGameItemList is a base game, not a DLC of nothing.
        Assert.Null(catalog[EpicFixturesWeb.FezCatalogItemId].MainGameCatalogItemId);
        Assert.False(catalog[EpicFixturesWeb.FezCatalogItemId].IsDlc);
    }

    [Fact]
    public async Task The_appName_comes_back_too_so_an_API_only_title_has_a_route_to_IGDB()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());
        await host.SignInAsync();

        var catalog = await host.Catalog.GetItemsAsync(Everything);

        Assert.Equal(EpicFixturesWeb.FezAppName, catalog[EpicFixturesWeb.FezCatalogItemId].AppName);
        Assert.Equal("UE_4.0", catalog[EpicFixturesWeb.EngineCatalogItemId].AppName);
    }

    // ── Silence must never be recorded as an answer ──────────────────────────

    [Fact]
    public async Task An_entry_with_no_title_is_still_classified_and_reports_no_name()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());
        await host.SignInAsync();

        var untitled = (await host.Catalog.GetItemsAsync(Everything))[EpicFixturesWeb.UntitledCatalogItemId];

        Assert.Null(untitled.Title);
        Assert.False(untitled.IsGame);
        Assert.Equal("hidden", untitled.CategoriesValue);
    }

    [Fact]
    public async Task An_entry_with_no_categories_reports_cannot_say_rather_than_not_a_game()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());
        await host.SignInAsync();

        var item = (await host.Catalog.GetItemsAsync(Everything))[EpicFixturesWeb.UncategorisedCatalogItemId];

        Assert.Equal("Uncategorised Thing", item.Title);

        // Null, not false. An unclassified item is visible, and the null is what
        // makes the repository's COALESCE leave the column alone.
        Assert.Null(item.IsGame);
        Assert.Null(item.CategoriesValue);
    }

    [Fact]
    public async Task An_id_the_service_does_not_recognise_is_absent_from_the_result()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());
        await host.SignInAsync();

        var catalog = await host.Catalog.GetItemsAsync(Everything);

        Assert.DoesNotContain(EpicFixturesWeb.UnknownToCatalogCatalogItemId, catalog.Keys);
    }

    [Fact]
    public async Task Nothing_is_returned_when_the_service_is_unreachable()
    {
        using var host = new EpicWebTestHost((request, _) => request.Endpoint switch
        {
            EpicEndpoint.Token => FakeEpicHandler.Json(HttpStatusCode.OK, EpicFixturesWeb.Token()),
            EpicEndpoint.LibraryItems => FakeEpicHandler.Json(
                HttpStatusCode.OK, EpicFixturesWeb.LibraryMixed()),
            EpicEndpoint.Playtime => FakeEpicHandler.Json(HttpStatusCode.OK, EpicFixturesWeb.Playtime()),
            EpicEndpoint.CatalogItems => FakeEpicHandler.Json(
                HttpStatusCode.ServiceUnavailable, "{\"errorCode\":\"boom\"}"),
            _ => FakeEpicHandler.Json(HttpStatusCode.NotFound, "{}"),
        });
        await host.SignInAsync();

        // Empty, not an exception, and — see the next test — nothing cached.
        Assert.Empty(await host.Catalog.GetItemsAsync(Everything));
    }

    [Fact]
    public async Task A_transport_failure_is_not_cached_but_a_definite_miss_is()
    {
        var cache = new InMemoryEpicCatalogCache();
        var fail = true;

        using var host = new EpicWebTestHost(
            (request, _) => request.Endpoint switch
            {
                EpicEndpoint.Token => FakeEpicHandler.Json(HttpStatusCode.OK, EpicFixturesWeb.Token()),
                EpicEndpoint.LibraryItems => FakeEpicHandler.Json(
                    HttpStatusCode.OK, EpicFixturesWeb.LibraryMixed()),
                EpicEndpoint.Playtime => FakeEpicHandler.Json(
                    HttpStatusCode.OK, EpicFixturesWeb.Playtime()),
                EpicEndpoint.CatalogItems => fail
                    ? FakeEpicHandler.Json(HttpStatusCode.ServiceUnavailable, "{}")
                    : FakeEpicHandler.Json(
                        HttpStatusCode.OK,
                        request.CatalogNamespace == EpicFixturesWeb.GamesNamespace
                            ? EpicFixturesWeb.CatalogGames()
                            : EpicFixturesWeb.CatalogEngine()),
                _ => FakeEpicHandler.Json(HttpStatusCode.NotFound, "{}"),
            },
            catalogCache: cache);
        await host.SignInAsync();

        await host.Catalog.GetItemsAsync(Everything);

        // A 503 recorded nothing. Caching it would have said "Epic knows nothing
        // about these" for a whole TTL on the strength of one bad minute.
        Assert.Null(await cache.GetAsync(EpicFixturesWeb.FezCatalogItemId));

        fail = false;
        var catalog = await host.Catalog.GetItemsAsync(Everything);
        Assert.Equal("Fez", catalog[EpicFixturesWeb.FezCatalogItemId].Title);

        // The id the service answered about and did not recognise IS cached, as
        // a null payload — an answer worth keeping, and the difference between a
        // row that exists with no payload and no row at all.
        var miss = await cache.GetAsync(EpicFixturesWeb.UnknownToCatalogCatalogItemId);
        Assert.NotNull(miss);
        Assert.Null(miss!.Value.PayloadJson);
    }

    [Fact]
    public async Task A_cached_answer_and_a_cached_miss_both_keep_the_second_run_off_the_wire()
    {
        var cache = new InMemoryEpicCatalogCache();
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog(), catalogCache: cache);
        await host.SignInAsync();

        await host.Catalog.GetItemsAsync(Everything);
        var afterFirst = host.Handler.CountFor(EpicEndpoint.CatalogItems);
        Assert.True(afterFirst > 0);

        var second = await host.Catalog.GetItemsAsync(Everything);

        Assert.Equal(afterFirst, host.Handler.CountFor(EpicEndpoint.CatalogItems));
        Assert.Equal("Fez", second[EpicFixturesWeb.FezCatalogItemId].Title);
        Assert.DoesNotContain(EpicFixturesWeb.UnknownToCatalogCatalogItemId, second.Keys);
    }

    [Fact]
    public async Task Nothing_is_requested_when_there_is_no_session()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());

        // Deliberately not signed in.
        Assert.Empty(await host.Catalog.GetItemsAsync(Everything));
        Assert.Equal(0, host.Handler.CountFor(EpicEndpoint.CatalogItems));
    }

    [Fact]
    public async Task An_id_the_owned_library_does_not_carry_cannot_be_asked_about()
    {
        // The route is keyed by (namespace, catalogItemId) and only the library
        // knows the namespace. An id with no namespace is absent from the result
        // and costs no request — the same "learned nothing" as every other
        // failure, which is exactly why the caller cannot tell them apart.
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());
        await host.SignInAsync();

        var catalog = await host.Catalog.GetItemsAsync(["ffffffffffffffffffffffffffffffff"]);

        Assert.Empty(catalog);
        Assert.Equal(0, host.Handler.CountFor(EpicEndpoint.CatalogItems));
    }

    [Fact]
    public async Task Requests_are_grouped_by_namespace_and_carry_only_the_ids_asked_for()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());
        await host.SignInAsync();

        await host.Catalog.GetItemsAsync(
            [EpicFixturesWeb.FezCatalogItemId, EpicFixturesWeb.EngineCatalogItemId]);

        var requests = host.Handler.Requests
            .Where(r => r.Endpoint == EpicEndpoint.CatalogItems)
            .ToArray();

        Assert.Equal(2, requests.Length);
        Assert.Equal(
            [EpicFixturesWeb.FezCatalogItemId],
            requests.Single(r => r.CatalogNamespace == EpicFixturesWeb.GamesNamespace).CatalogIds);
        Assert.Equal(
            [EpicFixturesWeb.EngineCatalogItemId],
            requests.Single(r => r.CatalogNamespace == EpicFixturesWeb.EngineNamespace).CatalogIds);
    }

    [Fact]
    public async Task The_bearer_token_is_attached_and_never_logged()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());
        await host.SignInAsync();
        await host.Catalog.GetItemsAsync(Everything);

        var request = host.Handler.Requests.First(r => r.Endpoint == EpicEndpoint.CatalogItems);
        Assert.StartsWith("Bearer ", request.Authorization, StringComparison.Ordinal);

        // Not a single logged line may contain the token or an owned catalog id.
        var log = host.Logs.AllText;
        Assert.DoesNotContain("Bearer", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(EpicFixturesWeb.FezCatalogItemId, log, StringComparison.OrdinalIgnoreCase);
    }

    // ── The stored form ─────────────────────────────────────────────────────

    [Fact]
    public async Task The_stored_form_round_trips_through_the_shared_filter()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());
        await host.SignInAsync();
        var catalog = await host.Catalog.GetItemsAsync(Everything);

        var stored = catalog[EpicFixturesWeb.FezCatalogItemId].CategoriesValue;

        // Epic's own flattening: comma-joined, no spaces, storefront order.
        Assert.Equal("public,games,applications", stored);
        Assert.True(EpicGameFilter.IsGame(stored));
        Assert.False(NonGameEntries.IsNonGameEpicCategories(stored));

        var engine = catalog[EpicFixturesWeb.EngineCatalogItemId].CategoriesValue;
        Assert.Equal("engines,engines/ue4", engine);
        Assert.True(NonGameEntries.IsNonGameEpicCategories(engine));
    }

    [Fact]
    public void Unknown_categories_are_visible_not_hidden()
    {
        // The 0006 rule restated for Epic: an unread classification is never
        // evidence. Hundreds of Epic works predate this column.
        Assert.False(NonGameEntries.IsNonGameEpicCategories(null));
        Assert.False(NonGameEntries.IsNonGameEpicCategories(string.Empty));
        Assert.False(NonGameEntries.IsNonGameEpicCategories("   "));
        Assert.Null(EpicGameFilter.IsGame((string?)null));
    }

    [Fact]
    public void A_category_Epic_has_not_invented_yet_does_not_make_something_a_game()
    {
        // The complement of the Steam rule, and deliberately so. Steam's list is
        // a closed set of things that cannot be games, so an unrecognised type
        // stays visible. Epic's rule is a positive test — "games AND
        // applications" — measured to admit every one of 73 games, so an
        // unrecognised category simply fails to satisfy it. Both keep precision
        // where the store's own vocabulary put it.
        Assert.False(EpicGameFilter.IsGame("public,someNewThingEpicInvented"));
        Assert.True(EpicGameFilter.IsGame("someNewThingEpicInvented,games,applications"));
    }

    [Fact]
    public void The_cache_payload_carries_no_account_identifier()
    {
        var item = new EpicCatalogItemInfo(
            "7a70b499513441c792b541d53505e0b2",
            "41f47fd0d3e248bc938a5815d6d64daa",
            "Fez",
            ["public", "games", "applications"],
            "Bluebird",
            null);

        var json = System.Text.Json.JsonSerializer.Serialize(item);

        // Derived properties are excluded, so the payload is exactly the six
        // fields — a catalog id, a namespace, a title, category paths and a
        // codename. Nothing about the person who owns it.
        Assert.DoesNotContain("IsGame", json, StringComparison.Ordinal);
        Assert.DoesNotContain("IsDlc", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CategoriesValue", json, StringComparison.Ordinal);

        var round = System.Text.Json.JsonSerializer.Deserialize<EpicCatalogItemInfo>(json)!;
        Assert.Equal(item.CatalogItemId, round.CatalogItemId);
        Assert.Equal(item.Namespace, round.Namespace);
        Assert.Equal(item.Title, round.Title);
        Assert.Equal(item.AppName, round.AppName);
        Assert.Equal(item.Categories, round.Categories);
        Assert.True(round.IsGame);
    }
}
