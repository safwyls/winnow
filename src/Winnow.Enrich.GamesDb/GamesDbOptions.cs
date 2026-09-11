namespace Winnow.Enrich.GamesDb;

/// <summary>Tunables for the gamesdb.gog.com identity graph.</summary>
public sealed class GamesDbOptions
{
    /// <summary>Uncompressed response ceiling enforced inside every attempted send.</summary>
    public long MaxResponseBytes { get; set; } = Winnow.Http.ProviderHttpTransport.DefaultMaxResponseBytes;
    /// <summary>One attempt, including response body buffering.</summary>
    public TimeSpan AttemptTimeout { get; set; } = Winnow.Http.ProviderHttpTransport.DefaultAttemptTimeout;
    /// <summary>Total request budget, including retries and rate-limit waiting.</summary>
    public TimeSpan OverallTimeout { get; set; } = Winnow.Http.ProviderHttpTransport.DefaultOverallTimeout;

    /// <summary>Service root. Trailing slash required — relative URIs hang off it.</summary>
    public Uri BaseAddress { get; set; } = new("https://gamesdb.gog.com/");

    /// <summary>
    /// Requests per second. Background graph lookups use a conservative rate
    /// because this endpoint has no published capacity guarantee.
    /// </summary>
    public int RequestsPerSecond { get; set; } = 4;

    /// <summary>
    /// How long a validated graph answer or confirmed miss is reused.
    /// Cached graph references alone do not establish edition equivalence;
    /// automatic identity links also require current matching release evidence.
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
