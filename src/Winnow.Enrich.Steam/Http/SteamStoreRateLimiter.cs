using System.Threading.RateLimiting;
using Polly;
using Polly.RateLimiting;

namespace Winnow.Enrich.Steam.Http;

/// <summary>
/// The single gate in front of api.steampowered.com's store endpoints.
/// Registered as a singleton so every typed client, background job and retry
/// attempt draws from the same budget — a per-client limiter would multiply the
/// ceiling by the number of clients and get the IP throttled.
///
/// <para>Token bucket rather than a fixed window: a batched backfill is allowed
/// a short burst while the long-run average stays at
/// <see cref="SteamStoreOptions.RequestsPerSecond"/>. <c>QueueLimit</c> is
/// effectively unbounded so callers <i>wait</i> for a permit rather than being
/// rejected — this is background work with nothing better to do, and a rejection
/// would only turn into a retry anyway.</para>
///
/// <para>The rate itself is a guess disciplined by evidence, not a published
/// figure: these endpoints are undocumented and returned no rate-limit headers
/// at all. See <see cref="SteamStoreOptions.RequestsPerSecond"/>.</para>
/// </summary>
public sealed class SteamStoreRateLimiter : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter;

    public SteamStoreRateLimiter(SteamStoreOptions options)
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
