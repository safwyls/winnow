namespace Winnow.Enrich.GamesDb;

/// <summary>
/// Tunables for the gamesdb.gog.com identity graph.
///
/// <para><b>Every default here is a courtesy setting, not a documented one.</b>
/// This is GOG Galaxy's own backing service, unversioned and unpublished — the
/// same category as api.steamcmd.net in §4.5. There is no published rate limit
/// to respect, which means the only defensible posture is to stay well under
/// what the real client would generate and to cache hard enough that a warm
/// library never touches it again.</para>
/// </summary>
public sealed class GamesDbOptions
{
    /// <summary>Service root. Trailing slash required — relative URIs hang off it.</summary>
    public Uri BaseAddress { get; set; } = new("https://gamesdb.gog.com/");

    /// <summary>
    /// Requests per second. Four, against the ~8 req/s the spike measured
    /// comfortably: this runs behind a library the user is already browsing and
    /// has nothing to gain from finishing sooner, and halving the rate of an
    /// unpublished endpoint costs 8 seconds on a 67-title library.
    /// </summary>
    public int RequestsPerSecond { get; set; } = 4;

    /// <summary>
    /// How long a resolved (or unresolvable) id stays authoritative.
    ///
    /// <para>90 days, longer than IGDB's 30. This answers "which stores sell
    /// this same game", which changes on the order of a store adding a back
    /// catalogue title — and unlike IGDB's metadata, a stale answer here cannot
    /// be wrong in a way the user sees. Long TTL is also the main courtesy this
    /// module owes a volunteer-shaped service.</para>
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromDays(90);

    /// <summary>Retry attempts after the first try, for 429/5xx/transient failures.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>First backoff step; subsequent steps are exponential with jitter.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Ceiling on any single backoff, including one derived from <c>Retry-After</c>.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Sent on every request so GOG can attribute the traffic and block it by name if they want to.</summary>
    public string UserAgent { get; set; } =
        "Winnow/0.1 (+https://github.com/winnow-app; local game library manager)";
}
