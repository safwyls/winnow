using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Winnow.Enrich.SteamWeb.Http;

/// <summary>
/// Polly retry for the Steam Web API pipeline: exponential backoff with jitter
/// for transient failures, and explicit <c>Retry-After</c> honouring for 429 —
/// which §4.2 requires from the first commit, not after the first throttle.
///
/// <para>§4.2: since June 2025 Steam throttles profile endpoints aggressively,
/// answering 429 with a <c>Retry-After</c> of 60–120 s.
/// <see cref="SteamWebOptions.MaxRetryDelay"/> is set high enough to honour the
/// top of that range and no higher, so a mistaken or hostile header cannot park
/// a sync for hours.</para>
///
/// <para>403 is deliberately <b>not</b> retried. On this API it means the key is
/// missing, wrong, or not entitled to the profile — none of which a delay fixes,
/// and all of which the client soft-fails on instead.</para>
///
/// <para><b>Nothing here logs a URI.</b> The <c>key</c> parameter travels in the
/// query string (<c>GetOwnedGames</c> offers no alternative), so every message
/// in this file names the status code and nothing else. See
/// <see cref="RedactingHttpClientLogger"/> for why the framework's own request
/// logging is removed rather than trusted.</para>
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
        => await _pipeline.ExecuteAsync(
            // Every request this module sends is a bodyless GET, so the message
            // is replayable as-is and needs none of the buffer-and-clone dance
            // the IGDB pipeline does for its text/plain Apicalypse bodies.
            async (state, token) => await base.SendAsync(state, token),
            request,
            cancellationToken);

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
