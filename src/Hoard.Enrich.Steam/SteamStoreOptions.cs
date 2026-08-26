namespace Hoard.Enrich.Steam;

/// <summary>
/// Tunables for the Steam store-frontend client. Defaults encode what
/// <c>docs/spikes/steam-store-tags.md</c> verified live on 2026-08-23 and what
/// <c>game-library-design.md</c> §4.3 requires; nothing here should need
/// changing to run correctly, only to run differently.
/// </summary>
public sealed class SteamStoreOptions
{
    /// <summary>Web API root. Trailing slash required (relative URIs hang off it).</summary>
    public Uri BaseAddress { get; set; } = new("https://api.steampowered.com/");

    /// <summary>
    /// Appids per <c>GetItems</c> request. The spike sent 102 in one request and
    /// got 102 <c>store_items</c> back (124187 bytes, ~1.2 KB/app), so 100 is a
    /// verified-safe round number: a 616-game library costs 7 requests, not 616.
    ///
    /// <para>No documented ceiling exists — the endpoint is undocumented — so
    /// this stays at the observed-good figure rather than being pushed higher on
    /// the assumption that more would also work.</para>
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Tags requested per app. The spike found the response caps at 20 whatever
    /// this says (100 returned the same 20) because Steam itself publishes only a
    /// top-20 list. Kept configurable only so the request matches the documented
    /// shape exactly.
    /// </summary>
    public int TagCount { get; set; } = 20;

    /// <summary>Store context: language for names and tag vocabulary.</summary>
    public string Language { get; set; } = "english";

    /// <summary>Store context: country, which selects the storefront region.</summary>
    public string CountryCode { get; set; } = "US";

    /// <summary>Store context: 1 = the global Steam realm (2 is Steam China).</summary>
    public int SteamRealm { get; set; } = 1;

    /// <summary>
    /// How long a cached store item stays authoritative. §4.3 sets a floor of 24
    /// hours; 7 days sits well above it while staying short enough that a
    /// renamed or newly-tagged app is picked up within a week — affordable
    /// because a full 616-game refresh is 7 batched requests, not 616.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How long the tag vocabulary stays authoritative. 446 tags arrive in one
    /// 15 KB request and the list changes on the order of never, so 30 days
    /// costs one request a month. The response carries a <c>version_hash</c>
    /// (<see cref="Model.SteamTagVocabulary.VersionHash"/>) for callers that want
    /// to detect a change rather than assume one.
    /// </summary>
    public TimeSpan TagListCacheTtl { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// How long the store-category vocabulary stays authoritative. 72 categories
    /// arrive in one 16 KB keyless request and Valve adds one every year or two
    /// (the accessibility block, ids 64-82, is the most recent), so 30 days costs
    /// one request a month — the same bargain as the tag list, for the same
    /// reason.
    /// </summary>
    public TimeSpan StoreCategoryCacheTtl { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Ceiling on outbound requests to api.steampowered.com, enforced by a shared
    /// token-bucket limiter on the HttpClient pipeline (never by sleeping at a
    /// call site).
    ///
    /// <para>The spike saw 8 back-to-back batched requests (~2.7 req/s) all
    /// return 200 with no <c>Retry-After</c>, and explicitly calls that a lower
    /// bound rather than the ceiling. 2 req/s sits under the only rate we have
    /// evidence for; §4.3's "Valve rate-limits traffic that resembles scraping"
    /// is the governing constraint, and absence of evidence is not evidence of
    /// absence.</para>
    /// </summary>
    public int RequestsPerSecond { get; set; } = 2;

    /// <summary>Retry attempts after the first try, for 429/5xx/transient failures.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>First backoff step; subsequent steps are exponential with jitter.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Ceiling on any single backoff, including one derived from
    /// <c>Retry-After</c>. §4.2 reports Steam sending 60–120 s on throttled
    /// endpoints, so the cap has to be able to honour that; this is background
    /// work with nothing to block.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>Sent on every request so Valve can attribute traffic (§4.3).</summary>
    public string UserAgent { get; set; } = "Hoard/0.1 (+https://github.com/hoard-app; local game library manager)";
}
