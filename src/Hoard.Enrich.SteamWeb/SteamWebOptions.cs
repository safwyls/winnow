namespace Hoard.Enrich.SteamWeb;

/// <summary>
/// Tunables for the Steam Web API client. Defaults encode the constraints in
/// <c>game-library-design.md</c> §4.2; nothing here should need changing to run
/// correctly, only to run differently.
/// </summary>
public sealed class SteamWebOptions
{
    /// <summary>Web API root. Trailing slash required (relative URIs hang off it).</summary>
    public Uri BaseAddress { get; set; } = new("https://api.steampowered.com/");

    /// <summary>
    /// How long a cached <c>GetOwnedGames</c> payload stays authoritative.
    ///
    /// <para>Six hours. The call costs exactly one request for the entire
    /// library whatever its size (841 games came back in a single 263 KB
    /// response on 2026-08-24), so against §4.2's nominal 100,000 calls/day the
    /// TTL is not a budget decision at all — it is a freshness decision. Six
    /// hours means a game bought this morning is in the library by this
    /// afternoon while a chatty sync loop still costs four requests a day.</para>
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Ceiling on outbound requests, enforced by a shared token-bucket limiter
    /// on the HttpClient pipeline (never by sleeping at a call site).
    ///
    /// <para>One per second. §4.2's nominal 100,000 calls/day is ~1.16/s
    /// sustained, and this module's whole job is one request per sync, so the
    /// limiter exists to bound a pathological caller rather than to pace a
    /// backfill.</para>
    /// </summary>
    public int RequestsPerSecond { get; set; } = 1;

    /// <summary>Retry attempts after the first try, for 429/5xx/transient failures.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>First backoff step; subsequent steps are exponential with jitter.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Ceiling on any single backoff, including one derived from
    /// <c>Retry-After</c>. §4.2: since June 2025 Steam throttles profile
    /// endpoints with 429 and a <c>Retry-After</c> of 60–120 s, so the cap has
    /// to be able to honour the top of that range — but no more, or a hostile or
    /// mistaken header could park a background sync for hours.
    ///
    /// <para>Worst case is therefore <see cref="MaxRetryAttempts"/> × 120 s ≈ 6
    /// minutes of waiting inside one call. That is fine for the background
    /// enrichment path and is exactly why §5.1 forbids putting this on a
    /// user-facing one.</para>
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>Sent on every request so Valve can attribute — and if necessary contact — this traffic.</summary>
    public string UserAgent { get; set; } = "Hoard/0.1 (+https://github.com/hoard-app; local game library manager)";
}
