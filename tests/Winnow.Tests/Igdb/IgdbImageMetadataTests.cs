using System.Net;
using System.Text.Json.Nodes;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Storage;
using Xunit;

namespace Winnow.Tests.Igdb;

public sealed class IgdbImageMetadataTests
{
    private const string Response = """
        [{"id":100440,"name":"Canned game",
          "screenshots":[{"image_id":"sc1","width":1920,"height":1080,"alpha_channel":false,"animated":false},
                         {"image_id":"sc2","width":0,"height":-1}],
          "artworks":[{"image_id":"ar1","width":3840,"height":2160,"alpha_channel":true,"animated":true,
                       "image_type":{"id":1,"name":" Promotional "}},
                      {"image_id":"ar2","image_type":2},{"image_id":"../invalid"}]}]
        """;

    [Fact]
    public async Task Image_metadata_projects_and_survives_a_warm_cache_read()
    {
        using var host = new IgdbTestHost(Responder);
        var game = Assert.Single(await host.Client.GetGamesAsync([100440]));
        Assert.Equal(new[] { "sc1", "sc2" }, game.ScreenshotImageIds);
        Assert.Equal(new[] { "ar1", "ar2" }, game.ArtworkImageIds);
        Assert.Equal(1920, game.ScreenshotImages[0].Width);
        Assert.Equal(1080, game.ScreenshotImages[0].Height);
        Assert.False(game.ScreenshotImages[0].AlphaChannel);
        Assert.False(game.ScreenshotImages[0].Animated);
        Assert.Null(game.ScreenshotImages[1].Width);
        Assert.Null(game.ScreenshotImages[1].Height);
        Assert.Null(game.ScreenshotImages[1].AlphaChannel);
        Assert.Equal("Promotional", game.ArtworkImages[0].ImageType);
        Assert.True(game.ArtworkImages[0].AlphaChannel);
        Assert.True(game.ArtworkImages[0].Animated);
        Assert.Null(game.ArtworkImages[1].ImageType);
        var cached = Assert.Single(await host.Client.GetGamesAsync([100440]));
        Assert.Equal(game.ScreenshotImages, cached.ScreenshotImages);
        Assert.Equal(game.ArtworkImages, cached.ArtworkImages);
        Assert.Equal(1, host.Handler.CountFor("games"));
    }

    [Fact]
    public async Task Version_four_images_survive_offline_then_refetch_metadata_when_configured()
    {
        var cache = new InMemoryMetadataCache();
        const string old = """{"version":4,"game":{"igdb_id":100440,"name":"Canned game","screenshot_image_ids":["sc1"],"artwork_image_ids":["ar1"]}}""";
        await cache.SetAsync("igdb", IgdbClient.GameCacheKey(100440), old, DateTime.UtcNow);
        using (var offline = new IgdbTestHost(Responder, clientId: null, clientSecret: null, cache: cache))
        {
            var game = Assert.Single(await offline.Client.GetGamesAsync([100440]));
            Assert.Equal(new[] { "ar1" }, game.ArtworkImageIds);
            Assert.Empty(game.ArtworkImages);
            Assert.Equal(0, offline.Handler.CountFor("games"));
        }
        using var online = new IgdbTestHost(Responder, cache: cache);
        Assert.NotEmpty(Assert.Single(await online.Client.GetGamesAsync([100440])).ArtworkImages);
        var refreshed = (await cache.GetAsync("igdb", IgdbClient.GameCacheKey(100440)))!.Value;
        Assert.Equal(5, JsonNode.Parse(refreshed.PayloadJson!)!["version"]!.GetValue<int>());
    }

    [Fact]
    public void Shared_query_requests_current_quality_fields_without_widening_search()
    {
        var query = Apicalypse.Games([100440], 500, 0);
        foreach (var kind in new[] { "screenshots", "artworks" })
            foreach (var field in new[] { "image_id", "width", "height", "alpha_channel", "animated" })
                Assert.Contains($"{kind}.{field}", query, StringComparison.Ordinal);
        Assert.Contains("artworks.image_type.name", query, StringComparison.Ordinal);
        Assert.DoesNotContain("artwork_type", query, StringComparison.Ordinal);
        Assert.DoesNotContain("artworks", Apicalypse.SearchGames("game", 20), StringComparison.Ordinal);
    }

    private static HttpResponseMessage Responder(RecordedRequest request, int prior)
        => request.Endpoint == "games"
            ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, Response)
            : IgdbTestHost.DefaultResponder()(request, prior);
}
