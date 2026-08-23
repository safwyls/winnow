namespace Hoard.Enrich.Igdb;

/// <summary>
/// Tunables for the IGDB client. Defaults encode the constraints in
/// <c>game-library-design.md</c> §4.4; nothing here should need changing to
/// run correctly, only to run differently.
/// </summary>
public sealed class IgdbOptions
{
    /// <summary>Apicalypse endpoint root. Trailing slash required (relative URIs hang off it).</summary>
    public Uri BaseAddress { get; set; } = new("https://api.igdb.com/v4/");

    /// <summary>Twitch client-credentials token endpoint (§4.4).</summary>
    public Uri TokenEndpoint { get; set; } = new("https://id.twitch.tv/oauth2/token");

    /// <summary>
    /// §4.4: 4 requests/second per credential. Enforced by a shared token-bucket
    /// rate limiter behind a Polly strategy on the HttpClient pipeline, never by
    /// sleeping at a call site.
    /// </summary>
    public int RequestsPerSecond { get; set; } = 4;

    /// <summary>
    /// How long a cached IGDB payload stays authoritative before a refetch.
    /// 30 days: IGDB records for shipped games are effectively static — name,
    /// cover, release year and summary change on the order of never — while the
    /// 4 req/s ceiling makes refetching expensive. Short enough that a
    /// correction reaches users within a month, long enough that a full library
    /// re-scan is free. Callers may override per call.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Ids per Apicalypse request — the whole point of the
    /// <c>where uid = (…)</c> form: a 616-game library resolves in two requests,
    /// not 616.
    ///
    /// <para>400 rather than the 500-row <c>limit</c> ceiling, because
    /// <c>external_games</c> can return more than one row per appid (regional
    /// and storefront variants). The 25% headroom means the common case reads a
    /// batch in a single request; <c>offset</c> paging still covers a batch that
    /// overflows, so correctness never depends on the headroom being enough.</para>
    /// </summary>
    public int BatchSize { get; set; } = 400;

    /// <summary>Retry attempts after the first try, for 429/5xx/transient failures.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>First backoff step; subsequent steps are exponential with jitter.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Ceiling on any single backoff, including one derived from <c>Retry-After</c>.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Refresh a token this far before its stated expiry. Tokens last ~60 days,
    /// so the skew only has to cover a long-running request.
    /// </summary>
    public TimeSpan TokenRefreshSkew { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Sent on every request so Valve/Twitch can attribute traffic.</summary>
    public string UserAgent { get; set; } = "Hoard/0.1 (+https://github.com/hoard-app; local game library manager)";

    /// <summary>
    /// IGDB's <c>external_game_source</c> id for Steam. The old
    /// <c>external_games.category</c> enum is deprecated in favour of this
    /// reference field; both use 1 for Steam. Configurable so a source-id change
    /// does not need a code change.
    /// </summary>
    public int SteamExternalGameSourceId { get; set; } = 1;
}
