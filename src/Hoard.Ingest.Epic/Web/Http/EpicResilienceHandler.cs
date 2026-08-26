using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Hoard.Ingest.Epic.Web.Http;

/// <summary>
/// Polly retry for the Epic pipeline: exponential backoff with jitter for
/// transient failures, and explicit 429 handling from the first commit.
///
/// <para><b>429 is handled without a <c>Retry-After</c> to lean on.</b> This is
/// the difference from the Steam pipeline, and it decides the whole design.
/// Steam answers a throttle with <c>Retry-After: 60–120</c>, so
/// <c>SteamWebResilienceHandler</c> can honour a server-stated delay. Epic sends
/// no <c>Retry-After</c> and no <c>X-RateLimit-*</c> at all — it returns
/// <c>x-epic-error-code</c> / <c>x-epic-correlation-id</c> instead — so there is
/// nothing to honour and the exponential schedule is the only thing standing
/// between a throttled sync and a hot loop. The header is still read and still
/// respected if Epic ever starts sending one; it is simply never relied
/// upon.</para>
///
/// <para>That Epic throttles at all is not speculative:
/// <c>errors.com.epicgames.common.throttled</c> is a real response, and
/// Legendary — the reference implementation for this flow — carries an open
/// report of an unhandled 429 crashing it on the launcher assets endpoint,
/// because it handles 503 and not 429. That crash is the cautionary tale this
/// handler exists to not repeat.</para>
///
/// <para><b>401 is deliberately NOT retried here.</b> It means the access token
/// is spent, which a delay does not fix;
/// <see cref="EpicAuthenticationHandler"/> owns that case and answers it with
/// exactly one refresh-and-retry. 403 is likewise not retried: it means the
/// token is not entitled, and the client soft-fails instead.</para>
///
/// <para><b>Nothing here logs a URI or a body.</b> The library path carries the
/// account id, the token request carries the client secret in its body, and both
/// requests carry a bearer credential in a header — so every message in this
/// file names the status code and nothing else.</para>
/// </summary>
public sealed class EpicResilienceHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public EpicResilienceHandler(EpicWebOptions options, ILogger<EpicResilienceHandler> log)
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
                    // Returning null defers to the exponential schedule, which is
                    // the path Epic actually takes today. A server-supplied
                    // Retry-After overrides it if one ever appears, capped so a
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
                        "Epic request failed ({Outcome}); retry {Attempt} in {Delay}.",
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
        // Unlike the Steam pipeline — whose every request is a bodyless GET —
        // this one carries form-encoded token requests, whose content stream the
        // first attempt consumes. Buffer once, rebuild per attempt.
        var body = await EpicRequestReplay.BufferAsync(request, cancellationToken);

        return await _pipeline.ExecuteAsync(
            async (state, token) =>
                await base.SendAsync(EpicRequestReplay.Clone(state.Request, state.Body), token),
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
    /// HTTP-date converted to a delay relative to the response's own <c>Date</c>
    /// header (falling back to local time) so clock skew between client and
    /// server cannot produce a negative or absurd wait.
    ///
    /// <para>Epic has not been observed sending this header. It is parsed anyway
    /// because the cost is nothing and the alternative — discovering the header
    /// exists only after Epic starts sending it and Hoard ignores it — is a
    /// throttle Hoard would keep walking into.</para>
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
