using System.Net;
using Winnow.Enrich.Igdb.Auth;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.Igdb.Storage;
using Microsoft.Extensions.Configuration;
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

    // ══ Protected at rest (TASK-78) ═════════════════════════════════════════

    /// <summary>
    /// What reaches disk is one protected blob. The token never appears as
    /// itself in any row, and the three plaintext rows an older build wrote are
    /// left empty — a token cached by this build leaves nothing readable behind.
    /// </summary>
    [Fact]
    public async Task The_persisted_token_is_one_protected_row_and_no_plaintext_rows()
    {
        var settings = new InMemorySettingsStore();

        using (var first = new IgdbTestHost(IgdbTestHost.DefaultResponder(), settings: settings))
        {
            await first.Client.ResolveBySteamAppIdsAsync(TwoAppIds);
            Assert.Equal(1, first.Handler.CountFor("token"));
        }

        var blob = await settings.GetAsync(TwitchTokenProvider.TokenBlobKey);
        Assert.False(string.IsNullOrWhiteSpace(blob));

        // Every token the default responder mints starts with this marker; if
        // it appears in the stored row, the row is the token, not a cipher.
        Assert.DoesNotContain("token-", blob, StringComparison.Ordinal);

        // Never written, never migrated: this build leaves the legacy rows unset.
        Assert.Null(await settings.GetAsync(TwitchTokenProvider.TokenValueKey));
        Assert.Null(await settings.GetAsync(TwitchTokenProvider.TokenClientIdKey));
        Assert.Null(await settings.GetAsync(TwitchTokenProvider.TokenExpiresAtKey));
    }

    /// <summary>
    /// The upgrade path: an install from before protected storage has its token
    /// in three plaintext rows. The first use migrates them into the blob and
    /// leaves them empty, and the token is reused rather than re-minted.
    /// </summary>
    [Fact]
    public async Task A_plaintext_token_from_an_earlier_version_is_migrated_and_reused()
    {
        var settings = new InMemorySettingsStore();
        await settings.SetAsync(SettingsTableCredentialSource.ClientIdKey, "test-client");
        await settings.SetAsync(SettingsTableCredentialSource.ClientSecretKey, "test-secret");

        // The clock starts 2026-01-01; a token expiring in March is worth reusing.
        await settings.SetAsync(TwitchTokenProvider.TokenClientIdKey, "test-client");
        await settings.SetAsync(TwitchTokenProvider.TokenValueKey, "legacy-token-value");
        await settings.SetAsync(
            TwitchTokenProvider.TokenExpiresAtKey, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).ToString("O"));

        using var host = new IgdbTestHost(IgdbTestHost.DefaultResponder(), settings: settings);
        await host.Client.ResolveBySteamAppIdsAsync(TwoAppIds);

        // Reused, not re-minted.
        Assert.Equal(0, host.Handler.CountFor("token"));

        // Migrated: blob written without the token in it, plaintext rows emptied.
        var blob = await settings.GetAsync(TwitchTokenProvider.TokenBlobKey);
        Assert.False(string.IsNullOrWhiteSpace(blob));
        Assert.DoesNotContain("legacy-token-value", blob, StringComparison.Ordinal);
        Assert.Equal(string.Empty, await settings.GetAsync(TwitchTokenProvider.TokenValueKey));
        Assert.Equal(string.Empty, await settings.GetAsync(TwitchTokenProvider.TokenClientIdKey));
        Assert.Equal(string.Empty, await settings.GetAsync(TwitchTokenProvider.TokenExpiresAtKey));
    }

    /// <summary>
    /// A host that cannot encrypt mints and uses the token in memory but stores
    /// nothing — no plaintext row is ever written. Enrichment still works via
    /// configuration-supplied credentials (the settings-table secret is
    /// refused on such a host, which is the other half of this test's point);
    /// the token is minted again after a restart, which is the refusal §4.7's
    /// second amendment asks for.
    /// </summary>
    [Fact]
    public async Task A_host_that_cannot_encrypt_mints_and_stores_nothing()
    {
        var settings = new InMemorySettingsStore();

        using (var first = new IgdbTestHost(
            IgdbTestHost.DefaultResponder(),
            clientId: null,
            clientSecret: null,
            settings: settings,
            protector: new UnavailableIgdbSecretProtector(),
            configuration: DeveloperCredentials()))
        {
            await first.Client.ResolveBySteamAppIdsAsync(TwoAppIds);
            Assert.Equal(1, first.Handler.CountFor("token"));
        }

        Assert.Null(await settings.GetAsync(TwitchTokenProvider.TokenBlobKey));
    }

    /// <summary>
    /// The one place a plaintext row is emptied even on a host that cannot
    /// encrypt: the token was minted, not typed, so the row can only pay out to
    /// whatever else reads the disk. A mint is cheap; a bearer credential in the
    /// clear is not.
    /// </summary>
    [Fact]
    public async Task A_host_that_cannot_encrypt_still_empties_plaintext_token_rows()
    {
        var settings = new InMemorySettingsStore();

        await settings.SetAsync(TwitchTokenProvider.TokenClientIdKey, "test-client");
        await settings.SetAsync(TwitchTokenProvider.TokenValueKey, "legacy-token-value");
        await settings.SetAsync(
            TwitchTokenProvider.TokenExpiresAtKey, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).ToString("O"));

        using var host = new IgdbTestHost(
            IgdbTestHost.DefaultResponder(),
            clientId: null,
            clientSecret: null,
            settings: settings,
            protector: new UnavailableIgdbSecretProtector(),
            configuration: DeveloperCredentials());
        await host.Client.ResolveBySteamAppIdsAsync(TwoAppIds);

        // Not reused — the plaintext token is refused — and re-minted instead.
        Assert.Equal(1, host.Handler.CountFor("token"));

        // But emptied either way: the machine-minted row never survives a visit.
        Assert.Equal(string.Empty, await settings.GetAsync(TwitchTokenProvider.TokenValueKey));
        Assert.Equal(string.Empty, await settings.GetAsync(TwitchTokenProvider.TokenClientIdKey));
        Assert.Equal(string.Empty, await settings.GetAsync(TwitchTokenProvider.TokenExpiresAtKey));
        Assert.Null(await settings.GetAsync(TwitchTokenProvider.TokenBlobKey));
    }

    /// <summary>
    /// Credentials as the developer path supplies them, for the tests that run
    /// on a host that cannot encrypt — where the settings-table secret is
    /// refused and the environment variables are the supported path.
    /// </summary>
    private static IConfiguration DeveloperCredentials()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Igdb:ClientId"] = "test-client",
                ["Igdb:ClientSecret"] = "test-secret",
            })
            .Build();

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
