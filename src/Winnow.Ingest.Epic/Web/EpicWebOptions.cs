namespace Winnow.Ingest.Epic.Web;

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

    /// <summary>Library service root. Trailing slash required.</summary>
    public Uri LibraryBaseAddress { get; set; }
        = new("https://library-service.live.use1a.on.epicgames.com/");

    /// <summary>Catalog service root. Trailing slash required.</summary>
    public Uri CatalogBaseAddress { get; set; }
        = new("https://catalog-public-service-prod.ol.epicgames.com/");

    /// <summary>How long a catalog answer stays authoritative.</summary>
    public TimeSpan CatalogCacheTtl { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How many catalog item ids to put in one bulk request.</summary>
    public int CatalogBatchSize { get; set; } = 20;

    /// <summary>
    /// <c>country</c> sent to the catalog service. What the launcher sends.
    /// </summary>
    public string CatalogCountry { get; set; } = "US";

    /// <summary><c>locale</c> sent to the catalog service. Fixed to match the English IGDB corpus.</summary>
    public string CatalogLocale { get; set; } = "en";

    /// <summary>
    /// Harvest URL that issues an authorization code for a browser with an existing
    /// Epic session. <c>{0}</c> is the client id. Not a starting page for cold browsers.
    /// </summary>
    public string AuthorizationCodeUrlFormat { get; set; }
        = "https://www.epicgames.com/id/api/redirect?clientId={0}&responseType=code";

    /// <summary>
    /// OAuth authorize endpoint the embedded browser starts on. <c>{0}</c> is the
    /// client id, <c>{1}</c> the URL-encoded redirect. Renders a real login form.
    /// </summary>
    public string AuthorizeUrlFormat { get; set; }
        = "https://www.epicgames.com/id/authorize?client_id={0}&response_type=code&scope=basic_profile"
        + "&redirect_uri={1}";

    /// <summary>
    /// Whether the embedded browser starts on <see cref="AuthorizeUrlFormat"/>
    /// rather than <see cref="AuthorizationCodeUrlFormat"/>. Must be true for
    /// cold browser profiles that have no existing Epic session.
    /// </summary>
    public bool UseAuthorizeEndpointForSignIn { get; set; } = true;

    /// <summary>The only redirect target Epic's launcher client accepts; watched by the embedded browser.</summary>
    public Uri LauncherRedirectUrl { get; set; } = new("https://localhost/launcher/authorized");

    /// <summary>How long a fetched Epic library stays authoritative before a resync refetches it.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Headroom applied to both token expiries, so a token that is about to
    /// lapse is renewed rather than spent on a request that will 401.
    /// </summary>
    public TimeSpan TokenRefreshSkew { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How to read Epic's <c>totalTime</c>. See <see cref="Model.EpicPlaytimeUnit"/>.</summary>
    public Model.EpicPlaytimeUnit PlaytimeUnit { get; set; } = Model.EpicPlaytimeUnit.Seconds;

    /// <summary>
    /// Access-token lifetime assumed only when Epic's response carries neither
    /// <c>expires_at</c> nor <c>expires_in</c>. Deliberately short.
    /// </summary>
    public TimeSpan FallbackAccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Ceiling on outbound requests per second, enforced by a shared token-bucket limiter.</summary>
    public int RequestsPerSecond { get; set; } = 4;

    /// <summary>Retry attempts after the first try, for 429/5xx/transient failures.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>First backoff step; subsequent steps are exponential with jitter.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Ceiling on any single backoff, including one derived from <c>Retry-After</c>.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Guard against a cursor that never terminates.</summary>
    public int MaxLibraryPages { get; set; } = 50;

    /// <summary><c>User-Agent</c> sent on every request. Launcher-style with Winnow appended.</summary>
    public string UserAgent { get; set; }
        = "UELauncher/11.0.1-14907503+++Portal+Release-Live Windows/10.0.19041.1.256.64bit Winnow/0.1";
}
