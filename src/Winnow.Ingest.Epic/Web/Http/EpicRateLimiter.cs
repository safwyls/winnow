using System.Threading.RateLimiting;
using Polly;
using Polly.RateLimiting;

namespace Winnow.Ingest.Epic.Web.Http;

/// <summary>
/// Singleton token-bucket rate limiter shared by all Epic HTTP clients, keeping
/// total outbound traffic within <see cref="EpicWebOptions.RequestsPerSecond"/>.
/// </summary>
public sealed class EpicRateLimiter : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter;

    public EpicRateLimiter(EpicWebOptions options)
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

/// <summary>Applies <see cref="EpicRateLimiter"/> to every outbound request. Innermost handler.</summary>
public sealed class EpicRateLimitingHandler : DelegatingHandler
{
    private readonly EpicRateLimiter _limiter;

    public EpicRateLimitingHandler(EpicRateLimiter limiter) => _limiter = limiter;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => await _limiter.Pipeline.ExecuteAsync(
            async token => await base.SendAsync(request, token), cancellationToken);
}
