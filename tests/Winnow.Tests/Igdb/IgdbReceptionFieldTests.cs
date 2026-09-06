using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Storage;
using Xunit;
using Xunit.Abstractions;

namespace Winnow.Tests.Igdb;

/// <summary>
/// Verifies that the screenshot, artwork and rating fields ride the shared
/// games query (payload version 4) without widening requests or the search
/// query, that a version-3 cached payload triggers a refetch, and that a
/// library-wide refetch after the version bump costs no more requests than
/// the bump-less query did.
/// </summary>
public sealed class IgdbReceptionFieldTests
{
    private readonly ITestOutputHelper _output;

    public IgdbReceptionFieldTests(ITestOutputHelper output) => _output = output;

    private const string VersionThreeEnvelope =
        """
        {"version":3,"game":{"igdb_id":100440,"name":"Game 100440","cover_url":null,
        "first_release_year":2008,"summary":"A canned summary.","genres":["Shooter"],
        "themes":["Action"],"publishers":["Valve"],"game_modes":[],"player_perspectives":[],
        "platforms":["PC (Microsoft Windows)"],"game_type":"main_game","parent_game_id":null,
        "version_parent_id":null,"version_title":null}}
        """;

    [Fact]
    public void The_shared_games_query_asks_for_the_media_and_reception_fields()
    {
        var body = Apicalypse.Games([440], 500, 0);

        Assert.Contains("screenshots.image_id", body, StringComparison.Ordinal);
        Assert.Contains("artworks.image_id", body, StringComparison.Ordinal);
        Assert.Contains("rating,", body, StringComparison.Ordinal);
        Assert.Contains("rating_count", body, StringComparison.Ordinal);
        Assert.Contains("aggregated_rating", body, StringComparison.Ordinal);
        Assert.Contains("aggregated_rating_count", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_media_and_reception_fields_ride_the_query_that_already_existed()
    {
        var body = Apicalypse.Games([440], 500, 0);

        Assert.Equal(1, body.Split("fields ", StringSplitOptions.None).Length - 1);
        Assert.Contains("involved_companies.company.name", body, StringComparison.Ordinal);
        Assert.Contains("platforms.name", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_search_query_is_not_widened_by_them()
    {
        var body = Apicalypse.SearchGames("prey", 20);

        Assert.DoesNotContain("screenshots", body, StringComparison.Ordinal);
        Assert.DoesNotContain("aggregated_rating", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Screenshots_and_artworks_project_onto_image_ids()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        var game = Assert.Single(await host.Client.GetGamesAsync([100_440]));

        Assert.Equal(
            [IgdbFixtures.ScreenshotImageId(100_440, 1), IgdbFixtures.ScreenshotImageId(100_440, 2)],
            game.ScreenshotImageIds);

        Assert.Equal([IgdbFixtures.ArtworkImageId(100_440, 1)], game.ArtworkImageIds);
    }

    [Fact]
    public async Task The_two_igdb_figures_are_projected_apart_with_their_counts()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        var game = Assert.Single(await host.Client.GetGamesAsync([100_440]));

        Assert.Equal(IgdbFixtures.UserRating, game.UserRating);
        Assert.Equal(IgdbFixtures.UserRatingCount, game.UserRatingCount);
        Assert.Equal(IgdbFixtures.CriticRating, game.CriticRating);
        Assert.Equal(IgdbFixtures.CriticRatingCount, game.CriticRatingCount);
    }

    [Fact]
    public async Task A_game_igdb_has_no_figures_for_carries_nulls_rather_than_zeroes()
    {
        using var host = new IgdbTestHost((request, _) => request.Endpoint switch
        {
            "token" => FakeHttpMessageHandler.Json(
                System.Net.HttpStatusCode.OK, IgdbFixtures.TokenResponse("t")),
            "games" => FakeHttpMessageHandler.Json(
                System.Net.HttpStatusCode.OK,
                """
                [{"id":100440,"name":"Game 100440","rating":0,"rating_count":0,
                  "aggregated_rating":0,"aggregated_rating_count":0,
                  "screenshots":[],"artworks":[]}]
                """),
            _ => FakeHttpMessageHandler.Json(System.Net.HttpStatusCode.NotFound, "[]"),
        });

        var game = Assert.Single(await host.Client.GetGamesAsync([100_440]));

        Assert.Null(game.UserRating);
        Assert.Null(game.UserRatingCount);
        Assert.Null(game.CriticRating);
        Assert.Null(game.CriticRatingCount);
        Assert.Empty(game.ScreenshotImageIds);
        Assert.Empty(game.ArtworkImageIds);
    }

    [Fact]
    public async Task An_image_id_that_is_not_one_is_dropped_rather_than_stored()
    {
        using var host = new IgdbTestHost((request, _) => request.Endpoint switch
        {
            "token" => FakeHttpMessageHandler.Json(
                System.Net.HttpStatusCode.OK, IgdbFixtures.TokenResponse("t")),
            "games" => FakeHttpMessageHandler.Json(
                System.Net.HttpStatusCode.OK,
                """
                [{"id":100440,"name":"Game 100440",
                  "screenshots":[{"id":1,"image_id":"sc1good"},
                                 {"id":2,"image_id":"../../etc/passwd"},
                                 {"id":3,"image_id":""}]}]
                """),
            _ => FakeHttpMessageHandler.Json(System.Net.HttpStatusCode.NotFound, "[]"),
        });

        var game = Assert.Single(await host.Client.GetGamesAsync([100_440]));

        Assert.Equal(["sc1good"], game.ScreenshotImageIds);
    }

    [Fact]
    public async Task A_version_three_payload_is_refetched_rather_than_served_without_the_new_fields()
    {
        var cache = new InMemoryMetadataCache();
        await cache.SetAsync(
            IgdbClient.CacheProvider,
            IgdbClient.GameCacheKey(100_440),
            VersionThreeEnvelope,
            DateTime.UtcNow);

        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder(), cache: cache);

        var game = Assert.Single(await host.Client.GetGamesAsync([100_440]));

        Assert.Equal(1, host.Handler.CountFor("games"));
        Assert.NotEmpty(game.ScreenshotImageIds);

        var stored = await cache.GetAsync(IgdbClient.CacheProvider, IgdbClient.GameCacheKey(100_440));
        Assert.NotNull(stored);
        using var document = JsonDocument.Parse(stored.Value.PayloadJson!);
        Assert.Equal(4, document.RootElement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task A_version_three_payload_is_still_served_when_no_refetch_is_possible()
    {
        var cache = new InMemoryMetadataCache();
        await cache.SetAsync(
            IgdbClient.CacheProvider,
            IgdbClient.GameCacheKey(100_440),
            VersionThreeEnvelope,
            DateTime.UtcNow);

        using var host = new IgdbTestHost(
            IgdbTestHost.DefaultResponder(), clientId: null, clientSecret: null, cache: cache);

        var game = Assert.Single(await host.Client.GetGamesAsync([100_440]));

        Assert.Equal("Game 100440", game.Name);
        Assert.Empty(game.ScreenshotImageIds);
        Assert.Null(game.UserRating);
    }

    /// <summary>
    /// The cost constraint. 967 games at 400 ids per Apicalypse body
    /// (IgdbOptions.BatchSize) is three requests, the same three the
    /// pre-bump query cost. Adding six fields to the <c>fields</c> clause
    /// does not add a request, because a <c>fields</c> clause is one
    /// request whatever it lists. The test also measures the per-game
    /// cache weight so the growth from the new fields is visible in the
    /// output.
    /// </summary>
    [Fact]
    public async Task A_library_sized_refetch_after_the_bump_still_costs_three_requests()
    {
        const int libraryGames = 967;

        var cache = new InMemoryMetadataCache();
        var ids = Enumerable.Range(1, libraryGames).Select(i => (long)(100_000 + i)).ToArray();

        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder(), cache: cache);

        var stopwatch = Stopwatch.StartNew();
        var games = await host.Client.GetGamesAsync(ids);
        stopwatch.Stop();

        Assert.Equal(libraryGames, games.Count);

        // 400 ids per Apicalypse request (IgdbOptions.BatchSize), so the whole
        // library is three bodies — the same three the pre-bump query cost.
        // A `fields` clause is one request whatever it lists.
        Assert.Equal(3, host.Handler.CountFor("games"));

        var payloadBytes = 0L;
        foreach (var id in ids)
        {
            var entry = await cache.GetAsync(IgdbClient.CacheProvider, IgdbClient.GameCacheKey(id));
            payloadBytes += Encoding.UTF8.GetByteCount(entry!.Value.PayloadJson!);
        }

        var withoutMedia = new InMemoryMetadataCache();
        using var bare = new IgdbTestHost(BareResponder, cache: withoutMedia);
        await bare.Client.GetGamesAsync(ids);

        var bareBytes = 0L;
        foreach (var id in ids)
        {
            var entry = await withoutMedia.GetAsync(
                IgdbClient.CacheProvider, IgdbClient.GameCacheKey(id));
            bareBytes += Encoding.UTF8.GetByteCount(entry!.Value.PayloadJson!);
        }

        _output.WriteLine(
            $"payload v{IgdbClient.GamePayloadVersion}: {libraryGames} games, "
            + $"{host.Handler.CountFor("games")} requests, {stopwatch.ElapsedMilliseconds} ms local, "
            + $"{payloadBytes} cached bytes ({payloadBytes / libraryGames} per game); "
            + $"same games with the media and reception fields absent: {bareBytes} bytes "
            + $"({bareBytes / libraryGames} per game)");
    }

    /// <summary>
    /// Answers the shared query with every field it asked for EXCEPT the six
    /// this bump added, which is what a version-3 response looked like.
    /// </summary>
    private static HttpResponseMessage BareResponder(RecordedRequest request, int _)
        => request.Endpoint switch
        {
            "token" => FakeHttpMessageHandler.Json(
                System.Net.HttpStatusCode.OK, IgdbFixtures.TokenResponse("t")),
            "games" => FakeHttpMessageHandler.Json(
                System.Net.HttpStatusCode.OK, StripMediaAndReception(IgdbFixtures.Games(request.Body))),
            _ => FakeHttpMessageHandler.Json(System.Net.HttpStatusCode.NotFound, "[]"),
        };

    private static string StripMediaAndReception(string body)
    {
        using var document = JsonDocument.Parse(body);
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var row in document.RootElement.EnumerateArray())
            {
                writer.WriteStartObject();
                foreach (var property in row.EnumerateObject())
                {
                    if (property.NameEquals("screenshots")
                        || property.NameEquals("artworks")
                        || property.NameEquals("rating")
                        || property.NameEquals("rating_count")
                        || property.NameEquals("aggregated_rating")
                        || property.NameEquals("aggregated_rating_count"))
                    {
                        continue;
                    }

                    property.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
