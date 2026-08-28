namespace Winnow.Enrich.Igdb.Http;

/// <summary>
/// Applies <see cref="IgdbRateLimiter"/> to every outbound request.
///
/// <para>Innermost handler, below retry, so a retried attempt spends a permit
/// too — a backoff storm must not be able to exceed 4 req/s. This is the only
/// place in Winnow allowed to make an IGDB call wait; call sites never sleep.</para>
/// </summary>
public sealed class IgdbRateLimitingHandler : DelegatingHandler
{
    private readonly IgdbRateLimiter _limiter;

    public IgdbRateLimitingHandler(IgdbRateLimiter limiter) => _limiter = limiter;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => await _limiter.Pipeline.ExecuteAsync(
            async token => await base.SendAsync(request, token), cancellationToken);
}
