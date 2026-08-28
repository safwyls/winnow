using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Winnow.Enrich.Igdb.Http;

/// <summary>
/// Polly retry for the IGDB and Twitch pipelines: exponential backoff with
/// jitter for transient failures, and explicit <c>Retry-After</c> honouring for
/// 429 — the behaviour §4.2/§4.4 require from the first commit.
///
/// <para>401 is deliberately <b>not</b> retried here.
/// <see cref="IgdbAuthenticationHandler"/> owns that case, because the fix is a
/// new token rather than a delay; retrying the same expired bearer three times
/// would just waste the rate-limit budget.</para>
/// </summary>
public sealed class IgdbResilienceHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public IgdbResilienceHandler(IgdbOptions options, ILogger<IgdbResilienceHandler> log)
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
                    // hostile or mistaken header cannot stall the app for hours.
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
                        "IGDB request failed ({Outcome}); retry {Attempt} in {Delay}.",
                        args.Outcome.Result is { } response
                            ? ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)
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
        var body = await RequestReplay.BufferAsync(request, cancellationToken);

        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                // A fresh message per attempt: the previous attempt's content
                // stream is spent (see RequestReplay).
                var attempt = RequestReplay.Clone(state.Request, state.Body);
                return await base.SendAsync(attempt, token);
            },
            (Request: request, Body: body),
            cancellationToken);
    }

    private static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    /// <summary>
    /// <c>Retry-After</c> in either documented form: delta-seconds, or an
    /// HTTP-date that must be converted to a delay relative to the response's
    /// own <c>Date</c> header (falling back to local time) so clock skew between
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
