using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Winnow.Http;

namespace Winnow.Enrich.Updates.Http;

/// <summary>Provider retry policy; absence and invalid requests are not retried.</summary>
public abstract class UpdateSignalResilienceHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;
    private readonly long _maxResponseBytes;

    protected UpdateSignalResilienceHandler(string host, UpdateSignalOptions options, ILogger logger)
    {
        _maxResponseBytes = options.MaxResponseBytes;
        _pipeline = ProviderHttpTransport.CreatePipeline(logger, host, IsTransient,
            options.MaxRetryAttempts, options.RetryBaseDelay, options.MaxRetryDelay,
            options.AttemptTimeout, options.OverallTimeout);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => ProviderHttpTransport.SendAsync(_pipeline, request, (attempt, token) => base.SendAsync(attempt, token),
            _maxResponseBytes, ct);

    // 403 means no Steam news feed, not throttling.
    internal static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}

public sealed class SteamNewsResilienceHandler(UpdateSignalOptions options, ILogger<SteamNewsResilienceHandler> logger)
    : UpdateSignalResilienceHandler("Steam news", options, logger);

public sealed class BuildInfoResilienceHandler(UpdateSignalOptions options, ILogger<BuildInfoResilienceHandler> logger)
    : UpdateSignalResilienceHandler("steamcmd.net", options, logger);