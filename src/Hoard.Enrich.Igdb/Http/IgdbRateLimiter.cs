using System.Threading.RateLimiting;
using Polly;
using Polly.RateLimiting;

namespace Hoard.Enrich.Igdb.Http;

/// <summary>
/// The single 4 req/s gate in front of api.igdb.com (§4.4). Registered as a
/// singleton so every typed client, background job and retry attempt draws from
/// the same budget — a per-client limiter would multiply the ceiling by the
/// number of clients and get the credential throttled.
///
/// <para>Token bucket rather than a fixed window: bursts up to the bucket size
/// are allowed (which is what a batched 616-appid resolve wants) while the
/// long-run average stays at the limit. <c>QueueLimit</c> is effectively
/// unbounded so callers <i>wait</i> for a permit instead of being rejected —
/// enrichment is a background activity with nothing better to do, and a
/// rejection would only turn into a retry anyway.</para>
/// </summary>
public sealed class IgdbRateLimiter : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter;

    public IgdbRateLimiter(IgdbOptions options)
    {
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

    /// <summary>Requests currently waiting for a permit. Diagnostics and tests.</summary>
    public int QueuedRequests => (int)_limiter.GetStatistics()!.CurrentQueuedCount;

    public void Dispose() => _limiter.Dispose();
}
