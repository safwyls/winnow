namespace Winnow.Enrich.Steam.Http;

/// <summary>
/// Applies <see cref="SteamStoreRateLimiter"/> to every outbound request.
///
/// <para>Innermost handler, below retry, so a retried attempt spends a permit
/// too — a backoff storm must not be able to exceed the configured rate. This is
/// the only place in this module allowed to make a request wait; call sites
/// never sleep (charter).</para>
/// </summary>
public sealed class SteamStoreRateLimitingHandler : DelegatingHandler
{
    private readonly SteamStoreRateLimiter _limiter;

    public SteamStoreRateLimitingHandler(SteamStoreRateLimiter limiter) => _limiter = limiter;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => await _limiter.Pipeline.ExecuteAsync(
            async token => await base.SendAsync(request, token), cancellationToken);
}
