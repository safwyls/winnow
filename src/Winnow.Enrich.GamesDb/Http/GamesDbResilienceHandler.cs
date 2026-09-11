using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Winnow.Http;

namespace Winnow.Enrich.GamesDb.Http;

/// <summary>Provider retry policy; the inner limiter accounts for every attempted send.</summary>
public sealed class GamesDbResilienceHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;
    private readonly long _maxResponseBytes;

    public GamesDbResilienceHandler(GamesDbOptions options, ILogger<GamesDbResilienceHandler> log)
    {
        _maxResponseBytes = options.MaxResponseBytes;
        _pipeline = ProviderHttpTransport.CreatePipeline(log, "gamesdb", IsTransient,
            options.MaxRetryAttempts, options.RetryBaseDelay, options.MaxRetryDelay,
            options.AttemptTimeout, options.OverallTimeout);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => ProviderHttpTransport.SendAsync(_pipeline, request, (attempt, token) => base.SendAsync(attempt, token),
            _maxResponseBytes, ct);

    // Auth rejection, absence and invalid requests are provider answers, not transient failures.
    internal static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}