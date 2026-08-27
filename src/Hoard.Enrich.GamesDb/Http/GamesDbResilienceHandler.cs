using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Hoard.Enrich.GamesDb.Http;

/// <summary>
/// Polly retry for gamesdb.gog.com: exponential backoff with jitter for
/// genuinely transient failures, and explicit <c>Retry-After</c> honouring for
/// 429 — the same policy shape §4.2 requires of every Steam client, applied here
/// from the first commit rather than after the first throttling.
///
/// <para><b>404 is deliberately not retried, and that is the important line in
/// this file.</b> A 404 from this endpoint means "gamesdb has no release under
/// that platform and id" — a fact about the game, which is a normal answer for
/// an Epic exclusive or a title GOG's graph has never indexed. Retrying it
/// spends four requests and a growing backoff to re-learn the same nothing, on
/// an unpublished endpoint, for every unmatchable title in the library. The
/// client turns a 404 into a cached miss instead.</para>
///
/// <para>There is no circuit breaker, for the reason the update-signal module
/// records: a breaker keyed on "failures" lets one permanently-absent id
/// suppress the sweep for every other title. The only status that slows anything
/// down here is 429.</para>
/// </summary>
public sealed class GamesDbResilienceHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public GamesDbResilienceHandler(GamesDbOptions options, ILogger<GamesDbResilienceHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

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
                    // hostile or mistaken header cannot stall a pass for hours.
                    var retryAfter = args.Outcome.Result is { } response
                        ? GetRetryAfter(response)
                        : null;

                    return ValueTask.FromResult(retryAfter is { } delay
                        ? delay > options.MaxRetryDelay ? options.MaxRetryDelay : delay
                        : (TimeSpan?)null);
                },
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "gamesdb request failed ({Outcome}); retry {Attempt} in {Delay}.",
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
                // are bodiless GETs, so the clone is header-only.
                using var attempt = Clone(template);
                return await base.SendAsync(attempt, token);
            },
            request,
            cancellationToken);

    /// <summary>
    /// The complete set of statuses worth trying again. An allow-list, not a
    /// deny-list, so a status nobody thought about defaults to "give up and let
    /// the caller decide" rather than to "hammer it three more times".
    ///
    /// <para><b>404 is absent on purpose</b> — see the type remarks.</para>
    /// </summary>
    internal static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

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
