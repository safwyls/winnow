namespace Winnow.Enrich.Updates.Http;

/// <summary>
/// Applies a <see cref="HostRateLimiter"/> to every outbound request.
///
/// <para>Innermost handler, below retry, so a retried attempt spends a permit
/// too — a backoff storm must not be able to exceed the configured rate. This is
/// the only place in this module allowed to make a request wait; call sites
/// never sleep (charter).</para>
/// </summary>
public abstract class RateLimitingHandler : DelegatingHandler
{
    private readonly HostRateLimiter _limiter;

    protected RateLimitingHandler(HostRateLimiter limiter) => _limiter = limiter;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => await _limiter.Pipeline.ExecuteAsync(
            async token => await base.SendAsync(request, token), cancellationToken);
}

/// <summary>Rate gate for <c>ISteamNews/GetNewsForApp</c>.</summary>
public sealed class SteamNewsRateLimitingHandler : RateLimitingHandler
{
    public SteamNewsRateLimitingHandler(SteamNewsRateLimiter limiter)
        : base(limiter)
    {
    }
}

/// <summary>Rate gate for api.steamcmd.net.</summary>
public sealed class BuildInfoRateLimitingHandler : RateLimitingHandler
{
    public BuildInfoRateLimitingHandler(BuildInfoRateLimiter limiter)
        : base(limiter)
    {
    }
}
