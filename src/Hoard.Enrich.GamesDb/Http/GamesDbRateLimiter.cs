using System.Threading.RateLimiting;
using Polly;
using Polly.RateLimiting;

namespace Hoard.Enrich.GamesDb.Http;

/// <summary>
/// The single gate in front of gamesdb.gog.com. A singleton, so every pass and
/// every retry attempt draws from one budget — a per-client limiter would
/// multiply the ceiling by the number of clients and be exactly the kind of
/// traffic that gets an unpublished endpoint closed.
///
/// <para>Token bucket rather than a fixed window: a library sweep is allowed a
/// short burst while the long-run average holds. <c>QueueLimit</c> is
/// effectively unbounded so callers <i>wait</i> for a permit rather than being
/// rejected — this is background work with nothing better to do, and a rejection
/// would only become a retry anyway.</para>
///
/// <para>This is the only place in this module allowed to make a request wait.
/// No call site sleeps.</para>
/// </summary>
public sealed class GamesDbRateLimiter : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter;

    public GamesDbRateLimiter(GamesDbOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var permits = Math.Max(1, options.RequestsPerSecond);
        _limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = permits,
            TokensPerPeriod = permits,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = int.MaxValue,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });

        Pipeline = new ResiliencePipelineBuilder()
            .AddRateLimiter(new RateLimiterStrategyOptions
            {
                RateLimiter = args => _limiter.AcquireAsync(1, args.Context.CancellationToken),
            })
            .Build();
    }

    /// <summary>Polly pipeline that acquires one permit around each execution.</summary>
    public ResiliencePipeline Pipeline { get; }

    /// <summary>Permits currently available. Diagnostics and tests.</summary>
    public int AvailablePermits => (int)_limiter.GetStatistics()!.CurrentAvailablePermits;

    public void Dispose()
    {
        _limiter.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Applies <see cref="GamesDbRateLimiter"/> to every outbound request.
///
/// <para>Innermost handler, below retry, so a retried attempt spends a permit
/// too — a backoff storm must not be able to exceed the configured rate.</para>
/// </summary>
public sealed class GamesDbRateLimitingHandler : DelegatingHandler
{
    private readonly GamesDbRateLimiter _limiter;

    public GamesDbRateLimitingHandler(GamesDbRateLimiter limiter) => _limiter = limiter;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => await _limiter.Pipeline.ExecuteAsync(
            async token => await base.SendAsync(request, token), cancellationToken);
}
