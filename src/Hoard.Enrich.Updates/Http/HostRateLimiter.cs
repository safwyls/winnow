using System.Threading.RateLimiting;
using Polly;
using Polly.RateLimiting;

namespace Hoard.Enrich.Updates.Http;

/// <summary>
/// One gate in front of one host. Registered as a singleton per host so every
/// typed client, background pass and retry attempt draws from the same budget —
/// a per-client limiter would multiply the ceiling by the number of clients and
/// get the IP throttled.
///
/// <para>Two subclasses rather than one shared limiter because the two hosts are
/// not comparable: api.steampowered.com is Valve's, documented, with a ~100k/day
/// nominal budget; api.steamcmd.net is a volunteer mirror with no SLA and no
/// published limit at all. Sharing one bucket would let a news sweep spend the
/// courtesy budget owed to the volunteer service, and vice versa.</para>
///
/// <para>Token bucket rather than a fixed window: a batch is allowed a short
/// burst while the long-run average holds. <c>QueueLimit</c> is effectively
/// unbounded so callers <i>wait</i> for a permit rather than being rejected —
/// this is background work with nothing better to do, and a rejection would only
/// become a retry anyway.</para>
/// </summary>
public abstract class HostRateLimiter : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter;

    protected HostRateLimiter(int requestsPerSecond)
    {
        var permits = Math.Max(1, requestsPerSecond);
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

/// <summary>The api.steampowered.com budget. See <see cref="UpdateSignalOptions.NewsRequestsPerSecond"/>.</summary>
public sealed class SteamNewsRateLimiter : HostRateLimiter
{
    public SteamNewsRateLimiter(UpdateSignalOptions options)
        : base(options.NewsRequestsPerSecond)
    {
    }
}

/// <summary>
/// The api.steamcmd.net budget, deliberately the tighter of the two. See
/// <see cref="UpdateSignalOptions.BuildInfoRequestsPerSecond"/>.
/// </summary>
public sealed class BuildInfoRateLimiter : HostRateLimiter
{
    public BuildInfoRateLimiter(UpdateSignalOptions options)
        : base(options.BuildInfoRequestsPerSecond)
    {
    }
}
