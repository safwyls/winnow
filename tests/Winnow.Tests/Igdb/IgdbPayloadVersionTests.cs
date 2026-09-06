using System.Net;
using System.Text.Json.Nodes;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Storage;
using Xunit;

namespace Winnow.Tests.Igdb;

public sealed class IgdbPayloadVersionTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public async Task Version_bump_refetches_hits_and_misses_in_each_namespace(int kind, bool miss)
    {
        using var host = new IgdbTestHost((request, prior) => miss && request.Endpoint != "token"
            ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]")
            : IgdbTestHost.DefaultResponder()(request, prior));
        await Fetch(host, kind);
        var entry = (await host.Cache.GetAsync("igdb", Key(kind)))!.Value;
        var payload = JsonNode.Parse(entry.PayloadJson!)!;
        var version = payload["version"]!.GetValue<int>();
        await Fetch(host, kind);
        Assert.Equal(1, host.Handler.CountFor(Endpoint(kind)));

        payload["version"] = version - 1;
        await host.Cache.SetAsync("igdb", Key(kind), payload.ToJsonString(), entry.FetchedAt);
        await Fetch(host, kind);
        Assert.Equal(2, host.Handler.CountFor(Endpoint(kind)));
        var refreshed = (await host.Cache.GetAsync("igdb", Key(kind)))!.Value;
        Assert.Equal(version, JsonNode.Parse(refreshed.PayloadJson!)!["version"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Legacy_unversioned_misses_are_rechecked(int kind)
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());
        await host.Cache.SetAsync("igdb", Key(kind), null, host.Clock.GetUtcNow().UtcDateTime);
        await Fetch(host, kind);
        Assert.Equal(1, host.Handler.CountFor(Endpoint(kind)));
        Assert.NotNull((await host.Cache.GetAsync("igdb", Key(kind)))!.Value.PayloadJson);
    }

    [Fact]
    public async Task Unversioned_external_mapping_is_refetched_but_survives_offline()
    {
        var cache = new InMemoryMetadataCache();
        const string legacy = """{"igdb_id":100440,"name":"Old title"}""";
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await cache.SetAsync("igdb", Key(0), legacy, now);
        using (var offline = new IgdbTestHost(IgdbTestHost.DefaultResponder(), clientId: null, clientSecret: null, cache: cache))
        {
            Assert.Equal("Old title", (await offline.Client.ResolveBySteamAppIdsAsync(["440"]))["440"].Name);
            Assert.Equal(0, offline.Handler.CountFor("external_games"));
        }
        using var online = new IgdbTestHost(IgdbTestHost.DefaultResponder(), cache: cache);
        Assert.NotEqual("Old title", (await online.Client.ResolveBySteamAppIdsAsync(["440"]))["440"].Name);
        Assert.Equal(1, online.Handler.CountFor("external_games"));
    }

    [Fact]
    public async Task Successful_refetch_miss_replaces_an_outdated_positive_game()
    {
        using var host = new IgdbTestHost((request, prior) => request.Endpoint == "games"
            ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, "[]")
            : IgdbTestHost.DefaultResponder()(request, prior));
        await host.Cache.SetAsync("igdb", Key(1),
            """{"version":0,"game":{"igdb_id":100440,"name":"Old game"}}""",
            host.Clock.GetUtcNow().UtcDateTime);
        Assert.Empty(await host.Client.GetGamesAsync([100440]));
        Assert.Empty(await host.Client.GetGamesAsync([100440]));
        Assert.Equal(1, host.Handler.CountFor("games"));
    }

    private static string Key(int kind) => kind switch
    {
        0 => IgdbClient.SteamAppCacheKey("440"),
        1 => IgdbClient.GameCacheKey(100440),
        2 => IgdbClient.AgeRatingsCacheKey(100440),
        _ => IgdbClient.SearchCacheKey("Prey", 10),
    };

    private static string Endpoint(int kind) => kind == 0 ? "external_games" : "games";

    private static async Task Fetch(IgdbTestHost host, int kind)
    {
        switch (kind)
        {
            case 0: await host.Client.ResolveBySteamAppIdsAsync(["440"]); break;
            case 1: await host.Client.GetGamesAsync([100440]); break;
            case 2: await host.Client.GetAgeRatingsAsync([100440]); break;
            default: await host.Client.SearchGamesAsync("Prey", 10); break;
        }
    }
}
