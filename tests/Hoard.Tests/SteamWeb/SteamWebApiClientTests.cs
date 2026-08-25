using System.Net;
using Hoard.Core.Domain;
using Hoard.Enrich.SteamWeb;
using Hoard.Enrich.SteamWeb.Credentials;
using Hoard.Enrich.SteamWeb.Http;
using Hoard.Enrich.SteamWeb.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Fixture = Hoard.Tests.SteamWeb.SteamWebFixtures.OwnedGameFixture;

namespace Hoard.Tests.SteamWeb;

/// <summary>
/// <c>IPlayerService/GetOwnedGames</c> (§4.2). Every response here is canned;
/// nothing in this file opens a socket.
/// </summary>
public class SteamWebApiClientTests
{
    private static readonly SteamId Account = SteamId.FromAccountId(12345678)!.Value;

    // ── The §4.2 request contract ────────────────────────────────────────────

    /// <summary>
    /// §4.2 is emphatic about three parameters and one of them is a trap:
    /// without <c>skip_unvetted_apps=false</c>, apps flagged "Profile Features
    /// Limited" are silently omitted. Measured live on 2026-08-24 against a real
    /// account: 841 titles with the flag, 834 without. Nothing in the response
    /// says anything is missing, so the only defence is asserting the request.
    /// </summary>
    [Fact]
    public async Task The_three_parameters_section_4_2_requires_are_actually_on_the_request()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder());

        await host.Client.GetOwnedGamesAsync(Account);

        var request = Assert.Single(host.Handler.Requests);
        Assert.Equal("1", request.Parameter("include_appinfo"));
        Assert.Equal("1", request.Parameter("include_played_free_games"));
        Assert.Equal("false", request.Parameter("skip_unvetted_apps"));
    }

    [Fact]
    public async Task The_request_is_a_get_to_the_owned_games_endpoint_for_the_right_account()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder());

        await host.Client.GetOwnedGamesAsync(Account);

        var request = Assert.Single(host.Handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(SteamWebTestHost.GetOwnedGames, request.Endpoint);

        // The SteamID64, not the steam3 folder name the local scan enumerates.
        Assert.Equal("76561197972611406", request.Parameter("steamid"));
        Assert.Equal("test-api-key", request.Parameter("key"));
    }

    [Fact]
    public async Task A_descriptive_user_agent_identifies_the_traffic()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder());

        await host.Client.GetOwnedGamesAsync(Account);

        var request = Assert.Single(host.Handler.Requests);
        Assert.Contains("Hoard", request.UserAgent ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_whole_library_costs_exactly_one_request()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder());

        var library = await host.Client.GetOwnedGamesAsync(Account);

        Assert.Equal(SteamWebFixtures.CapturedAppIds.Length, library.Games.Count);
        Assert.Single(host.Handler.Requests);
    }

    // ── Not configured ───────────────────────────────────────────────────────

    /// <summary>
    /// §5.1: enrichment must never block or break a path. No key means the
    /// module declines — no request, no throw, and the app works exactly as it
    /// does today.
    /// </summary>
    [Fact]
    public async Task Unconfigured_is_a_silent_no_op()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder(), apiKey: null);

        Assert.False(await host.Client.IsConfiguredAsync());

        var library = await host.Client.GetOwnedGamesAsync(Account);

        Assert.False(library.Succeeded);
        Assert.Empty(library.Games);
        Assert.Empty(host.Handler.Requests);
        Assert.Empty(await host.Client.GetOwnershipCandidatesAsync(Account));

        // And nothing was written down: an unconfigured module must not leave a
        // cached "this account owns nothing" behind for a configured run to hit.
        Assert.Null(await host.Cache.GetAsync(
            SteamWebApiClient.CacheProvider, SteamWebApiClient.OwnedGamesCacheKey(Account)));
    }

    [Fact]
    public async Task A_key_pasted_into_settings_later_is_picked_up_after_an_invalidate()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder(), apiKey: null);

        Assert.False(await host.Client.IsConfiguredAsync());

        await host.Settings.SetAsync(SettingsTableApiKeySource.ApiKeySetting, "late-key");
        host.Resolve<ISteamApiKeyProvider>().Invalidate();

        Assert.True(await host.Client.IsConfiguredAsync());
        Assert.True((await host.Client.GetOwnedGamesAsync(Account)).Succeeded);
    }

    // ── The key never reaches a log ──────────────────────────────────────────

    /// <summary>
    /// The whole reason <see cref="RedactingHttpClientLogger"/> exists.
    /// <c>IHttpClientFactory</c> attaches two loggers by default and both write
    /// the full request URI at Information — and §4.2's <c>GetOwnedGames</c>
    /// carries the key in that URI with no header or body alternative. The
    /// registration strips them; this asserts the strip actually happened, with
    /// the sink turned all the way up to Trace.
    /// </summary>
    [Fact]
    public async Task The_key_never_appears_in_any_logged_output()
    {
        const string secret = "SUPERSECRETKEYVALUE0123456789ABC";
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder(), apiKey: secret);

        await host.Client.GetOwnedGamesAsync(Account);

        Assert.NotEmpty(host.Logs.Lines);
        Assert.DoesNotContain(secret, host.Logs.AllText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task The_key_never_appears_in_a_failure_log_either(HttpStatusCode status)
    {
        const string secret = "SUPERSECRETKEYVALUE0123456789ABC";
        using var host = new SteamWebTestHost(
            (_, _) => FakeSteamWebHandler.Json(status, "{}"),
            apiKey: secret,
            configure: options => options.MaxRetryAttempts = 1);

        await host.Client.GetOwnedGamesAsync(Account);

        Assert.DoesNotContain(secret, host.Logs.AllText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_key_never_appears_in_a_dead_network_log()
    {
        const string secret = "SUPERSECRETKEYVALUE0123456789ABC";
        using var host = new SteamWebTestHost(
            (_, _) => throw new HttpRequestException("no such host"),
            apiKey: secret,
            configure: options => options.MaxRetryAttempts = 1);

        await host.Client.GetOwnedGamesAsync(Account);

        Assert.DoesNotContain(secret, host.Logs.AllText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_redactor_allowlists_rather_than_denylists()
    {
        var described = SteamWebRedaction.Describe(new Uri(
            "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/"
            + "?steamid=76561197972611406&include_appinfo=1&include_played_free_games=1"
            + "&skip_unvetted_apps=false&format=json&key=SECRET&access_token=ALSOSECRET"));

        Assert.DoesNotContain("SECRET", described, StringComparison.Ordinal);

        // access_token was never on any denylist; it is redacted because
        // anything not explicitly allowed is.
        Assert.DoesNotContain("ALSOSECRET", described, StringComparison.Ordinal);

        // The §4.2 flags survive, which is the point: a log has to be able to
        // confirm they were sent.
        Assert.Contains("skip_unvetted_apps=false", described, StringComparison.Ordinal);
        Assert.Contains("include_appinfo=1", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// A regression guard with teeth: if someone drops <c>RemoveAllLoggers()</c>
    /// from the registration, the framework's own
    /// <c>"Sending HTTP request {HttpMethod} {Uri}"</c> line comes back — and
    /// takes the key with it.
    /// </summary>
    [Fact]
    public async Task The_frameworks_own_uri_printing_logger_is_gone()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder());

        await host.Client.GetOwnedGamesAsync(Account);

        Assert.DoesNotContain("Sending HTTP request", host.Logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "System.Net.Http.HttpClient", host.Logs.AllText, StringComparison.Ordinal);

        // Replaced, not merely deleted: diagnosing a throttle still needs to see
        // that a request happened and what came back.
        Assert.Contains("Steam Web API", host.Logs.AllText, StringComparison.Ordinal);
    }

    // ── Caching ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_cache_hit_issues_zero_requests()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder());

        var first = await host.Client.GetOwnedGamesAsync(Account);
        var second = await host.Client.GetOwnedGamesAsync(Account);

        Assert.Single(host.Handler.Requests);
        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(first.Games, second.Games);
    }

    [Fact]
    public async Task The_cache_holds_the_response_body_verbatim()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder());

        await host.Client.GetOwnedGamesAsync(Account);

        var entry = await host.Cache.GetAsync(
            SteamWebApiClient.CacheProvider, SteamWebApiClient.OwnedGamesCacheKey(Account));

        Assert.NotNull(entry);

        // Verbatim, so the per-platform playtime splits and anything else this
        // client does not project stay recoverable without a refetch.
        Assert.Equal(SteamWebFixtures.CapturedResponse(), entry.Value.PayloadJson);
        Assert.Contains("playtime_windows_forever", entry.Value.PayloadJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expired_entry_is_refetched()
    {
        using var host = new SteamWebTestHost(
            SteamWebTestHost.DefaultResponder(), configure: o => o.CacheTtl = TimeSpan.FromHours(6));

        await host.Client.GetOwnedGamesAsync(Account);
        host.Clock.Advance(TimeSpan.FromHours(7));
        await host.Client.GetOwnedGamesAsync(Account);

        Assert.Equal(2, host.Handler.Requests.Count);
    }

    [Fact]
    public async Task A_per_call_ttl_of_zero_forces_a_refetch()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder());

        await host.Client.GetOwnedGamesAsync(Account);
        await host.Client.GetOwnedGamesAsync(Account, cacheTtl: TimeSpan.Zero);

        Assert.Equal(2, host.Handler.Requests.Count);
    }

    // ── Soft failure, and what must never be cached ──────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_failed_request_is_not_cached_as_a_genuine_empty_library(HttpStatusCode status)
    {
        using var host = new SteamWebTestHost(
            (_, _) => FakeSteamWebHandler.Json(status, "{}"),
            configure: options => options.MaxRetryAttempts = 1);

        var library = await host.Client.GetOwnedGamesAsync(Account);

        // No throw: a key revoked tomorrow must not break the app.
        Assert.False(library.Succeeded);
        Assert.Empty(library.Games);

        // Critically, nothing cached. Recording "this account owns nothing" on
        // the strength of one 503 would hold for a whole TTL, and a caller
        // reconciling ownership against it would delete the user's library.
        Assert.Null(await host.Cache.GetAsync(
            SteamWebApiClient.CacheProvider, SteamWebApiClient.OwnedGamesCacheKey(Account)));
    }

    [Fact]
    public async Task A_failed_request_is_retried_on_the_next_pass()
    {
        var fail = true;
        using var host = new SteamWebTestHost(
            (_, _) => fail
                ? FakeSteamWebHandler.Json(HttpStatusCode.Forbidden, "{}")
                : FakeSteamWebHandler.Json(HttpStatusCode.OK, SteamWebFixtures.CapturedResponse()),
            configure: options => options.MaxRetryAttempts = 1);

        Assert.False((await host.Client.GetOwnedGamesAsync(Account)).Succeeded);

        fail = false;
        Assert.True((await host.Client.GetOwnedGamesAsync(Account)).Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"response\":")]
    [InlineData("[]")]
    [InlineData("{\"response\":{\"games\":\"nope\"}}")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    public async Task A_malformed_body_degrades_to_empty_rather_than_throwing(string body)
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.Always(body));

        var library = await host.Client.GetOwnedGamesAsync(Account);

        Assert.False(library.Succeeded);
        Assert.Empty(library.Games);
        Assert.Null(await host.Cache.GetAsync(
            SteamWebApiClient.CacheProvider, SteamWebApiClient.OwnedGamesCacheKey(Account)));
    }

    /// <summary>
    /// Verified live on 2026-08-24: querying a second account on the same machine
    /// returned HTTP 200 with exactly <c>{"response":{}}</c> in 15 bytes. It is
    /// what Steam sends for a profile it will not disclose, and it is
    /// indistinguishable from "owns nothing" — so it is classified as
    /// unanswered, which is the safe reading of the two.
    /// </summary>
    [Fact]
    public async Task The_bare_envelope_is_unanswered_not_an_empty_library()
    {
        using var host = new SteamWebTestHost(
            SteamWebTestHost.Always(SteamWebFixtures.UndisclosedProfile));

        var library = await host.Client.GetOwnedGamesAsync(Account);

        Assert.False(library.Succeeded);
        Assert.Null(await host.Cache.GetAsync(
            SteamWebApiClient.CacheProvider, SteamWebApiClient.OwnedGamesCacheKey(Account)));
    }

    /// <summary>The counterpart: an explicit zero count IS an answer.</summary>
    [Fact]
    public async Task An_explicit_zero_game_count_is_an_answer()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.Always(SteamWebFixtures.EmptyLibrary));

        var library = await host.Client.GetOwnedGamesAsync(Account);

        Assert.True(library.Succeeded);
        Assert.Empty(library.Games);
        Assert.NotNull(await host.Cache.GetAsync(
            SteamWebApiClient.CacheProvider, SteamWebApiClient.OwnedGamesCacheKey(Account)));
    }

    /// <summary>
    /// Ownership does not un-happen, so yesterday's library beats no library when
    /// today's request fails.
    /// </summary>
    [Fact]
    public async Task A_stale_entry_is_served_when_a_refetch_fails()
    {
        var fail = false;
        using var host = new SteamWebTestHost(
            (_, _) => fail
                ? FakeSteamWebHandler.Json(HttpStatusCode.ServiceUnavailable, "{}")
                : FakeSteamWebHandler.Json(HttpStatusCode.OK, SteamWebFixtures.CapturedResponse()),
            configure: options =>
            {
                options.CacheTtl = TimeSpan.FromHours(6);
                options.MaxRetryAttempts = 1;
            });

        await host.Client.GetOwnedGamesAsync(Account);

        fail = true;
        host.Clock.Advance(TimeSpan.FromDays(30));
        var library = await host.Client.GetOwnedGamesAsync(Account);

        Assert.True(library.Succeeded);
        Assert.True(library.FromCache);
        Assert.Equal(SteamWebFixtures.CapturedAppIds.Length, library.Games.Count);
    }

    [Fact]
    public async Task A_dead_network_degrades_to_unanswered()
    {
        using var host = new SteamWebTestHost(
            (_, _) => throw new HttpRequestException("no such host"),
            configure: options => options.MaxRetryAttempts = 1);

        Assert.False((await host.Client.GetOwnedGamesAsync(Account)).Succeeded);
    }

    /// <summary>
    /// The one exception to soft-fail: the caller asking to stop is not an
    /// enrichment failure, and swallowing it into an empty result would make a
    /// cancelled sync look like a completed one.
    /// </summary>
    [Fact]
    public async Task Caller_cancellation_still_propagates()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => host.Client.GetOwnedGamesAsync(Account, ct: cts.Token));
    }

    // ── Rate limiting and retry (§4.2) ───────────────────────────────────────

    [Fact]
    public async Task A_429_is_retried_rather_than_surfaced()
    {
        using var host = new SteamWebTestHost((_, prior) => prior == 0
            ? FakeSteamWebHandler.TooManyRequests(TimeSpan.FromSeconds(1))
            : FakeSteamWebHandler.Json(HttpStatusCode.OK, SteamWebFixtures.CapturedResponse()));

        var library = await host.Client.GetOwnedGamesAsync(Account);

        Assert.True(library.Succeeded);
        Assert.Equal(2, host.Handler.Requests.Count);
    }

    /// <summary>
    /// The header is actually waited on, not ignored in favour of the much
    /// shorter exponential schedule. §4.2 reports Steam sending 60–120 s, and
    /// hammering through that is how a key gets throttled harder.
    /// </summary>
    [Fact]
    public async Task Retry_after_is_honoured_rather_than_the_exponential_schedule()
    {
        using var host = new SteamWebTestHost(
            (_, prior) => prior == 0
                ? FakeSteamWebHandler.TooManyRequests(TimeSpan.FromSeconds(1))
                : FakeSteamWebHandler.Json(HttpStatusCode.OK, SteamWebFixtures.CapturedResponse()),
            configure: options =>
            {
                // Base delay far below the header, cap far above it: only a
                // policy that reads Retry-After waits a whole second here.
                options.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
                options.MaxRetryDelay = TimeSpan.FromSeconds(10);
            });

        var started = DateTime.UtcNow;
        Assert.True((await host.Client.GetOwnedGamesAsync(Account)).Succeeded);

        Assert.True(
            DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(900),
            "the 1s Retry-After was ignored in favour of the 1ms exponential schedule");
    }

    /// <summary>
    /// Honoured but capped: §4.2 reports 60–120 s, and a mistaken or hostile
    /// header must not be able to park a background sync for an hour.
    /// </summary>
    [Fact]
    public async Task An_absurd_retry_after_is_capped()
    {
        using var host = new SteamWebTestHost(
            (_, prior) => prior == 0
                ? FakeSteamWebHandler.TooManyRequests(TimeSpan.FromHours(1))
                : FakeSteamWebHandler.Json(HttpStatusCode.OK, SteamWebFixtures.CapturedResponse()),
            configure: options => options.MaxRetryDelay = TimeSpan.FromMilliseconds(50));

        var started = DateTime.UtcNow;
        Assert.True((await host.Client.GetOwnedGamesAsync(Account)).Succeeded);

        Assert.True(
            DateTime.UtcNow - started < TimeSpan.FromSeconds(30), "the one-hour Retry-After was not capped");
    }

    [Fact]
    public void The_shipped_cap_can_still_honour_the_top_of_section_4_2s_range()
        // §4.2 reports Retry-After values of 60–120 s. A cap below 120 s would
        // quietly retry early and earn a harder throttle.
        => Assert.True(new SteamWebOptions().MaxRetryDelay >= TimeSpan.FromSeconds(120));

    [Fact]
    public async Task Exhausted_retries_still_degrade_rather_than_throw()
    {
        using var host = new SteamWebTestHost(
            (_, _) => FakeSteamWebHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"),
            configure: options => options.MaxRetryAttempts = 2);

        Assert.False((await host.Client.GetOwnedGamesAsync(Account)).Succeeded);

        // The original attempt plus two retries; then it gives up quietly.
        Assert.Equal(3, host.Handler.Requests.Count);
    }

    /// <summary>403 means the key is wrong, not that the server is busy. Retrying it wastes budget.</summary>
    [Fact]
    public async Task A_403_is_not_retried()
    {
        using var host = new SteamWebTestHost((_, _) => FakeSteamWebHandler.Json(HttpStatusCode.Forbidden, "{}"));

        await host.Client.GetOwnedGamesAsync(Account);

        Assert.Single(host.Handler.Requests);
    }

    [Fact]
    public void The_rate_limiter_is_shared_across_the_whole_module()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder());

        // A per-client limiter would multiply the ceiling by the number of clients.
        Assert.Same(host.Resolve<SteamWebRateLimiter>(), host.Resolve<SteamWebRateLimiter>());
    }

    [Fact]
    public async Task The_configured_rate_is_actually_enforced()
    {
        using var host = new SteamWebTestHost(
            SteamWebTestHost.DefaultResponder(), configure: options => options.RequestsPerSecond = 2);
        var limiter = host.Resolve<SteamWebRateLimiter>();

        Assert.Equal(2, limiter.AvailablePermits);
        await host.Client.GetOwnedGamesAsync(Account);
        Assert.Equal(1, limiter.AvailablePermits);
    }

    // ── Projection ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Every_field_an_ingest_source_needs_survives_the_projection()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.Always(SteamWebFixtures.OwnedGames(
            new Fixture(1203620, "Enshrouded", PlaytimeForever: 817, PlaytimeTwoWeeks: 473,
                RtimeLastPlayed: 1786924992, IconHash: "b51ff4aa"))));

        var game = Assert.Single((await host.Client.GetOwnedGamesAsync(Account)).Games);

        Assert.Equal("1203620", game.AppId);
        Assert.Equal("Enshrouded", game.Title);
        Assert.Equal(817, game.PlaytimeForeverMinutes);
        Assert.Equal(473, game.PlaytimeTwoWeeksMinutes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1786924992).UtcDateTime, game.LastPlayedUtc);
        Assert.Equal("b51ff4aa", game.IconHash);
        Assert.False(game.NeverPlayed);
    }

    /// <summary>
    /// The population <c>localconfig.vdf</c> cannot see at all: it records only
    /// games that have playtime, so a never-launched owned game is invisible to
    /// the local scan. Zero must therefore survive as zero, not be discarded as
    /// "no data".
    /// </summary>
    [Fact]
    public async Task A_never_played_owned_game_survives_with_zero_playtime_and_no_last_played()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.Always(SteamWebFixtures.OwnedGames(
            new Fixture(20, "Team Fortress Classic"))));

        var game = Assert.Single((await host.Client.GetOwnedGamesAsync(Account)).Games);

        Assert.Equal(0, game.PlaytimeForeverMinutes);

        // rtime_last_played is present and zero on the wire; zero is the "never"
        // sentinel and must not become 1970-01-01.
        Assert.Null(game.LastPlayedUtc);
        Assert.True(game.NeverPlayed);
    }

    [Fact]
    public async Task Candidates_carry_the_provider_the_account_and_the_provenance()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder());

        var candidates = await host.Client.GetOwnershipCandidatesAsync(Account);

        Assert.Equal(SteamWebFixtures.CapturedAppIds.Length, candidates.Count);
        Assert.All(candidates, c =>
        {
            Assert.Equal(ExternalIdProviders.Steam, c.Provider);
            Assert.Equal(SteamWebApiClient.SourceName, c.Source);

            // The steam3 folder name, so a Web API candidate attributes to the
            // same account as its locally-scanned twin.
            Assert.Equal("12345678", c.AccountRef);

            // The Web API knows nothing about install state or install path.
            Assert.False(c.Installed);
            Assert.Null(c.InstallPath);

            // GetOwnedGames exposes no purchase or licence date in any form.
            Assert.Null(c.AcquiredAt);
        });

        var enshrouded = candidates.Single(c => c.ProviderId == "1203620");
        Assert.Equal("Enshrouded", enshrouded.Title);
        Assert.Equal(817, enshrouded.PlaytimeMinutes);
        Assert.NotNull(enshrouded.LastPlayedAt);
    }

    [Fact]
    public async Task An_unanswered_result_yields_no_candidates_rather_than_an_empty_library()
    {
        using var host = new SteamWebTestHost(
            SteamWebTestHost.Always(SteamWebFixtures.UndisclosedProfile));

        // Empty is safe here only because a candidate feed is additive:
        // contributing nothing is never a claim that the library is empty.
        Assert.Empty(await host.Client.GetOwnershipCandidatesAsync(Account));
        Assert.False((await host.Client.GetOwnedGamesAsync(Account)).Succeeded);
    }

    [Fact]
    public async Task A_blank_name_becomes_a_provisional_null_title_not_an_empty_string()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.Always(SteamWebFixtures.OwnedGames(
            new Fixture(999999, Name: "   "))));

        var candidate = Assert.Single(await host.Client.GetOwnershipCandidatesAsync(Account));

        // §5.1's contract: null Title means "the source knows the app exists but
        // has no title for it", which the resolver names provisionally. An empty
        // string would become a real, blank title.
        Assert.Null(candidate.Title);
    }

    [Fact]
    public async Task An_entry_with_no_usable_appid_is_dropped_rather_than_poisoning_the_batch()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.Always(
            "{\"response\":{\"game_count\":2,\"games\":[{\"name\":\"No appid\"},"
            + "{\"appid\":440,\"name\":\"Team Fortress 2\"}]}}"));

        var library = await host.Client.GetOwnedGamesAsync(Account);

        Assert.True(library.Succeeded);
        var game = Assert.Single(library.Games);
        Assert.Equal("440", game.AppId);
    }

    [Fact]
    public async Task Numbers_encoded_as_strings_are_read_the_same_as_numbers()
    {
        // Valve mixes numeric encodings within one object — the pinned store
        // fixtures already show final_price_in_cents as a string beside a numeric
        // weight — so nothing assumes which form a field arrives in.
        using var host = new SteamWebTestHost(SteamWebTestHost.Always(
            "{\"response\":{\"game_count\":1,\"games\":[{\"appid\":\"440\",\"name\":\"Team Fortress 2\","
            + "\"playtime_forever\":\"120\",\"rtime_last_played\":\"1527216883\"}]}}"));

        var game = Assert.Single((await host.Client.GetOwnedGamesAsync(Account)).Games);

        Assert.Equal("440", game.AppId);
        Assert.Equal(120, game.PlaytimeForeverMinutes);
        Assert.NotNull(game.LastPlayedUtc);
    }

    [Fact]
    public async Task Games_come_back_in_a_deterministic_order()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.Always(SteamWebFixtures.OwnedGames(
            new Fixture(1203620, "Enshrouded"),
            new Fixture(10, "Counter-Strike"),
            new Fixture(440, "Team Fortress 2"))));

        var library = await host.Client.GetOwnedGamesAsync(Account);

        // So a cached payload and a fresh one project identically, and a diff
        // between two runs is a real change rather than reordering.
        Assert.Equal(
            new[] { "10", "440", "1203620" }, library.Games.Select(g => g.AppId).ToArray());
    }

    [Fact]
    public async Task The_library_reports_how_many_entries_carry_a_last_played_timestamp()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder());

        var library = await host.Client.GetOwnedGamesAsync(Account);

        // §4.2: rtime_last_played comes back only when the key belongs to the
        // queried account. A count of zero on a real run is the tell that it
        // does not.
        Assert.Equal(3, library.WithLastPlayed);
    }

    [Fact]
    public async Task The_library_never_renders_anything_secret_in_its_own_ToString()
    {
        using var host = new SteamWebTestHost(SteamWebTestHost.DefaultResponder(), apiKey: "SECRETKEY");

        var rendered = (await host.Client.GetOwnedGamesAsync(Account)).ToString();

        Assert.DoesNotContain("SECRETKEY", rendered, StringComparison.Ordinal);
        Assert.Contains("games=7", rendered, StringComparison.Ordinal);
    }

    // ── Registration ─────────────────────────────────────────────────────────

    [Fact]
    public void Registrations_defer_to_anything_already_in_the_container()
    {
        var cache = new InMemorySteamWebMetadataCache();
        var settings = new InMemorySettingsRepository();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISteamWebMetadataCache>(cache);
        services.AddSingleton<Hoard.Core.Repositories.ISettingsRepository>(settings);
        services.AddSteamWebApi();

        using var provider = services.BuildServiceProvider();

        Assert.Same(cache, provider.GetRequiredService<ISteamWebMetadataCache>());
        Assert.Same(settings, provider.GetRequiredService<Hoard.Core.Repositories.ISettingsRepository>());
    }

    /// <summary>
    /// The module composes and runs on a host with no key, no configuration
    /// provider and no database — which is exactly the state a first-run user is
    /// in.
    /// </summary>
    [Fact]
    public async Task The_module_works_with_nothing_configured()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISteamWebMetadataCache>(new InMemorySteamWebMetadataCache());
        services.AddSingleton<Hoard.Core.Repositories.ISettingsRepository>(new InMemorySettingsRepository());
        services.AddSteamWebApi();

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ISteamWebApiClient>();

        Assert.False(await client.IsConfiguredAsync());
        Assert.False((await client.GetOwnedGamesAsync(Account)).Succeeded);
        Assert.Empty(await client.GetOwnershipCandidatesAsync(Account));
    }
}
