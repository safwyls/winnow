using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Winnow.Enrich.Steam.Http;

/// <summary>
/// Polly retry for the store pipeline: exponential backoff with jitter for
/// transient failures, and explicit <c>Retry-After</c> honouring for 429 — what
/// §4.2/§4.3 require from the first commit.
///
/// <para>The spike saw no 429 across 8 batched requests. That is a sample, not a
/// guarantee: these endpoints publish no rate-limit headers and no documented
/// budget, so the backoff ships now rather than after the first throttle.</para>
/// </summary>
public sealed class SteamStoreResilienceHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public SteamStoreResilienceHandler(SteamStoreOptions options, ILogger<SteamStoreResilienceHandler> log)
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
                    // hostile or mistaken header cannot stall a backfill for hours.
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
                        "Steam store request failed ({Outcome}); retry {Attempt} in {Delay}.",
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
            async (template, token) =>
            {
                // A fresh message per attempt: an HttpRequestMessage is not
                // guaranteed re-sendable once a handler has consumed it. These
                // requests are bodiless GETs, so the clone is header-only.
                using var attempt = Clone(template);
                return await base.SendAsync(attempt, token);
            },
            request,
            cancellationToken);

    private static HttpRequestMessage Clone(HttpRequestMessage template)
    {
        var clone = new HttpRequestMessage(template.Method, template.RequestUri)
        {
            Version = template.Version,
            VersionPolicy = template.VersionPolicy,
        };

        foreach (var header in template.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in (IDictionary<string, object?>)template.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        return clone;
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
    /// header (falling back to local time) so clock skew cannot produce a
    /// negative or absurd wait.
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
