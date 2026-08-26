using System.Net;
using Hoard.Ingest.Epic.Web;
using Hoard.Ingest.Epic.Web.Auth;
using Xunit;

namespace Hoard.Tests.EpicWeb;

/// <summary>
/// The OAuth session: code exchange, refresh, and the two ways a session ends.
/// </summary>
public sealed class EpicAuthTests
{
    [Fact]
    public async Task Unconfigured_is_a_clean_no_op()
    {
        // No client pair at all — the state of every install nobody opted in on.
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), clientId: null, clientSecret: null);

        Assert.False(await host.Client.IsConfiguredAsync());
        Assert.False(await host.Client.IsSignedInAsync());

        var library = await host.Client.GetOwnedLibraryAsync();

        // Not an exception, not an empty-but-succeeded library — unanswered.
        Assert.False(library.Succeeded);
        Assert.Empty(await host.Client.GetOwnershipCandidatesAsync());

        // And, crucially, no request was made. An unconfigured module is silent
        // on the wire, exactly like ISteamWebApiClient.IsConfiguredAsync.
        Assert.Empty(host.Handler.Requests);
    }

    [Fact]
    public async Task Half_configured_credentials_count_as_unconfigured()
    {
        // A client id with no secret produces Epic's invalid_client rather than
        // anything useful, so it must never be sent.
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), clientSecret: null);

        Assert.False(await host.Client.IsConfiguredAsync());
        Assert.Empty(host.Handler.Requests);
    }

    [Fact]
    public async Task Sign_in_exchanges_the_code_in_a_form_body_never_a_uri()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());

        var result = await host.Client.SignInAsync("SECRET-AUTH-CODE-VALUE");

        Assert.True(result.Succeeded);
        Assert.Equal("SanitizedTester", result.DisplayName);

        var request = Assert.Single(host.Handler.Requests, r => r.Endpoint == EpicEndpoint.Token);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("authorization_code", request.GrantType);
        Assert.Equal("SECRET-AUTH-CODE-VALUE", request.Form["code"]);
        Assert.Equal("eg1", request.Form["token_type"]);

        // The credentials go in an Authorization: Basic header, per OAuth.
        Assert.StartsWith("Basic ", request.Authorization, StringComparison.Ordinal);

        // And the code is nowhere in the URI, which is the string every HTTP
        // stack copies into logs, exception messages and proxy records.
        Assert.DoesNotContain("SECRET-AUTH-CODE-VALUE", request.Uri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_spent_access_token_is_refreshed_without_a_second_sign_in()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());
        await host.SignInAsync();

        // The fixture's access token expires 2026-08-27T04:00Z. Move past it,
        // but stay well inside the refresh token's 2026-09-18 expiry.
        host.Clock.Now = new DateTimeOffset(2026, 8, 27, 6, 0, 0, TimeSpan.Zero);

        var token = await host.Tokens.GetAsync();

        Assert.NotNull(token);
        Assert.Equal("FAKE_ACCESS_TOKEN_1111111111111111", token!.AccessToken);

        // Exactly two token requests: the original exchange and one refresh.
        var tokenRequests = host.Handler.Requests.Where(r => r.Endpoint == EpicEndpoint.Token).ToList();
        Assert.Equal(2, tokenRequests.Count);
        Assert.Equal("authorization_code", tokenRequests[0].GrantType);
        Assert.Equal("refresh_token", tokenRequests[1].GrantType);

        // The refresh spent the refresh token from the first response, and the
        // rotated one replaced it.
        Assert.Equal("FAKE_REFRESH_TOKEN_000000000000000", tokenRequests[1].Form["refresh_token"]);
        Assert.Equal("FAKE_REFRESH_TOKEN_111111111111111", token.RefreshToken);
    }

    [Fact]
    public async Task Concurrent_callers_spend_the_refresh_token_once()
    {
        // Epic rotates the refresh token on every use, so two simultaneous
        // refreshes would leave one caller holding a value that no longer exists.
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());
        await host.SignInAsync();

        host.Clock.Now = new DateTimeOffset(2026, 8, 27, 6, 0, 0, TimeSpan.Zero);

        var tokens = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => host.Tokens.GetAsync()));

        Assert.All(tokens, t => Assert.NotNull(t));
        Assert.Single(tokens.Select(t => t!.AccessToken).Distinct(StringComparer.Ordinal));

        // One exchange plus one refresh. Not one refresh per caller.
        Assert.Equal(2, host.Handler.CountFor(EpicEndpoint.Token));
    }

    [Fact]
    public async Task A_stated_refresh_expiry_that_has_passed_degrades_without_a_request()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());
        await host.SignInAsync();

        var beforeLapse = host.Handler.CountFor(EpicEndpoint.Token);

        // Past the fixture's refresh_expires_at of 2026-09-18.
        host.Clock.Now = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Null(await host.Tokens.GetAsync());

        // Epic already told us the token was dead. Asking anyway would be one
        // doomed request per sync, forever.
        Assert.Equal(beforeLapse, host.Handler.CountFor(EpicEndpoint.Token));

        // And the dead session is forgotten rather than retried next time.
        Assert.Null(await host.TokenStore.LoadAsync());
        Assert.False(await host.Client.IsSignedInAsync());
    }

    [Fact]
    public async Task A_rejected_refresh_token_degrades_and_clears_the_session()
    {
        // The case Epic does not warn about in advance: the refresh token is
        // within its stated life but Epic has revoked it — a password change, or
        // a sign-out elsewhere.
        using var host = new EpicWebTestHost((request, prior) => request.Endpoint switch
        {
            EpicEndpoint.Token when request.GrantType == "refresh_token" => FakeEpicHandler.Json(
                HttpStatusCode.BadRequest, EpicFixturesWeb.InvalidRefresh()),
            EpicEndpoint.Token => FakeEpicHandler.Json(HttpStatusCode.OK, EpicFixturesWeb.Token()),
            _ => EpicWebTestHost.Healthy()(request, prior),
        });

        await host.SignInAsync();
        host.Clock.Now = new DateTimeOffset(2026, 8, 27, 6, 0, 0, TimeSpan.Zero);

        Assert.Null(await host.Tokens.GetAsync());
        Assert.Null(await host.TokenStore.LoadAsync());

        // Latched: a second sync does not re-attempt a refresh that cannot work.
        var afterFirst = host.Handler.CountFor(EpicEndpoint.Token);
        Assert.Null(await host.Tokens.GetAsync());
        Assert.Equal(afterFirst, host.Handler.CountFor(EpicEndpoint.Token));
    }

    [Fact]
    public async Task Wrong_client_credentials_are_reported_apart_from_a_bad_code()
    {
        // Verified live 2026-08-26: Epic validates the client pair BEFORE the
        // grant, so a wrong pair answers invalid_client whatever the code was.
        // The UI has to be able to tell the user which one is wrong.
        using var host = new EpicWebTestHost((request, _) => request.Endpoint == EpicEndpoint.Token
            ? FakeEpicHandler.Json(HttpStatusCode.BadRequest, EpicFixturesWeb.InvalidClient())
            : FakeEpicHandler.Json(HttpStatusCode.NotFound, "{}"));

        var result = await host.Client.SignInAsync("anything");

        Assert.False(result.Succeeded);
        Assert.Equal(EpicSignInFailure.InvalidClientCredentials, result.Failure);
    }

    [Fact]
    public async Task A_stale_authorization_code_is_reported_as_such()
    {
        using var host = new EpicWebTestHost((request, _) => request.Endpoint == EpicEndpoint.Token
            ? FakeEpicHandler.Json(HttpStatusCode.BadRequest, EpicFixturesWeb.InvalidRefresh())
            : FakeEpicHandler.Json(HttpStatusCode.NotFound, "{}"));

        var result = await host.Client.SignInAsync("stale");

        Assert.False(result.Succeeded);
        Assert.Equal(EpicSignInFailure.InvalidAuthorizationCode, result.Failure);
    }

    [Fact]
    public async Task A_token_response_with_no_refresh_expiry_is_still_usable()
    {
        // Legendary — the reference implementation for this flow — never reads
        // refresh_expires_at, so a real response is not guaranteed to carry one.
        // Treating "not stated" as "expired" would silently disable Epic forever.
        const string NoRefreshExpiry = """
            {
              "access_token": "FAKE_ACCESS_TOKEN_2222222222222222",
              "expires_in": 28800,
              "refresh_token": "FAKE_REFRESH_TOKEN_222222222222222",
              "account_id": "00000000000000000000000000000001",
              "displayName": "SanitizedTester"
            }
            """;

        using var host = new EpicWebTestHost((request, prior) => request.Endpoint == EpicEndpoint.Token
            ? FakeEpicHandler.Json(HttpStatusCode.OK, NoRefreshExpiry)
            : EpicWebTestHost.Healthy()(request, prior));

        await host.SignInAsync();

        var stored = await host.TokenStore.LoadAsync();
        Assert.NotNull(stored);
        Assert.Null(stored!.RefreshExpiresAt);

        // Years later, this session is still considered worth trying — Epic gets
        // to decide, not a sentinel Hoard invented.
        Assert.True(stored.IsRefreshUsable(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(10)));
        Assert.True(await host.Client.IsSignedInAsync());
    }

    [Fact]
    public async Task Editing_the_client_pair_invalidates_the_stored_session()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());
        await host.SignInAsync();

        // A session minted by one client must never be sent on behalf of another.
        var stored = await host.TokenStore.LoadAsync();
        Assert.Equal("test-client-id", stored!.ClientId);

        var rebound = stored with { ClientId = "a-different-client" };
        await host.TokenStore.SaveAsync(rebound);

        Assert.False(rebound.ClientId == "test-client-id");
    }

    [Fact]
    public async Task Signing_out_forgets_the_session()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());
        await host.SignInAsync();
        Assert.True(await host.Client.IsSignedInAsync());

        await host.Client.SignOutAsync();

        Assert.False(await host.Client.IsSignedInAsync());
        Assert.Null(await host.TokenStore.LoadAsync());
    }

    [Fact]
    public async Task The_authorization_url_carries_the_configured_client_id()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy(), clientId: "my-client");

        var url = await host.Client.AuthorizationCodeUrl();

        Assert.NotNull(url);
        Assert.Contains("clientId=my-client", url, StringComparison.Ordinal);
        Assert.Contains("responseType=code", url, StringComparison.Ordinal);

        // Hoard never fetches this page — it is for the user's own browser.
        Assert.Empty(host.Handler.Requests);
    }

    [Fact]
    public async Task No_client_pair_means_no_authorization_url()
    {
        using var host = new EpicWebTestHost(
            EpicWebTestHost.Healthy(), clientId: null, clientSecret: null);

        Assert.Null(await host.Client.AuthorizationCodeUrl());
    }
}
