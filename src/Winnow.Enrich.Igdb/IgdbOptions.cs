namespace Winnow.Enrich.Igdb;

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
    public string UserAgent { get; set; } = "Winnow/0.1 (+https://github.com/winnow-app; local game library manager)";

    /// <summary>
    /// IGDB's <c>external_game_source</c> id for Steam. The old
    /// <c>external_games.category</c> enum is deprecated in favour of this
    /// reference field; both use 1 for Steam. Configurable so a source-id change
    /// does not need a code change.
    /// </summary>
    public int SteamExternalGameSourceId { get; set; } = 1;

    /// <summary>
    /// IGDB's <c>external_game_source</c> id for GOG.
    ///
    /// <para><b>This one is a genuine hard join and it works today.</b> IGDB's
    /// source-5 <c>uid</c> is the bare GOG product id as a string — byte-identical
    /// to what Galaxy's <c>gog_&lt;id&gt;</c> releaseKey carries and to what
    /// Winnow stores in <c>external_ids.provider_id</c>, with no transformation.
    /// Re-verified live against the author's library: 13 of 14 owned GOG base
    /// games matched in a single request. The one miss is
    /// <c>1441199941</c>, "The Witcher 3 REDkit" — a modding toolkit IGDB does
    /// not carry as a game, which is the right answer rather than a failure.</para>
    /// </summary>
    public int GogExternalGameSourceId { get; set; } = 5;

    /// <summary>
    /// IGDB's <c>external_game_source</c> id for the Epic Games Store.
    ///
    /// <para><b>Present for completeness and all but useless — read this before
    /// building on it.</b> §4.4 claims <c>external_games</c> maps the "Epic
    /// catalog id"; it does not. IGDB's source-26 uids are Epic <i>store offer</i>
    /// ids (32-hex) and CMS <i>page</i> ids (dashed UUID), and the launcher
    /// writes neither to disk — it writes <c>CatalogItemId</c>, which is a third
    /// id space. Measured twice, once during the spike and once again while
    /// fixing this: <b>0 of the author's 67 owned Epic catalog item ids match any
    /// of IGDB's 10,145 source-26 rows</b>, and titles like ABZU have no
    /// source-26 row at all, so no id mapping could rescue it. Epic reaches IGDB
    /// through the cross-store hop instead — see
    /// <c>Winnow.Enrich.GamesDb</c>. This id stays configured so the attempt is
    /// one line if IGDB's Epic coverage ever changes shape, not because it
    /// currently resolves anything.</para>
    /// </summary>
    public int EpicExternalGameSourceId { get; set; } = 26;

    /// <summary>
    /// The <c>external_game_source</c> id to query for one
    /// <c>ExternalIdProviders</c> value, or null when that provider's ids are
    /// not something IGDB indexes.
    ///
    /// <para>Null is a real answer and callers must treat it as "do not ask",
    /// never as "ask with zero" — a wrong source id returns an empty page, and
    /// an empty page from a source that was asked wrongly is indistinguishable
    /// from a game IGDB has never heard of once it reaches the cache.</para>
    /// </summary>
    public int? ExternalGameSourceIdFor(string provider) => provider switch
    {
        Core.Domain.ExternalIdProviders.Steam => SteamExternalGameSourceId,
        Core.Domain.ExternalIdProviders.Gog => GogExternalGameSourceId,
        _ => null,
    };
}
