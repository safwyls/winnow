using System.Net;
using Winnow.Enrich.Igdb;
using Xunit;

namespace Winnow.Tests.Igdb;

public sealed class IgdbLifecycleClientTests
{
    [Fact]
    public async Task Expanded_status_modes_and_original_cache_time_are_preserved()
    {
        using var host = new IgdbTestHost((r, n) => r.Endpoint == "games"
            ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, """[{"id":42,"game_status":{"id":6,"status":"Offline"},"game_modes":[{"name":"Single player"},{"name":"Multiplayer"}]}]""")
            : IgdbTestHost.DefaultResponder()(r, n));
        var client = host.Resolve<IIgdbLifecycleClient>();
        var first = await client.GetAsync(42);
        Assert.NotNull(first);
        Assert.Equal("offline", first.Signals.IgdbStatus);
        Assert.True(first.Signals.IsMultiplayer);
        Assert.True(first.Signals.HasSinglePlayer);
        host.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(first.ObservedAt, (await client.GetAsync(42))!.ObservedAt);
        Assert.Equal(1, host.Handler.CountFor("games"));
        Assert.Contains("game_status.status", host.Handler.Requests.Single(r => r.Endpoint == "games").Body);
    }

    [Fact]
    public async Task Failed_refresh_does_not_relabel_stale_evidence()
    {
        var fail = false;
        using var host = new IgdbTestHost((r, n) => r.Endpoint == "games"
            ? FakeHttpMessageHandler.Json(fail ? HttpStatusCode.BadRequest : HttpStatusCode.OK, """[{"id":42,"game_status":{"status":"Cancelled"}}]""")
            : IgdbTestHost.DefaultResponder()(r, n));
        var client = host.Resolve<IIgdbLifecycleClient>();
        Assert.NotNull(await client.GetAsync(42));
        host.Clock.Advance(TimeSpan.FromDays(8));
        fail = true;
        Assert.Null(await client.GetAsync(42));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("[{\"id\":99,\"game_status\":{\"status\":\"Offline\"}}]")]
    public async Task Missing_malformed_or_wrong_identity_is_unknown(string body)
    {
        using var host = new IgdbTestHost((r, n) => r.Endpoint == "games"
            ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, body) : IgdbTestHost.DefaultResponder()(r, n));
        Assert.Null(await host.Resolve<IIgdbLifecycleClient>().GetAsync(42));
    }

    [Fact]
    public async Task Partially_expanded_modes_do_not_assert_multiplayer_only()
    {
        using var host = new IgdbTestHost((r, n) => r.Endpoint == "games"
            ? FakeHttpMessageHandler.Json(HttpStatusCode.OK, """[{"id":42,"game_modes":[{"name":"Multiplayer"},1]}]""")
            : IgdbTestHost.DefaultResponder()(r, n));
        var result = await host.Resolve<IIgdbLifecycleClient>().GetAsync(42);
        Assert.NotNull(result);
        Assert.Null(result.Signals.HasSinglePlayer);
        Assert.Null(result.Signals.IsMultiplayer);
    }
}
