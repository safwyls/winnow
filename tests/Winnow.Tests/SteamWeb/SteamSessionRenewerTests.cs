using System.Net;
using System.Text;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// One outbound request from S6's renewal exchange, captured before it would
/// have hit the wire. Carries the body, unlike
/// <see cref="RecordedSteamWebRequest"/>, because the refresh token travels in a
/// form body and the assertion that matters is which field it is in.
/// </summary>
public sealed record RecordedRenewalRequest(HttpMethod Method, Uri Uri, string? Cookie, string Body)
{
    /// <summary>The form fields, decoded.</summary>
    public IReadOnlyDictionary<string, string> Form
    {
        get
        {
            var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in Body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=');
                var name = equals < 0 ? pair : pair[..equals];
                var value = equals < 0 ? string.Empty : pair[(equals + 1)..];
                parsed[Uri.UnescapeDataString(name)] = Uri.UnescapeDataString(value.Replace('+', ' '));
            }

            return parsed;
        }
    }

    /// <summary>Scheme and host, which is what the closed request list is about.</summary>
    public string Origin => Uri.Scheme + "://" + Uri.Host;

    /// <summary>The URI without its query, for comparison against the endpoint constants.</summary>
    public string Endpoint => Uri.GetLeftPart(UriPartial.Path);
}

/// <summary>
/// The only transport S6's renewal tests use. Nothing here opens a socket.
/// </summary>
public sealed class FakeSteamRenewalHandler : HttpMessageHandler
{
    private readonly Func<RecordedRenewalRequest, int, HttpResponseMessage> _responder;
    private readonly Lock _lock = new();
    private readonly List<RecordedRenewalRequest> _requests = [];

    public FakeSteamRenewalHandler(Func<RecordedRenewalRequest, int, HttpResponseMessage> responder)
        => _responder = responder;

    /// <summary>Every request seen, in order. Empty is the assertion that nothing was renewed.</summary>
    public IReadOnlyList<RecordedRenewalRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>A JSON response, optionally setting cookies the way Steam's do.</summary>
    public static HttpResponseMessage Json(HttpStatusCode status, string json, params string[] setCookies)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        foreach (var cookie in setCookies)
        {
            response.Headers.TryAddWithoutValidation("Set-Cookie", cookie);
        }

        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var recorded = new RecordedRenewalRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join("; ", cookies) : null,
            body);

        int prior;
        lock (_lock)
        {
            prior = _requests.Count;
            _requests.Add(recorded);
        }

        return _responder(recorded, prior);
    }
}

/// <summary>
/// Fixtures for S6, built on the live capture rather than on the shape this
/// spike originally assumed.
/// </summary>
internal static class SteamRenewalFixtures
{
    /// <summary>The store transfer endpoint Steam's own finalizelogin response names.</summary>
    public const string StoreTransferUri = "https://store.steampowered.com/login/settoken";

    /// <summary>
    /// The refresh token in its LIVE shape: a bare JWT, three dot-separated
    /// segments, no <c>steamid64||</c> prefix
    /// (docs/spikes/steam-web-session-auth.md §7.2, captured 2026-08-31). The
    /// audience is the captured one, and <c>renew</c> is the claim that makes the
    /// token worth spending.
    ///
    /// <para>Everything in S6 is tested against this shape, deliberately. The
    /// <c>steamid64||jwt</c> fixture beside it in
    /// <see cref="SteamSessionFixtures"/> exercises the separator branch that the
    /// live capture proves is NOT the live path.</para>
    /// </summary>
    public static string BareRefreshToken(
        DateTimeOffset expiresAt, string subject = SteamSessionFixtures.Subject)
        => SteamSessionFixtures.Jwt($$"""
            {"iss":"steam","sub":"{{subject}}","aud":["web","renew","derive"],"exp":{{expiresAt.ToUnixTimeSeconds()}}}
            """);

    /// <summary>
    /// An access token with an audience the caller chooses, so the
    /// audience-change path can be driven without inventing a second token
    /// builder.
    /// </summary>
    public static string AccessToken(
        DateTimeOffset expiresAt,
        string audience = "web:store",
        string subject = SteamSessionFixtures.Subject,
        string issuer = "r:0012_ABCDEF")
        => SteamSessionFixtures.Jwt($$"""
            {"iss":"{{issuer}}","sub":"{{subject}}","aud":["{{audience}}"],"exp":{{expiresAt.ToUnixTimeSeconds()}}}
            """);

    /// <summary>A renewable session in the shape a live sign-in actually produces.</summary>
    public static SteamSession RenewableSession(
        DateTimeOffset now, TimeSpan? accessLife = null, TimeSpan? refreshLife = null)
        => SteamSession.TryCreate(
            AccessToken(now + (accessLife ?? TimeSpan.FromHours(24))),
            BareRefreshToken(now + (refreshLife ?? TimeSpan.FromDays(210))),
            now)!;

    /// <summary>Steam's finalizelogin body, naming whichever transfer hosts the test wants.</summary>
    public static string FinalizeBody(params string[] transferUris)
    {
        var entries = string.Join(
            ",",
            transferUris.Select(static u =>
                "{\"url\":\"" + u + "\",\"params\":{\"nonce\":\"transfer-nonce\",\"auth\":\"transfer-auth\"}}"));

        return "{\"steamID\":\"" + SteamSessionFixtures.Subject + "\","
            + "\"redir\":\"https://store.steampowered.com/login/\","
            + "\"transfer_info\":[" + entries + "]}";
    }

    /// <summary>The mint's signed-in answer.</summary>
    public static string MintBody(string token)
        => "{\"success\":1,\"data\":{\"webapi_token\":\"" + token + "\"}}";

    /// <summary>
    /// A responder for the happy path: finalize, one store transfer, one mint.
    /// </summary>
    public static Func<RecordedRenewalRequest, int, HttpResponseMessage> HappyPath(
        string mintedAccessToken, string? rotatedRefreshToken = null, string? transferUri = null)
    {
        var target = transferUri ?? StoreTransferUri;

        return (request, _) => request.Endpoint switch
        {
            SteamSessionRenewer.FinalizeLoginUri => FakeSteamRenewalHandler.Json(
                HttpStatusCode.OK,
                FinalizeBody(target),
                rotatedRefreshToken is null
                    ? []
                    : ["steamRefresh_steam=" + rotatedRefreshToken + "; Path=/; Secure; HttpOnly"]),

            var endpoint when endpoint == target => FakeSteamRenewalHandler.Json(
                HttpStatusCode.OK,
                "{}",
                "steamLoginSecure=76561198000000001%7C%7CtransferredCookieValue; Path=/; Secure; HttpOnly"),

            SteamSessionRenewer.TokenMintUri => FakeSteamRenewalHandler.Json(
                HttpStatusCode.OK, MintBody(mintedAccessToken)),

            _ => FakeSteamRenewalHandler.Json(HttpStatusCode.NotFound, "{}"),
        };
    }
}

/// <summary>
/// The three requests section 4.7's second amendment permits with nobody
/// watching, and the proof that there are no others.
///
/// <para>Condition 3 is the reason this class exists. Every test here is an
/// assertion about the request set, the request shapes, or how a refusal is
/// classified — never about the network, which is canned throughout.</para>
/// </summary>
public sealed class SteamSessionRenewerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_renewal_sends_exactly_the_three_permitted_requests_and_no_others()
    {
        var minted = SteamRenewalFixtures.AccessToken(Now.AddHours(24));
        var (renewer, handler) = Build(SteamRenewalFixtures.HappyPath(minted));

        var outcome = await renewer.RenewAsync(SteamRenewalFixtures.RenewableSession(Now));

        Assert.Equal(SteamRenewalStatus.Renewed, outcome.Status);
        Assert.Equal(minted, outcome.AccessToken);

        var requests = handler.Requests;
        Assert.Equal(3, requests.Count);

        // 1. finalizelogin, spending the refresh token as the nonce.
        Assert.Equal(HttpMethod.Post, requests[0].Method);
        Assert.Equal(SteamSessionRenewer.FinalizeLoginUri, requests[0].Endpoint);

        // 2. the transfer POST the first response named.
        Assert.Equal(HttpMethod.Post, requests[1].Method);
        Assert.Equal(SteamRenewalFixtures.StoreTransferUri, requests[1].Endpoint);

        // 3. the mint, and a GET of a JSON endpoint rather than a page.
        Assert.Equal(HttpMethod.Get, requests[2].Method);
        Assert.Equal(SteamSessionRenewer.TokenMintUri, requests[2].Endpoint);

        // Two origins, both named in this file, and no third one.
        Assert.Equal(
            new[] { "https://login.steampowered.com", "https://store.steampowered.com" },
            requests.Select(r => r.Origin).Distinct().Order().ToArray());
    }

    [Fact]
    public async Task The_refresh_token_is_spent_as_a_form_field_and_never_appears_in_a_uri()
    {
        var session = SteamRenewalFixtures.RenewableSession(Now);
        var (renewer, handler) = Build(
            SteamRenewalFixtures.HappyPath(SteamRenewalFixtures.AccessToken(Now.AddHours(24))));

        await renewer.RenewAsync(session);

        var finalize = handler.Requests[0];
        Assert.Equal(session.RefreshToken, finalize.Form["nonce"]);
        Assert.False(string.IsNullOrWhiteSpace(finalize.Form["sessionid"]));
        Assert.Equal("https://store.steampowered.com/login/", finalize.Form["redir"]);

        // A URI reaches the framework's own request logging; a form body does
        // not. Neither token may appear in one.
        foreach (var request in handler.Requests)
        {
            Assert.DoesNotContain(session.RefreshToken!, request.Uri.AbsoluteUri, StringComparison.Ordinal);
            Assert.DoesNotContain(session.AccessToken, request.Uri.AbsoluteUri, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_fresh_sessionid_is_generated_per_renewal_and_matches_its_own_cookie()
    {
        var (renewer, handler) = Build(
            SteamRenewalFixtures.HappyPath(SteamRenewalFixtures.AccessToken(Now.AddHours(24))));

        await renewer.RenewAsync(SteamRenewalFixtures.RenewableSession(Now));
        await renewer.RenewAsync(SteamRenewalFixtures.RenewableSession(Now));

        var first = handler.Requests[0].Form["sessionid"];
        var second = handler.Requests[3].Form["sessionid"];

        Assert.NotEqual(first, second);

        // Steam's CSRF check compares the form field against the cookie, so they
        // have to be the same value — and it is generated here, used once, and
        // never stored, because condition 2 names sessionid as something that
        // must not reach disk.
        Assert.Contains("sessionid=" + first, handler.Requests[0].Cookie);
    }

    [Fact]
    public async Task A_transfer_target_on_any_other_host_is_never_requested()
    {
        // Steam names several transfer hosts. Only the store's cookie is spent by
        // the mint, so only the store is asked — which is also what stops a
        // reshaped response body from directing Winnow's traffic anywhere.
        var (renewer, handler) = Build((request, _) => request.Endpoint switch
        {
            SteamSessionRenewer.FinalizeLoginUri => FakeSteamRenewalHandler.Json(
                HttpStatusCode.OK,
                SteamRenewalFixtures.FinalizeBody(
                    "https://help.steampowered.com/login/settoken",
                    "https://steamcommunity.com/login/settoken",
                    "http://store.steampowered.com/login/settoken",
                    "https://evil.example/login/settoken",
                    SteamRenewalFixtures.StoreTransferUri)),

            SteamRenewalFixtures.StoreTransferUri => FakeSteamRenewalHandler.Json(
                HttpStatusCode.OK, "{}", "steamLoginSecure=cookie-value; Path=/; Secure; HttpOnly"),

            SteamSessionRenewer.TokenMintUri => FakeSteamRenewalHandler.Json(
                HttpStatusCode.OK,
                SteamRenewalFixtures.MintBody(SteamRenewalFixtures.AccessToken(Now.AddHours(24)))),

            _ => FakeSteamRenewalHandler.Json(HttpStatusCode.NotFound, "{}"),
        });

        var outcome = await renewer.RenewAsync(SteamRenewalFixtures.RenewableSession(Now));

        Assert.Equal(SteamRenewalStatus.Renewed, outcome.Status);
        Assert.Equal(3, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, r => r.Uri.Host == "help.steampowered.com");
        Assert.DoesNotContain(handler.Requests, r => r.Uri.Host == "steamcommunity.com");
        Assert.DoesNotContain(handler.Requests, r => r.Uri.Host == "evil.example");

        // The plain-HTTP entry names the right host and is still refused: the
        // filter is on scheme as well, so a downgrade cannot carry the cookie.
        Assert.All(handler.Requests, r => Assert.Equal(Uri.UriSchemeHttps, r.Uri.Scheme));
    }

    [Fact]
    public async Task The_login_cookie_is_carried_to_the_mint_by_hand_and_leaves_nothing_behind()
    {
        var (renewer, handler) = Build(
            SteamRenewalFixtures.HappyPath(SteamRenewalFixtures.AccessToken(Now.AddHours(24))));

        var outcome = await renewer.RenewAsync(SteamRenewalFixtures.RenewableSession(Now));

        // Sent explicitly on the mint request. There is no cookie jar on this
        // pipeline, so if the header were absent the exchange would simply not
        // authenticate — which is exactly the property being pinned.
        Assert.Contains("steamLoginSecure=", handler.Requests[2].Cookie);

        // And it does not come back out. The outcome carries two token fields and
        // a fixed reason string, and there is nowhere for a cookie to ride along.
        Assert.Null(outcome.RefreshToken);
        Assert.DoesNotContain("steamLoginSecure", outcome.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("transferredCookieValue", outcome.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rotated_refresh_cookie_is_carried_out_of_the_exchange()
    {
        var rotated = SteamRenewalFixtures.BareRefreshToken(Now.AddDays(210));
        var (renewer, _) = Build(SteamRenewalFixtures.HappyPath(
            SteamRenewalFixtures.AccessToken(Now.AddHours(24)), rotatedRefreshToken: rotated));

        var outcome = await renewer.RenewAsync(SteamRenewalFixtures.RenewableSession(Now));

        Assert.Equal(SteamRenewalStatus.Renewed, outcome.Status);
        Assert.Equal(rotated, outcome.RefreshToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Steam_refusing_the_nonce_is_a_hard_rejection(HttpStatusCode status)
    {
        var (renewer, handler) = Build((_, _) => FakeSteamRenewalHandler.Json(status, "{}"));

        var outcome = await renewer.RenewAsync(SteamRenewalFixtures.RenewableSession(Now));

        Assert.Equal(SteamRenewalStatus.Rejected, outcome.Status);

        // And the exchange stops there: no transfer, no mint.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task An_access_denied_eresult_on_a_200_is_a_hard_rejection()
    {
        // node-steam-session issue #56 reports AccessDenied (EResult 15) on every
        // refresh route, unresolved as of 2026-05. It is planned for as a live
        // possibility rather than as a formality.
        var (renewer, _) = Build((request, _) => request.Endpoint == SteamSessionRenewer.FinalizeLoginUri
            ? FakeSteamRenewalHandler.Json(HttpStatusCode.OK, """{"eresult":15}""")
            : FakeSteamRenewalHandler.Json(HttpStatusCode.NotFound, "{}"));

        var outcome = await renewer.RenewAsync(SteamRenewalFixtures.RenewableSession(Now));

        Assert.Equal(SteamRenewalStatus.Rejected, outcome.Status);
    }

    [Fact]
    public async Task An_access_denied_header_on_a_200_is_a_hard_rejection()
    {
        var (renewer, _) = Build((request, _) =>
        {
            if (request.Endpoint != SteamSessionRenewer.FinalizeLoginUri)
            {
                return FakeSteamRenewalHandler.Json(HttpStatusCode.NotFound, "{}");
            }

            var response = FakeSteamRenewalHandler.Json(HttpStatusCode.OK, "{}");
            response.Headers.TryAddWithoutValidation("x-eresult", "15");
            return response;
        });

        Assert.Equal(
            SteamRenewalStatus.Rejected,
            (await renewer.RenewAsync(SteamRenewalFixtures.RenewableSession(Now))).Status);
    }

    [Theory]
    [InlineData(SteamRenewalFixtures.StoreTransferUri)]
    [InlineData(SteamSessionRenewer.TokenMintUri)]
    public async Task An_access_denied_on_a_200_is_caught_at_every_step_not_only_the_first(string stage)
    {
        // The check used to run only in finalizelogin's did-not-parse branch, so
        // a denial arriving on the transfer or the mint was read as transient and
        // retried on every pass forever instead of surfacing. The mint is the
        // case that matters most: it is the step furthest from the refusal a user
        // could reason about.
        var (renewer, handler) = Build((request, _) =>
        {
            if (request.Endpoint == stage)
            {
                var denied = FakeSteamRenewalHandler.Json(HttpStatusCode.OK, """{"eresult":15}""");
                return denied;
            }

            return request.Endpoint switch
            {
                SteamSessionRenewer.FinalizeLoginUri => FakeSteamRenewalHandler.Json(
                    HttpStatusCode.OK,
                    SteamRenewalFixtures.FinalizeBody(SteamRenewalFixtures.StoreTransferUri)),
                SteamRenewalFixtures.StoreTransferUri => FakeSteamRenewalHandler.Json(
                    HttpStatusCode.OK, "{}", "steamLoginSecure=cookie; Path=/"),
                SteamSessionRenewer.TokenMintUri => FakeSteamRenewalHandler.Json(
                    HttpStatusCode.OK,
                    SteamRenewalFixtures.MintBody(SteamRenewalFixtures.AccessToken(Now.AddHours(24)))),
                _ => FakeSteamRenewalHandler.Json(HttpStatusCode.NotFound, "{}"),
            };
        });

        var outcome = await renewer.RenewAsync(SteamRenewalFixtures.RenewableSession(Now));

        Assert.Equal(SteamRenewalStatus.Rejected, outcome.Status);

        // And the exchange stops at the step that refused rather than pressing on.
        Assert.Equal(stage, handler.Requests[^1].Endpoint);
    }

    [Fact]
    public async Task An_access_denied_header_is_caught_on_the_mint_too()
    {
        var (renewer, _) = Build((request, _) =>
        {
            if (request.Endpoint != SteamSessionRenewer.TokenMintUri)
            {
                return request.Endpoint == SteamSessionRenewer.FinalizeLoginUri
                    ? FakeSteamRenewalHandler.Json(
                        HttpStatusCode.OK,
                        SteamRenewalFixtures.FinalizeBody(SteamRenewalFixtures.StoreTransferUri))
                    : FakeSteamRenewalHandler.Json(
                        HttpStatusCode.OK, "{}", "steamLoginSecure=cookie; Path=/");
            }

            var response = FakeSteamRenewalHandler.Json(
                HttpStatusCode.OK,
                SteamRenewalFixtures.MintBody(SteamRenewalFixtures.AccessToken(Now.AddHours(24))));
            response.Headers.TryAddWithoutValidation("x-eresult", "15");
            return response;
        });

        // A denial header beats a body that would otherwise have parsed into a
        // perfectly good token.
        Assert.Equal(
            SteamRenewalStatus.Rejected,
            (await renewer.RenewAsync(SteamRenewalFixtures.RenewableSession(Now))).Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Anything_that_is_not_a_refusal_is_transient(HttpStatusCode status)
    {
        var (renewer, _) = Build((_, _) => FakeSteamRenewalHandler.Json(status, "{}"));

        var outcome = await renewer.RenewAsync(SteamRenewalFixtures.RenewableSession(Now));

        // The direction the doubt goes in is chosen: being wrong here costs one
        // skipped pass, and being wrong the other way costs the user their
        // session.
        Assert.Equal(SteamRenewalStatus.Transient, outcome.Status);
    }

    [Fact]
    public async Task A_mint_that_answers_as_signed_out_is_a_rejection()
    {
        // An empty webapi_token is what pointssummary returns to a caller it does
        // not consider signed in. The cookie chain did not take, so the refresh
        // token did not buy a session.
        var (renewer, _) = Build((request, _) => request.Endpoint switch
        {
            SteamSessionRenewer.FinalizeLoginUri => FakeSteamRenewalHandler.Json(
                HttpStatusCode.OK, SteamRenewalFixtures.FinalizeBody(SteamRenewalFixtures.StoreTransferUri)),
            SteamRenewalFixtures.StoreTransferUri => FakeSteamRenewalHandler.Json(
                HttpStatusCode.OK, "{}", "steamLoginSecure=cookie; Path=/"),
            SteamSessionRenewer.TokenMintUri => FakeSteamRenewalHandler.Json(
                HttpStatusCode.OK, """{"success":1,"data":{"webapi_token":""}}"""),
            _ => FakeSteamRenewalHandler.Json(HttpStatusCode.NotFound, "{}"),
        });

        Assert.Equal(
            SteamRenewalStatus.Rejected,
            (await renewer.RenewAsync(SteamRenewalFixtures.RenewableSession(Now))).Status);
    }

    [Fact]
    public async Task A_mint_body_this_client_cannot_read_is_transient_rather_than_a_sign_out()
    {
        // The distinction that keeps a Valve deploy from signing everybody out: a
        // body naming the field and leaving it blank is an answer; a body with no
        // such field is a shape we do not know.
        var (renewer, _) = Build((request, _) => request.Endpoint switch
        {
            SteamSessionRenewer.FinalizeLoginUri => FakeSteamRenewalHandler.Json(
                HttpStatusCode.OK, SteamRenewalFixtures.FinalizeBody(SteamRenewalFixtures.StoreTransferUri)),
            SteamRenewalFixtures.StoreTransferUri => FakeSteamRenewalHandler.Json(
                HttpStatusCode.OK, "{}", "steamLoginSecure=cookie; Path=/"),
            SteamSessionRenewer.TokenMintUri => FakeSteamRenewalHandler.Json(
                HttpStatusCode.OK, """{"renamed_everything":true}"""),
            _ => FakeSteamRenewalHandler.Json(HttpStatusCode.NotFound, "{}"),
        });

        Assert.Equal(
            SteamRenewalStatus.Transient,
            (await renewer.RenewAsync(SteamRenewalFixtures.RenewableSession(Now))).Status);
    }

    [Fact]
    public async Task A_finalize_response_with_no_usable_transfer_target_is_transient()
    {
        var (renewer, handler) = Build((request, _) => request.Endpoint
            == SteamSessionRenewer.FinalizeLoginUri
                ? FakeSteamRenewalHandler.Json(HttpStatusCode.OK, """{"steamID":"1","transfer_info":[]}""")
                : FakeSteamRenewalHandler.Json(HttpStatusCode.NotFound, "{}"));

        Assert.Equal(
            SteamRenewalStatus.Transient,
            (await renewer.RenewAsync(SteamRenewalFixtures.RenewableSession(Now))).Status);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_session_with_no_refresh_token_sends_nothing_at_all()
    {
        var (renewer, handler) = Build((_, _) => FakeSteamRenewalHandler.Json(HttpStatusCode.OK, "{}"));

        var tokenOnly = SteamSession.TryCreate(
            SteamRenewalFixtures.AccessToken(Now.AddHours(24)), refreshToken: null, Now)!;

        var outcome = await renewer.RenewAsync(tokenOnly);

        Assert.Equal(SteamRenewalStatus.NotRenewable, outcome.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void The_renewal_client_is_registered_without_a_cookie_jar_or_redirects()
    {
        // Condition 2 forbids a cookie jar at rest; this registration forbids one
        // in memory as well, so steamLoginSecure cannot outlive the call that
        // read it. AllowAutoRedirect is off for the same reason condition 3 is a
        // closed list: a redirect is a request to a host this module did not
        // name, carrying a cookie it did not choose to send.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSteamWebApi();

        using var provider = services.BuildServiceProvider();

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(SteamSessionRenewer.HttpClientName);

        var primary = handler;
        while (primary is DelegatingHandler delegating && delegating.InnerHandler is { } inner)
        {
            primary = inner;
        }

        var client = Assert.IsType<HttpClientHandler>(primary);
        Assert.False(client.UseCookies);
        Assert.False(client.AllowAutoRedirect);
    }

    private static (SteamSessionRenewer Renewer, FakeSteamRenewalHandler Handler) Build(
        Func<RecordedRenewalRequest, int, HttpResponseMessage> responder)
    {
        var handler = new FakeSteamRenewalHandler(responder);
        return (new SteamSessionRenewer(new SingleClientFactory(handler)), handler);
    }

    /// <summary>An <see cref="IHttpClientFactory"/> that hands out one canned transport.</summary>
    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
