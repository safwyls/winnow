using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Winnow.Http;

namespace Winnow.Enrich.Igdb.Http;

/// <summary>Provider retry policy; the inner limiter accounts for every attempted send.</summary>
public sealed class IgdbResilienceHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;
    private readonly long _maxResponseBytes;

    public IgdbResilienceHandler(IgdbOptions options, ILogger<IgdbResilienceHandler> log)
    {
        _maxResponseBytes = options.MaxResponseBytes;
        _pipeline = ProviderHttpTransport.CreatePipeline(log, "IGDB", IsTransient,
            options.MaxRetryAttempts, options.RetryBaseDelay, options.MaxRetryDelay,
            options.AttemptTimeout, options.OverallTimeout);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => ProviderHttpTransport.SendAsync(_pipeline, request, (attempt, token) => base.SendAsync(attempt, token),
            _maxResponseBytes, ct);

    // Auth rejection, absence and invalid requests are provider answers, not transient failures.
    private static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}