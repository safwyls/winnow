using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Winnow.Enrich.SteamWeb.Http;

/// <summary>
/// Polly retry for the Steam Web API pipeline: exponential backoff with jitter
/// for transient failures, and explicit <c>Retry-After</c> honouring for 429.
/// 403 is not retried; nothing here logs a URI.
/// </summary>
public sealed class SteamWebResilienceHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public SteamWebResilienceHandler(SteamWebOptions options, ILogger<SteamWebResilienceHandler> log)
    {
        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(static response => IsTransient(response.StatusCode))
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>(),
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.RetryBaseDelay,
                MaxDelay = options.MaxRetryDelay,
                DelayGenerator = args =>
                {
                    // Returning null defers to the exponential schedule; a
                    // server-supplied Retry-After overrides it, capped so a
                    // hostile or mistaken header cannot stall a sync for hours.
                    var retryAfter = args.Outcome.Result is { } response
                        ? GetRetryAfter(response)
                        : null;

                    return ValueTask.FromResult(retryAfter is { } delay
                        ? delay > options.MaxRetryDelay ? options.MaxRetryDelay : delay
                        : (TimeSpan?)null);
                },
                OnRetry = args =>
                {
                    log.LogWarning(
                        "Steam Web API request failed ({Outcome}); retry {Attempt} in {Delay}.",
                        args.Outcome.Result is { } response
                            ? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                            : args.Outcome.Exception?.GetType().Name ?? "unknown",
                        args.AttemptNumber + 1,
                        args.RetryDelay);

                    // The failed response is about to be replaced; without this
                    // its connection is only released at GC time.
                    args.Outcome.Result?.Dispose();
                    return default;
                },
            })
            .Build();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // S6 put form POSTs on this pipeline (the session renewal exchange),
        // and a spent content stream cannot be replayed, so the buffer-and-clone
        // the IGDB pipeline already does is done here too. A GET buffers to null
        // and clones to an equivalent message, so the two typed API clients see
        // no change.
        var body = await SteamRequestReplay.BufferAsync(request, cancellationToken);

        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                var attempt = SteamRequestReplay.Clone(state.Request, state.Body);
                return await base.SendAsync(attempt, token);
            },
            (Request: request, Body: body),
            cancellationToken);
    }

    // 401 is deliberately absent. S6 depends on that: an unauthorized answer is
    // Steam's verdict on the credential rather than a blip, so it must reach the
    // call site, which renews the session once and re-sends. Retrying it here
    // would spend the request budget re-asking the same question with the same
    // dead token.
    private static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    /// <summary>
    /// <c>Retry-After</c> in either documented form: delta-seconds, or an
    /// HTTP-date that must be converted to a delay relative to the response's own
    /// <c>Date</c> header (falling back to local time) so clock skew between
    /// client and server does not produce a negative or absurd wait.
    /// </summary>
    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        if (retryAfter.Date is { } date)
        {
            var reference = response.Headers.Date ?? DateTimeOffset.UtcNow;
            var wait = date - reference;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }
}
