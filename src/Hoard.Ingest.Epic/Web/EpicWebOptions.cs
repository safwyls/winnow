namespace Hoard.Ingest.Epic.Web;

/// <summary>
/// Tunables for the authenticated Epic client. Defaults encode what
/// <c>docs/spikes/epic-oauth.md</c> established; nothing here should need
/// changing to run correctly, only to run differently.
/// </summary>
public sealed class EpicWebOptions
{
    /// <summary>
    /// OAuth token endpoint. Verified live 2026-08-26: rejects GET with 405 and
    /// validates the client pair before the grant.
    /// </summary>
    public Uri TokenEndpoint { get; set; }
        = new("https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token");

    /// <summary>
    /// Library service root. Trailing slash required — relative URIs hang off it.
    ///
    /// <para>The unusual host is correct and is not a regional mirror to be
    /// substituted: <c>live.use1a.on.epicgames.com</c> is the service's only
    /// published address. Verified live 2026-08-26.</para>
    /// </summary>
    public Uri LibraryBaseAddress { get; set; }
        = new("https://library-service.live.use1a.on.epicgames.com/");

    /// <summary>
    /// The page the user signs in on to obtain an authorization code. Presented
    /// to the user; never fetched by Hoard.
    ///
    /// <para><c>{0}</c> is the configured client id. The page is Epic's own, and
    /// the code it prints is what
    /// <see cref="Auth.IEpicTokenProvider.SignInWithAuthorizationCodeAsync"/>
    /// consumes.</para>
    ///
    /// <para><b>This is a harvest URL, not a starting page, and the difference is
    /// not cosmetic.</b> It is an API that answers for a browser that already
    /// holds Epic's cookies: a cold browser gets every code field null and never
    /// sees a login form. The manual flow works because the user opens it in
    /// their OWN browser, where they are already signed in. The embedded flow
    /// starts at <see cref="AuthorizeUrlFormat"/> and only comes here once a
    /// session exists.</para>
    /// </summary>
    public string AuthorizationCodeUrlFormat { get; set; }
        = "https://www.epicgames.com/id/api/redirect?clientId={0}&responseType=code";

    /// <summary>
    /// The conventional OAuth authorize endpoint — the page the embedded browser
    /// starts on. <c>{0}</c> is the client id, <c>{1}</c> the URL-encoded
    /// redirect.
    ///
    /// <para><b>CONFIRMED: this reaches a real login form.</b> The spike drove
    /// <c>/id/authorize</c> with the registered redirect through a real browser
    /// and landed on <c>"Sign in to Your Epic Games account"</c> with the email
    /// field present. That is the property that matters here and it is the reason
    /// this is the start URL rather than
    /// <see cref="AuthorizationCodeUrlFormat"/>.</para>
    ///
    /// <para><b>Still UNVERIFIED, and still recorded as such:</b> that Epic's
    /// authenticated flow 302s to <see cref="LauncherRedirectUrl"/> carrying
    /// <c>?code=</c>. Interception of such a redirect is confirmed; the redirect
    /// firing is not. <b>Nothing depends on it any more</b> — the flow collects
    /// the code from <see cref="AuthorizationCodeUrlFormat"/> once a session
    /// exists, rather than waiting for Epic to volunteer one — so this went from
    /// a load-bearing hypothesis to a shortcut that either fires or does not. If
    /// it fires, the sign-in reports "redirect interception" and the question is
    /// finally settled.</para>
    /// </summary>
    public string AuthorizeUrlFormat { get; set; }
        = "https://www.epicgames.com/id/authorize?client_id={0}&response_type=code&scope=basic_profile"
        + "&redirect_uri={1}";

    /// <summary>
    /// Whether the embedded browser starts on <see cref="AuthorizeUrlFormat"/>
    /// rather than <see cref="AuthorizationCodeUrlFormat"/>.
    ///
    /// <para><b>True, and it has to be. This defaulted to false once and the flow
    /// could not work for anybody.</b> <c>id/api/redirect</c> is an API endpoint
    /// that issues a code for a browser that ALREADY holds Epic's cookies; an
    /// embedded browser opens an isolated profile with none, so it answered every
    /// first-time user with <c>{"authorizationCode":null,"exchangeCode":null,…}</c>
    /// and never rendered a login form. The original default was chosen to avoid
    /// promoting an unverified hypothesis, which was the right instinct applied
    /// to a false choice: only one of the two URLs can BEGIN an unauthenticated
    /// flow, and it is this one.</para>
    ///
    /// <para>Setting it false starts on the redirect endpoint instead, which is
    /// only useful for a browser profile that is already signed in — the flow
    /// detects the null-code answer, names it, and sends the user to the login
    /// page anyway rather than failing obscurely.</para>
    /// </summary>
    public bool UseAuthorizeEndpointForSignIn { get; set; } = true;

    /// <summary>
    /// The only redirect target Epic's launcher client accepts, and therefore the
    /// URL the embedded browser watches for.
    ///
    /// <para>Verified live 2026-08-26 that the allowlist is EXACT: loopback,
    /// other ports and <c>http</c> instead of <c>https</c> are all rejected with
    /// <c>client_redirect_domain_mismatch</c>. This is also why RFC 8252 loopback
    /// is not an option for Epic — it is unavailable, not merely worse.</para>
    ///
    /// <para>Nothing listens on this address and nothing needs to. A navigation
    /// is observable before the connection is attempted, so no HTTPS listener and
    /// no certificate are involved.</para>
    /// </summary>
    public Uri LauncherRedirectUrl { get; set; } = new("https://localhost/launcher/authorized");

    /// <summary>
    /// How long a fetched Epic library stays authoritative before a resync will
    /// refetch it.
    ///
    /// <para>Six hours, matching <c>SteamWebOptions.CacheTtl</c> for the same
    /// reason: the whole library costs a small, bounded number of requests
    /// however large it is, so the TTL is a freshness decision rather than a
    /// budget one. A game bought this morning is in the library this afternoon,
    /// while the 15-minute snapshot scheduler still costs four fetches a day
    /// instead of ninety-six.</para>
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Headroom applied to both token expiries, so a token that is about to
    /// lapse is renewed rather than spent on a request that will 401.
    /// </summary>
    public TimeSpan TokenRefreshSkew { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How to read Epic's <c>totalTime</c>.
    ///
    /// <para>Seconds by default, and <b>this is the one figure in the module that
    /// is a reading rather than a verified fact</b> — see
    /// <see cref="Model.EpicPlaytimeUnit"/> for the evidence and for why the
    /// blast radius is smaller than it appears. A user who finds Hoard's Epic
    /// hours 60× off the launcher's own "You've Played" display flips this.</para>
    /// </summary>
    public Model.EpicPlaytimeUnit PlaytimeUnit { get; set; } = Model.EpicPlaytimeUnit.Seconds;

    /// <summary>
    /// Access-token lifetime assumed only when Epic's response carries neither
    /// <c>expires_at</c> nor <c>expires_in</c>.
    ///
    /// <para>Deliberately short. The widely-circulated "8 hours" could not be
    /// confirmed from any authoritative source, and a wrong long guess produces
    /// requests that fail with a token this client still believes in, while a
    /// wrong short guess costs one extra refresh. The error is made cheap in the
    /// direction it will actually be wrong.</para>
    /// </summary>
    public TimeSpan FallbackAccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Ceiling on outbound requests, enforced by a shared token-bucket limiter
    /// on the HttpClient pipeline (never by sleeping at a call site).
    ///
    /// <para>Four per second. Epic publishes no rate limit and sends no
    /// <c>X-RateLimit-*</c> or <c>Retry-After</c> headers, but 429s are real —
    /// Legendary has an open crash report from one on the assets endpoint. With
    /// no documented budget to spend against, this matches the IGDB ceiling
    /// (§4.4) as a deliberately conservative figure: a full library paginates in
    /// a handful of requests, so nothing here is throughput-sensitive and there
    /// is no reason to push.</para>
    /// </summary>
    public int RequestsPerSecond { get; set; } = 4;

    /// <summary>Retry attempts after the first try, for 429/5xx/transient failures.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>First backoff step; subsequent steps are exponential with jitter.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Ceiling on any single backoff, including one derived from
    /// <c>Retry-After</c> should Epic ever start sending it.
    ///
    /// <para>Bounded so a mistaken or hostile header cannot park a background
    /// sync for hours. Worst case is <see cref="MaxRetryAttempts"/> × this,
    /// which is why §5.1 keeps this module off every user-facing path.</para>
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How many library pages to follow before stopping.
    ///
    /// <para>A guard against a cursor that never terminates, not a real
    /// expectation: at Epic's page size this is far more library than any
    /// account has. Hitting it is logged as the anomaly it would be.</para>
    /// </summary>
    public int MaxLibraryPages { get; set; } = 50;

    /// <summary>
    /// <c>User-Agent</c> sent on every request.
    ///
    /// <para><b>This one is not like the others.</b> The Steam and IGDB clients
    /// send a descriptive Hoard user agent so Valve can attribute — and if
    /// necessary contact — the traffic (§4.3). Epic's launcher services answer
    /// requests that look like the launcher, and every tool in this space sends
    /// a launcher string. Hoard sends a launcher string with its own name
    /// appended: enough to be recognisably the launcher protocol, and honest
    /// about who is speaking rather than a bare impersonation. If Epic ever
    /// publishes a supported client, this becomes a plain descriptive agent like
    /// the other two modules use.</para>
    /// </summary>
    public string UserAgent { get; set; }
        = "UELauncher/11.0.1-14907503+++Portal+Release-Live Windows/10.0.19041.1.256.64bit Hoard/0.1";
}
