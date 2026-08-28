using System.Threading.RateLimiting;
using Polly;
using Polly.RateLimiting;

namespace Winnow.Ingest.Epic.Web.Http;

/// <summary>
/// The single gate in front of every authenticated Epic request. Registered as a
/// singleton so the token endpoint, the library endpoint and the playtime
/// endpoint all draw from one budget — a per-client limiter would multiply the
/// ceiling by the number of typed clients, which is exactly how an account walks
/// into a throttle it never intended to approach.
///
/// <para>Token bucket rather than a fixed window: a short burst is allowed —
/// which is what paginating a library actually looks like — while the long-run
/// average stays at <see cref="EpicWebOptions.RequestsPerSecond"/>.
/// <c>QueueLimit</c> is effectively unbounded so callers <i>wait</i> for a
/// permit rather than being rejected; this is background work with nothing
/// better to do, and a rejection would only turn into a retry anyway.</para>
///
/// <para><b>Why a limiter at all when Epic publishes no limit.</b> Precisely
/// because it publishes none. Epic sends no <c>X-RateLimit-*</c> headers and no
/// <c>Retry-After</c>, so there is no signal to react to and no budget to
/// compute against — the only available strategy is to stay well below anything
/// plausible. Epic does throttle: <c>errors.com.epicgames.common.throttled</c>
/// is a real response, and Legendary carries an open crash report from a 429 on
/// the launcher assets endpoint. Handling the 429 is
/// <see cref="EpicResilienceHandler"/>'s job; this limiter is the cheap upstream
/// guard that stops Winnow walking into it.</para>
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

/// <summary>
/// Applies <see cref="EpicRateLimiter"/> to every outbound request.
///
/// <para>Innermost handler, below retry, so a retried attempt spends a permit
/// too — a backoff storm must not be able to exceed the configured rate. This is
/// the only place in this module allowed to make a request wait; call sites
/// never sleep.</para>
/// </summary>
public sealed class EpicRateLimitingHandler : DelegatingHandler
{
    private readonly EpicRateLimiter _limiter;

    public EpicRateLimitingHandler(EpicRateLimiter limiter) => _limiter = limiter;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => await _limiter.Pipeline.ExecuteAsync(
            async token => await base.SendAsync(request, token), cancellationToken);
}
