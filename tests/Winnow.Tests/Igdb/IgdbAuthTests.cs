using System.Net;
using Winnow.Enrich.Igdb.Auth;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.Igdb.Storage;
using Xunit;

namespace Winnow.Tests.Igdb;

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

    /// <summary>
    /// The client secret goes in a form-encoded body, never in the URI.
    ///
    /// <para>A URI is the most-copied string in an HTTP stack: it lands in
    /// <c>HttpClient</c> logging, in <c>HttpRequestException</c> messages, in
    /// proxy access logs, in Polly telemetry, and in this module's own
    /// request-replay diagnostics. §4.4 documents the query-string form and
    /// Twitch accepts it, but the credential in v1 is user-supplied and stored
    /// locally (§4.2) and there is no reason to spray it across every log that
    /// happens to record a URL.</para>
    /// </summary>
    [Fact]
    public async Task The_client_secret_never_appears_in_the_token_request_uri()
    {
        using var host = new IgdbTestHost(
            IgdbTestHost.DefaultResponder(), clientSecret: "super-secret-value");

        await host.Client.ResolveBySteamAppIdsAsync(TwoAppIds);

        var token = Assert.Single(
            host.Handler.Requests, r => string.Equals(r.Endpoint, "token", StringComparison.Ordinal));

        Assert.DoesNotContain("super-secret-value", token.Uri.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("client_secret", token.Uri.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, token.Uri.Query);

        // It is in the body, form-encoded, alongside the other two parameters.
        Assert.Equal("application/x-www-form-urlencoded", token.ContentType);
        Assert.Contains("client_secret=super-secret-value", token.Body, StringComparison.Ordinal);
        Assert.Contains("grant_type=client_credentials", token.Body, StringComparison.Ordinal);
        Assert.Contains("client_id=test-client", token.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A secret containing reserved characters must survive the round trip —
    /// the reason the old code escaped its query values, and a property the
    /// form encoding has to keep.
    /// </summary>
    [Fact]
    public async Task A_secret_with_reserved_characters_is_escaped_not_truncated()
    {
        using var host = new IgdbTestHost(
            IgdbTestHost.DefaultResponder(), clientSecret: "a&b=c d+e%f");

        await host.Client.ResolveBySteamAppIdsAsync(TwoAppIds);

        var token = Assert.Single(
            host.Handler.Requests, r => string.Equals(r.Endpoint, "token", StringComparison.Ordinal));

        var parsed = ParseForm(token.Body);
        Assert.Equal("a&b=c d+e%f", parsed["client_secret"]);
        Assert.Equal("client_credentials", parsed["grant_type"]);
    }

    /// <summary>
    /// Minimal <c>application/x-www-form-urlencoded</c> reader: split on the
    /// separators, then undo percent-encoding and the '+'-for-space convention.
    /// </summary>
    private static Dictionary<string, string> ParseForm(string body)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.IndexOf('=', StringComparison.Ordinal);
            if (split < 0)
            {
                continue;
            }

            parsed[Uri.UnescapeDataString(pair[..split].Replace('+', ' '))] =
                Uri.UnescapeDataString(pair[(split + 1)..].Replace('+', ' '));
        }

        return parsed;
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
