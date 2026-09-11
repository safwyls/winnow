using System.Net;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Model;
using Xunit;

namespace Winnow.Tests.SteamWeb;

public sealed class SteamInventoryCompletenessTests
{
    [Theory]
    [InlineData("{\"response\":{\"game_count\":0}}", true)]
    [InlineData("{\"response\":{\"game_count\":1,\"games\":[{\"appid\":1}]}}", true)]
    [InlineData("{\"response\":{\"game_count\":\"1\",\"games\":[{\"appid\":\"1\"}]}}", true)]
    [InlineData("{\"response\":{\"games\":[{\"appid\":1}]}}", false)]
    [InlineData("{\"response\":{\"game_count\":2,\"games\":[{\"appid\":1}]}}", false)]
    [InlineData("{\"response\":{\"game_count\":1,\"games\":[{\"appid\":1},{}]}}", false)]
    [InlineData("{\"response\":{\"game_count\":2,\"games\":[{\"appid\":1},{\"appid\":1}]}}", false)]
    [InlineData("{\"response\":{\"game_count\":1.5,\"games\":[{\"appid\":1}]}}", false)]
    [InlineData("{\"response\":{\"game_count\":1,\"games\":[{\"appid\":1.5}]}}", false)]
    [InlineData("{\"response\":{\"game_count\":0,\"games\":{}}}", false)]
    public void A_usable_positive_response_does_not_necessarily_describe_a_complete_inventory(string body, bool complete)
    {
        var games = SteamWebJson.TryReadOwnedGames(body);
        Assert.NotNull(games);
        Assert.Equal(complete, SteamWebJson.IsCompleteOwnedGames(body, games));
    }

    [Fact]
    public async Task Fresh_cache_keeps_completeness_and_original_time_but_stale_fallback_cannot_establish_absence()
    {
        var fail = false;
        using var host = new SteamWebTestHost((_, _) => fail
            ? FakeSteamWebHandler.Json(HttpStatusCode.OK, SteamWebFixtures.UndisclosedProfile)
            : FakeSteamWebHandler.Json(HttpStatusCode.OK, "{\"response\":{\"game_count\":1,\"games\":[{\"appid\":1}]}}"));
        var id = SteamId.FromAccountId(123)!.Value;
        var live = await host.Client.GetOwnedGamesAsync(id);
        Assert.True(live.IsComplete);
        var cached = await host.Client.GetOwnedGamesAsync(id);
        Assert.True(cached.FromCache);
        Assert.True(cached.IsComplete);
        Assert.Equal(live.ObservedAt, cached.ObservedAt);
        fail = true;
        var stale = await host.Client.GetOwnedGamesAsync(id, cacheTtl: TimeSpan.Zero);
        Assert.True(stale.Succeeded);
        Assert.True(stale.FromCache);
        Assert.False(stale.IsComplete);
        Assert.Single(stale.Games);
        Assert.Equal(live.ObservedAt, stale.ObservedAt);
    }
}
