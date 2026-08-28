using System.Globalization;
using System.Net;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Storage;
using Xunit;

namespace Winnow.Tests.Igdb;

/// <summary>
/// The Apicalypse surface: the <c>external_games</c> hard join (§4.4), batching,
/// and the caching rule that a hit inside the TTL never touches the network.
/// </summary>
public class IgdbClientTests
{
    /// <summary>
    /// The real M0 number: 616 Steam games resolved from local files, ~600 of
    /// them still named <c>App &lt;appid&gt;</c> and waiting for a title.
    /// </summary>
    private const int M0LibrarySize = 616;

    private static string[] LibraryAppIds(int count)
        => Enumerable.Range(1, count)
            .Select(i => (10 * i).ToString(CultureInfo.InvariantCulture))
            .ToArray();

    [Fact]
    public async Task Resolving_616_appids_costs_two_requests_not_616()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        var matches = await host.Client.ResolveBySteamAppIdsAsync(LibraryAppIds(M0LibrarySize));

        // This assertion is the entire reason external_games is used with a
        // `where uid = (…)` list: an M0-sized library is two requests, which at
        // 4 req/s is under a second, not 616 requests and two and a half
        // minutes of rate-limited crawling.
        Assert.Equal(2, host.Handler.CountFor("external_games"));
        Assert.Equal(M0LibrarySize, matches.Count);

        // 400 ids in the first request (the batch size), 216 in the second.
        var bodies = host.Handler.Requests
            .Where(r => r.Endpoint == "external_games")
            .Select(r => IgdbFixtures.RequestedUids(r.Body).Count)
            .ToArray();
        Assert.Equal([400, 216], bodies);
    }

    [Fact]
    public async Task Fetching_616_games_costs_two_requests()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        var games = await host.Client.GetGamesAsync(
            Enumerable.Range(1, M0LibrarySize).Select(i => (long)(100_000 + i)));

        Assert.Equal(2, host.Handler.CountFor("games"));
        Assert.Equal(M0LibrarySize, games.Count);
    }

    [Fact]
    public async Task Query_is_a_text_plain_post_filtered_on_the_steam_external_game_source()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        await host.Client.ResolveBySteamAppIdsAsync(["440", "570"]);

        var query = host.Handler.Requests.Single(r => r.Endpoint == "external_games");
        Assert.Equal(HttpMethod.Post, query.Method);
        Assert.Equal("text/plain", query.ContentType);
        Assert.EndsWith("/v4/external_games", query.Uri.AbsolutePath, StringComparison.Ordinal);

        // external_game_source, not the deprecated `category` enum.
        Assert.Contains("where external_game_source = 1", query.Body, StringComparison.Ordinal);
        Assert.Contains("uid = (\"440\",\"570\")", query.Body, StringComparison.Ordinal);
        Assert.Contains("limit 500;", query.Body, StringComparison.Ordinal);
        Assert.Contains("fields uid,game,game.name", query.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Steam_appid_maps_to_igdb_id_with_name_cover_year_and_summary()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        var matches = await host.Client.ResolveBySteamAppIdsAsync(["440"]);

        var match = matches["440"];
        Assert.Equal(IgdbFixtures.IgdbIdForAppId("440"), match.IgdbId);
        Assert.Equal("Game 440", match.Name);
        Assert.Equal(2008, match.FirstReleaseYear);
        Assert.Equal("A canned summary.", match.Summary);

        // image_id wins over the protocol-relative t_thumb url IGDB returns.
        Assert.Equal("https://images.igdb.com/igdb/image/upload/t_cover_big/co440.jpg", match.CoverUrl);
    }

    [Fact]
    public async Task Game_metadata_carries_genres_themes_and_publisher_only()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        var game = Assert.Single(await host.Client.GetGamesAsync([100_440]));

        Assert.Equal("Game 100440", game.Name);
        Assert.Equal(["Shooter", "Adventure"], game.Genres);
        Assert.Equal(["Action"], game.Themes);

        // involved_companies carries developers too; only publishers are kept.
        Assert.Equal(["Valve"], game.Publishers);
    }

    [Fact]
    public async Task Cache_hit_inside_the_ttl_does_not_touch_the_network()
    {
        var cache = new InMemoryMetadataCache();
        var appIds = LibraryAppIds(50);

        using (var cold = new IgdbTestHost(IgdbTestHost.DefaultResponder(), cache: cache))
        {
            await cold.Client.ResolveBySteamAppIdsAsync(appIds);
            Assert.Equal(1, cold.Handler.CountFor("external_games"));
        }

        using var warm = new IgdbTestHost(IgdbTestHost.DefaultResponder(), cache: cache);
        var matches = await warm.Client.ResolveBySteamAppIdsAsync(appIds);

        Assert.Equal(50, matches.Count);
        Assert.Empty(warm.Handler.Requests);
    }

    [Fact]
    public async Task Cache_entry_older_than_the_ttl_is_refetched()
    {
        var cache = new InMemoryMetadataCache();
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder(), cache: cache);

        await host.Client.ResolveBySteamAppIdsAsync(["440"]);
        Assert.Equal(1, host.Handler.CountFor("external_games"));

        // Inside the default 30-day TTL: still free.
        host.Clock.Advance(TimeSpan.FromDays(29));
        await host.Client.ResolveBySteamAppIdsAsync(["440"]);
        Assert.Equal(1, host.Handler.CountFor("external_games"));

        host.Clock.Advance(TimeSpan.FromDays(2));
        await host.Client.ResolveBySteamAppIdsAsync(["440"]);
        Assert.Equal(2, host.Handler.CountFor("external_games"));
    }

    [Fact]
    public async Task Per_call_ttl_overrides_the_default()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        await host.Client.ResolveBySteamAppIdsAsync(["440"]);
        host.Clock.Advance(TimeSpan.FromHours(2));

        await host.Client.ResolveBySteamAppIdsAsync(["440"], cacheTtl: TimeSpan.FromHours(1));

        Assert.Equal(2, host.Handler.CountFor("external_games"));
    }

    [Fact]
    public async Task Appid_igdb_has_no_record_of_is_cached_as_a_miss_and_not_asked_about_again()
    {
        var cache = new InMemoryMetadataCache();
        var unknown = new HashSet<string>(StringComparer.Ordinal) { "999999" };
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder(unknown), cache: cache);

        var first = await host.Client.ResolveBySteamAppIdsAsync(["440", "999999"]);
        Assert.Single(first);
        Assert.False(first.ContainsKey("999999"));

        var second = await host.Client.ResolveBySteamAppIdsAsync(["440", "999999"]);
        Assert.Single(second);

        // A negative answer is worth caching: re-asking every run spends the
        // 4 req/s budget learning the same nothing.
        Assert.Equal(1, host.Handler.CountFor("external_games"));
    }

    [Fact]
    public async Task A_failed_batch_is_not_cached_as_a_miss()
    {
        var cache = new InMemoryMetadataCache();
        using (var broken = new IgdbTestHost(
            (request, _) => request.Endpoint switch
            {
                "token" => FakeHttpMessageHandler.Json(HttpStatusCode.OK, IgdbFixtures.TokenResponse("t")),
                _ => FakeHttpMessageHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"),
            },
            configure: o =>
            {
                o.MaxRetryAttempts = 1;
                o.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
            },
            cache: cache))
        {
            Assert.Empty(await broken.Client.ResolveBySteamAppIdsAsync(["440"]));
        }

        // If the 503 had been recorded as "IGDB has no such game", this appid
        // would stay unresolved for a whole TTL.
        Assert.Null(await cache.GetAsync(IgdbClient.CacheProvider, IgdbClient.SteamAppCacheKey("440")));

        using var healthy = new IgdbTestHost(IgdbTestHost.DefaultResponder(), cache: cache);
        Assert.Single(await healthy.Client.ResolveBySteamAppIdsAsync(["440"]));
    }

    [Fact]
    public async Task Unconfigured_client_is_a_silent_no_op_not_an_exception()
    {
        using var host = new IgdbTestHost(
            IgdbTestHost.DefaultResponder(), clientId: null, clientSecret: null);

        Assert.False(await host.Client.IsConfiguredAsync());

        var matches = await host.Client.ResolveBySteamAppIdsAsync(["440", "570"]);
        var games = await host.Client.GetGamesAsync([100_440]);

        Assert.Empty(matches);
        Assert.Empty(games);

        // Nothing was attempted: no token mint, no query, no exception.
        Assert.Empty(host.Handler.Requests);
    }

    [Fact]
    public async Task Unconfigured_client_still_serves_what_is_already_cached()
    {
        var cache = new InMemoryMetadataCache();
        using (var configured = new IgdbTestHost(IgdbTestHost.DefaultResponder(), cache: cache))
        {
            await configured.Client.ResolveBySteamAppIdsAsync(["440"]);
        }

        using var unconfigured = new IgdbTestHost(
            IgdbTestHost.DefaultResponder(), clientId: null, clientSecret: null, cache: cache);

        var matches = await unconfigured.Client.ResolveBySteamAppIdsAsync(["440", "570"]);

        Assert.Single(matches);
        Assert.True(matches.ContainsKey("440"));
        Assert.Empty(unconfigured.Handler.Requests);
    }

    [Fact]
    public async Task Empty_and_malformed_inputs_short_circuit_without_a_request()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        Assert.Empty(await host.Client.ResolveBySteamAppIdsAsync([]));
        Assert.Empty(await host.Client.ResolveBySteamAppIdsAsync(["", "   "]));
        Assert.Empty(await host.Client.GetGamesAsync([]));
        Assert.Empty(host.Handler.Requests);
    }

    [Fact]
    public async Task Duplicate_appids_are_collapsed_into_one_query_value()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        var matches = await host.Client.ResolveBySteamAppIdsAsync(["440", "440", "570", "440"]);

        Assert.Equal(2, matches.Count);
        var query = host.Handler.Requests.Single(r => r.Endpoint == "external_games");
        Assert.Equal(["440", "570"], IgdbFixtures.RequestedUids(query.Body));
    }

    [Theory]
    [InlineData("440\";--")]
    [InlineData("a\\b")]
    [InlineData("4 4 0;")]
    public void Values_that_could_escape_an_apicalypse_string_are_rejected(string value)
        => Assert.False(Apicalypse.IsSafeStringValue(value));
}
