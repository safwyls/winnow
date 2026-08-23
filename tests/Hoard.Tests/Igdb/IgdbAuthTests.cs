using System.Net;
using Hoard.Enrich.Igdb.Auth;
using Hoard.Enrich.Igdb.Credentials;
using Hoard.Enrich.Igdb.Storage;
using Xunit;

namespace Hoard.Tests.Igdb;

/// <summary>
/// §4.4 auth rules: Twitch client-credentials, <c>Client-ID</c> and
/// <c>Authorization: Bearer</c> on every request, tokens cached (~60 days) and
/// refreshed rather than re-minted per request.
/// </summary>
public class IgdbAuthTests
{
    private static readonly string[] TwoAppIds = ["440", "570"];
    private static readonly string[] OtherAppIds = ["620", "730"];

    [Fact]
    public async Task Token_is_minted_once_and_reused_across_calls()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        await host.Client.ResolveBySteamAppIdsAsync(TwoAppIds);
        await host.Client.ResolveBySteamAppIdsAsync(OtherAppIds);
        await host.Client.GetGamesAsync([100_440, 100_570]);

        Assert.Equal(1, host.Handler.CountFor("token"));
        Assert.Equal(1, host.TokenProvider.MintCount);

        // Three IGDB calls really did go out — the single token is reuse, not
        // three calls collapsing into one.
        Assert.Equal(2, host.Handler.CountFor("external_games"));
        Assert.Equal(1, host.Handler.CountFor("games"));
    }

    [Fact]
    public async Task Every_igdb_request_carries_client_id_and_bearer_headers()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        await host.Client.ResolveBySteamAppIdsAsync(TwoAppIds);

        var igdbRequests = host.Handler.Requests
            .Where(r => !string.Equals(r.Endpoint, "token", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(igdbRequests);
        foreach (var request in igdbRequests)
        {
            Assert.Equal("test-client", request.ClientId);
            Assert.StartsWith("Bearer ", request.Authorization, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Expired_token_is_refreshed_once_on_401_and_the_request_succeeds()
    {
        var tokens = 0;
        using var host = new IgdbTestHost((request, priorForEndpoint) => request.Endpoint switch
        {
            "token" => FakeHttpMessageHandler.Json(
                HttpStatusCode.OK,
                IgdbFixtures.TokenResponse(
                    Interlocked.Increment(ref tokens) == 1 ? "stale-token" : "fresh-token")),

            // The first attempt carries the stale bearer and is rejected; the
            // handler must re-mint and replay rather than give up.
            "external_games" when priorForEndpoint == 0
                => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"message\":\"Unauthorized\"}"),
            "external_games" => FakeHttpMessageHandler.Json(
                HttpStatusCode.OK, IgdbFixtures.ExternalGames(request.Body)),
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "[]"),
        });

        var matches = await host.Client.ResolveBySteamAppIdsAsync(TwoAppIds);

        Assert.Equal(2, host.Handler.CountFor("token"));
        Assert.Equal(2, host.TokenProvider.MintCount);
        Assert.Equal(2, host.Handler.CountFor("external_games"));
        Assert.Equal(2, matches.Count);

        var replay = host.Handler.Requests.Last(
            r => string.Equals(r.Endpoint, "external_games", StringComparison.Ordinal));
        Assert.Equal("Bearer fresh-token", replay.Authorization);

        // The replayed request kept its Apicalypse body — a 401 retry that
        // dropped the body would silently return the whole table.
        Assert.Contains("\"440\"", replay.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repeated_401_is_surfaced_rather_than_looping_on_token_minting()
    {
        using var host = new IgdbTestHost((request, _) => request.Endpoint switch
        {
            "token" => FakeHttpMessageHandler.Json(HttpStatusCode.OK, IgdbFixtures.TokenResponse("t")),
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{}"),
        });

        var matches = await host.Client.ResolveBySteamAppIdsAsync(TwoAppIds);

        Assert.Empty(matches);
        Assert.Equal(2, host.Handler.CountFor("external_games"));
        Assert.Equal(2, host.Handler.CountFor("token"));
    }

    [Fact]
    public async Task Persisted_token_survives_a_restart_and_is_not_re_minted()
    {
        var settings = new InMemorySettingsStore();

        using (var first = new IgdbTestHost(IgdbTestHost.DefaultResponder(), settings: settings))
        {
            await first.Client.ResolveBySteamAppIdsAsync(TwoAppIds);
            Assert.Equal(1, first.Handler.CountFor("token"));
        }

        // A fresh provider graph — the process restarting — sharing only the
        // settings table, and different appids so a network call is still made.
        using var second = new IgdbTestHost(IgdbTestHost.DefaultResponder(), settings: settings);
        await second.Client.ResolveBySteamAppIdsAsync(OtherAppIds);

        Assert.Equal(0, second.Handler.CountFor("token"));
        Assert.Equal(1, second.Handler.CountFor("external_games"));
    }

    [Fact]
    public async Task Token_stored_for_one_client_id_is_not_reused_after_credentials_change()
    {
        var settings = new InMemorySettingsStore();
        using (var first = new IgdbTestHost(IgdbTestHost.DefaultResponder(), settings: settings))
        {
            await first.Client.ResolveBySteamAppIdsAsync(TwoAppIds);
        }

        await settings.SetAsync(SettingsTableCredentialSource.ClientIdKey, "a-different-client");

        using var second = new IgdbTestHost(
            IgdbTestHost.DefaultResponder(), clientId: null, clientSecret: null, settings: settings);
        await second.Client.ResolveBySteamAppIdsAsync(OtherAppIds);

        Assert.Equal(1, second.Handler.CountFor("token"));
        Assert.Equal(
            "a-different-client",
            second.Handler.Requests.First(r => r.Endpoint == "external_games").ClientId);
    }

    [Fact]
    public async Task Expiring_token_is_replaced_when_the_clock_passes_its_expiry()
    {
        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder());

        await host.Client.ResolveBySteamAppIdsAsync(TwoAppIds);
        Assert.Equal(1, host.Handler.CountFor("token"));

        // Twitch tokens last ~60 days; step past that.
        host.Clock.Advance(TimeSpan.FromDays(61));
        await host.Client.ResolveBySteamAppIdsAsync(OtherAppIds);

        Assert.Equal(2, host.Handler.CountFor("token"));
    }

    [Fact]
    public void Credential_and_token_records_redact_their_values_when_stringified()
    {
        var credentials = new IgdbCredentials("client-abc", "secret-xyz") { Source = "settings" };
        var token = new IgdbAccessToken("client-abc", "bearer-value-123", DateTimeOffset.UnixEpoch);

        // ToString is overridden precisely so that an interpolated log line
        // cannot leak these (§4.2: never logged, never committed).
        Assert.DoesNotContain("secret-xyz", credentials.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("bearer-value-123", token.ToString(), StringComparison.Ordinal);
    }
}
