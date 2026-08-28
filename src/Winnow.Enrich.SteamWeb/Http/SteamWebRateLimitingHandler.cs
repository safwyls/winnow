namespace Winnow.Enrich.SteamWeb.Http;

/// <summary>
/// Applies <see cref="SteamWebRateLimiter"/> to every outbound request.
///
/// <para>Innermost handler, below retry, so a retried attempt spends a permit
/// too — a backoff storm must not be able to exceed the configured rate. This is
/// the only place in this module allowed to make a request wait; call sites
/// never sleep (charter, §4.2).</para>
/// </summary>
public sealed class SteamWebRateLimitingHandler : DelegatingHandler
{
    private readonly SteamWebRateLimiter _limiter;

    public SteamWebRateLimitingHandler(SteamWebRateLimiter limiter) => _limiter = limiter;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => await _limiter.Pipeline.ExecuteAsync(
            async token => await base.SendAsync(request, token), cancellationToken);
}
