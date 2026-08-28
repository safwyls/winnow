using System.Net;
using System.Text.Json;
using Winnow.Enrich.GamesDb;
using Winnow.Enrich.GamesDb.Http;
using Winnow.Enrich.GamesDb.Model;
using Xunit;

namespace Winnow.Tests.GamesDb;

/// <summary>
/// The cross-store identity graph, against canned responses only.
///
/// <para>Three properties carry the weight here, and all three are about
/// telling apart two things that look identical from the call site.</para>
/// <list type="number">
///   <item><b>404 is an answer, 503 is not.</b> "No release under this id" is a
///     durable fact about a game and is cached for 90 days; an outage is not,
///     and caching one would blank a library's worth of tiles for a quarter on
///     the strength of one bad afternoon.</item>
///   <item><b>A warm cache costs no requests.</b> This is an undocumented
///     service with no published limit, so the only defensible posture is to
///     ask each id once.</item>
///   <item><b>Nothing throws.</b> §5.1: enrichment degrades, it does not break
///     the caller.</item>
/// </list>
/// </summary>
public sealed class GamesDbClientTests
{
    [Fact]
    public async Task Resolves_epic_appname_to_the_steam_appid()
    {
        using var host = new GamesDbTestHost(GamesDbTestHost.FezResponder());

        var game = await host.Client.ResolveAsync(GamesDbPlatforms.Epic, GamesDbFixtures.FezAppName);

        Assert.NotNull(game);
        Assert.Equal("51152861476431582", game.GameId);

        // The whole reason this module exists: an Epic codename in, a Steam
        // appid out, with no title comparison anywhere in between.
        Assert.Equal(GamesDbFixtures.FezSteamAppId, game.IdOn(GamesDbPlatforms.Steam));
        Assert.Equal(GamesDbFixtures.FezGogId, game.IdOn(GamesDbPlatforms.Gog));
    }

    [Fact]
    public async Task Requests_the_documented_path_with_a_descriptive_user_agent()
    {
        using var host = new GamesDbTestHost(GamesDbTestHost.FezResponder());

        await host.Client.ResolveAsync(GamesDbPlatforms.Epic, GamesDbFixtures.FezAppName);

        var request = Assert.Single(host.Handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "/platforms/epic/external_releases/Bluebird",
            request.Uri.AbsolutePath);

        // §4.3's rule, applied to every unofficial endpoint this project touches:
        // identify yourself so the operator can attribute — and block — the
        // traffic by name rather than by IP.
        Assert.Contains("Winnow", request.UserAgent);
    }

    [Fact]
    public async Task A_platform_with_no_release_is_null_not_an_empty_string()
    {
        using var host = new GamesDbTestHost(GamesDbTestHost.FezResponder());

        var game = await host.Client.ResolveAsync(GamesDbPlatforms.Epic, GamesDbFixtures.FezAppName);

        // "This game is not on Xbox" is a fact about the game. The caller must
        // be able to read it as "no route this way" rather than as a failure.
        Assert.Null(game!.IdOn("xboxone_that_does_not_exist"));
    }

    [Fact]
    public async Task A_second_lookup_of_the_same_id_costs_no_request()
    {
        using var host = new GamesDbTestHost(GamesDbTestHost.FezResponder());

        await host.Client.ResolveAsync(GamesDbPlatforms.Epic, GamesDbFixtures.FezAppName);
        var again = await host.Client.ResolveAsync(GamesDbPlatforms.Epic, GamesDbFixtures.FezAppName);

        Assert.Single(host.Handler.Requests);
        Assert.Equal(GamesDbFixtures.FezSteamAppId, again!.IdOn(GamesDbPlatforms.Steam));
    }

    [Fact]
    public async Task A_404_is_cached_so_an_unknown_id_is_asked_about_once()
    {
        using var host = new GamesDbTestHost(GamesDbTestHost.FezResponder());

        Assert.Null(await host.Client.ResolveAsync(GamesDbPlatforms.Epic, "Fortnite"));
        Assert.Null(await host.Client.ResolveAsync(GamesDbPlatforms.Epic, "Fortnite"));

        // An Epic exclusive genuinely has no cross-store twin. Re-asking every
        // launch would spend an unpublished endpoint's goodwill to re-learn the
        // same nothing, once per title, forever.
        Assert.Equal(1, host.Handler.CountFor("epic", "Fortnite"));
    }

    [Fact]
    public async Task A_server_failure_is_NOT_cached()
    {
        using var host = new GamesDbTestHost(
            (_, _) => FakeGamesDbHandler.ServiceUnavailable(),
            options => options.MaxRetryAttempts = 1);

        Assert.Null(await host.Client.ResolveAsync(GamesDbPlatforms.Epic, GamesDbFixtures.FezAppName));

        var entry = await host.Cache.GetAsync(
            GamesDbClient.CacheKey(GamesDbPlatforms.Epic, GamesDbFixtures.FezAppName));

        // The distinction this whole class is about. A cached miss here would
        // record "Fez has no cross-store twin" for 90 days because gamesdb had
        // a bad minute — and the library would keep 67 blank Epic tiles that no
        // future run would revisit.
        Assert.Null(entry);
    }

    [Fact]
    public async Task A_failure_is_retried_on_the_next_run_and_then_answers()
    {
        // Fails twice, so the one in-call retry is exhausted and the first call
        // really does come back empty-handed. What matters is what happens
        // NEXT: nothing was cached, so the second call asks again.
        using var host = new GamesDbTestHost((request, prior) =>
            prior < 2
                ? FakeGamesDbHandler.ServiceUnavailable()
                : GamesDbTestHost.FezResponder()(request, prior),
            options => options.MaxRetryAttempts = 1);

        Assert.Null(await host.Client.ResolveAsync(GamesDbPlatforms.Epic, GamesDbFixtures.FezAppName));

        var second = await host.Client.ResolveAsync(GamesDbPlatforms.Epic, GamesDbFixtures.FezAppName);
        Assert.Equal(GamesDbFixtures.FezSteamAppId, second!.IdOn(GamesDbPlatforms.Steam));
    }

    [Fact]
    public async Task A_transient_failure_is_retried_inside_one_call()
    {
        using var host = new GamesDbTestHost((request, prior) =>
            prior == 0
                ? FakeGamesDbHandler.ServiceUnavailable()
                : GamesDbTestHost.FezResponder()(request, prior));

        var game = await host.Client.ResolveAsync(GamesDbPlatforms.Epic, GamesDbFixtures.FezAppName);

        Assert.NotNull(game);
        Assert.Equal(2, host.Handler.CountFor("epic", GamesDbFixtures.FezAppName));
    }

    [Fact]
    public async Task A_404_is_never_retried()
    {
        using var host = new GamesDbTestHost((_, _) => FakeGamesDbHandler.NotFound());

        await host.Client.ResolveAsync(GamesDbPlatforms.Epic, "Fortnite");

        // Four requests (one plus three retries) per permanently-absent title,
        // with a growing backoff between them, on a volunteer-shaped endpoint,
        // to re-learn a fact that cannot change by asking again.
        Assert.Equal(1, host.Handler.CountFor("epic", "Fortnite"));
    }

    [Fact]
    public async Task Retries_a_429_and_honours_Retry_After()
    {
        using var host = new GamesDbTestHost((request, prior) =>
        {
            if (prior > 0)
            {
                return GamesDbTestHost.FezResponder()(request, prior);
            }

            var response = FakeGamesDbHandler.Json(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.TryAddWithoutValidation("Retry-After", "0");
            return response;
        });

        var game = await host.Client.ResolveAsync(GamesDbPlatforms.Epic, GamesDbFixtures.FezAppName);

        Assert.NotNull(game);
        Assert.Equal(2, host.Handler.CountFor("epic", GamesDbFixtures.FezAppName));
    }

    [Fact]
    public async Task An_expired_entry_is_refetched()
    {
        using var host = new GamesDbTestHost(GamesDbTestHost.FezResponder());

        await host.Client.ResolveAsync(GamesDbPlatforms.Epic, GamesDbFixtures.FezAppName);
        host.Clock.Advance(TimeSpan.FromDays(91));
        await host.Client.ResolveAsync(GamesDbPlatforms.Epic, GamesDbFixtures.FezAppName);

        Assert.Equal(2, host.Handler.CountFor("epic", GamesDbFixtures.FezAppName));
    }

    [Fact]
    public async Task A_body_that_is_not_the_expected_shape_is_a_failure_not_an_absence()
    {
        using var host = new GamesDbTestHost(
            (_, _) => FakeGamesDbHandler.Json(HttpStatusCode.OK, """{"unexpected":true}"""),
            options => options.MaxRetryAttempts = 1);

        Assert.Null(await host.Client.ResolveAsync(GamesDbPlatforms.Epic, GamesDbFixtures.FezAppName));

        // A service that has been reshaped should cost one wasted request per
        // run until someone notices — not a silent quarter of blank tiles.
        Assert.Null(await host.Cache.GetAsync(
            GamesDbClient.CacheKey(GamesDbPlatforms.Epic, GamesDbFixtures.FezAppName)));
    }

    [Fact]
    public async Task Malformed_json_does_not_throw()
    {
        using var host = new GamesDbTestHost(
            (_, _) => FakeGamesDbHandler.Json(HttpStatusCode.OK, "not json at all"),
            options => options.MaxRetryAttempts = 1);

        // §5.1: enrichment degrades, it never breaks a caller.
        Assert.Null(await host.Client.ResolveAsync(GamesDbPlatforms.Epic, GamesDbFixtures.FezAppName));
    }

    [Fact]
    public async Task Blank_arguments_never_reach_the_wire()
    {
        using var host = new GamesDbTestHost(GamesDbTestHost.FezResponder());

        Assert.Null(await host.Client.ResolveAsync(GamesDbPlatforms.Epic, "   "));
        Assert.Null(await host.Client.ResolveAsync("", "Bluebird"));

        Assert.Empty(host.Handler.Requests);
    }

    [Fact]
    public async Task The_id_is_escaped_into_the_path()
    {
        using var host = new GamesDbTestHost((_, _) => FakeGamesDbHandler.NotFound());

        await host.Client.ResolveAsync(GamesDbPlatforms.Epic, "a/../b");

        // AppName is an opaque string read off disk. Every observed value has
        // been alphanumeric, which is exactly the condition under which nobody
        // escapes anything until the day it matters.
        // The id lands as ONE path segment, percent-encoded, so no amount of
        // slashes or dot-dots inside it can climb out of external_releases/.
        var request = Assert.Single(host.Handler.Requests);
        Assert.Equal("/platforms/epic/external_releases/a%2F..%2Fb", request.Uri.AbsolutePath);
        Assert.Equal("a%2F..%2Fb", request.Uri.Segments[^1]);
    }

    [Fact]
    public void The_rate_limiter_is_a_singleton_shared_by_every_request()
    {
        using var host = new GamesDbTestHost(GamesDbTestHost.FezResponder());

        // A per-client limiter would multiply the ceiling by the number of
        // clients, which is the shape of accident that gets an unpublished
        // endpoint closed. Charter: rate limiting lives on the pipeline, never
        // at a call site.
        Assert.Same(host.Resolve<GamesDbRateLimiter>(), host.Resolve<GamesDbRateLimiter>());
    }

    /// <summary>
    /// Pins the field names the client reads against the captured body. This
    /// service is unversioned, so a shape change is silent by construction —
    /// this is the alarm.
    /// </summary>
    [Fact]
    public void The_captured_response_still_carries_the_fields_the_client_reads()
    {
        using var document = JsonDocument.Parse(GamesDbFixtures.EpicBluebird());
        var root = document.RootElement;

        Assert.Equal("51152861476431582", root.GetProperty("game_id").GetString());

        var releases = root.GetProperty("game").GetProperty("releases");
        var steam = releases.EnumerateArray()
            .Where(r => r.GetProperty("platform_id").GetString() == "steam")
            .Select(r => r.GetProperty("external_id"))
            .ToArray();

        // external_id is a STRING even for a Steam appid. A client that bound it
        // to a number would work until the first psn id.
        Assert.All(steam, id => Assert.Equal(JsonValueKind.String, id.ValueKind));

        // TWO steam rows for one game, and the second is junk: `steam_224760`,
        // the release key pasted into the id field. This is pinned rather than
        // cleaned out of the fixture because it is why the planner filters on
        // shape instead of taking the graph's first answer.
        Assert.Equal(
            [GamesDbFixtures.FezSteamAppId, "steam_" + GamesDbFixtures.FezSteamAppId],
            steam.Select(id => id.GetString() ?? string.Empty).ToArray());
    }
}
