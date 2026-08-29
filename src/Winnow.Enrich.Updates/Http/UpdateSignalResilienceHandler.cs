using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Winnow.Enrich.Updates.Http;

/// <summary>
/// Polly retry for update-signal pipelines: exponential backoff with jitter for
/// transient failures, <c>Retry-After</c> honouring for 429. 403 is deliberately
/// NOT retried -- it means "no news feed" for that appid, not rate limiting.
/// </summary>
public abstract class UpdateSignalResilienceHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    protected UpdateSignalResilienceHandler(
        string host, UpdateSignalOptions options, ILogger logger)
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
                        "{Host} request failed ({Outcome}); retry {Attempt} in {Delay}.",
                        host,
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

    /// <summary>Allow-list of statuses worth retrying. 403, 404, 422 are absent on purpose.</summary>
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

    /// <summary>Parses <c>Retry-After</c> as delta-seconds or HTTP-date.</summary>
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

/// <summary>Retry policy for <c>ISteamNews/GetNewsForApp</c>. 403 is not retried; see the base type.</summary>
public sealed class SteamNewsResilienceHandler : UpdateSignalResilienceHandler
{
    public SteamNewsResilienceHandler(UpdateSignalOptions options, ILogger<SteamNewsResilienceHandler> logger)
        : base("Steam news", options, logger)
    {
    }
}

/// <summary>Retry policy for api.steamcmd.net.</summary>
public sealed class BuildInfoResilienceHandler : UpdateSignalResilienceHandler
{
    public BuildInfoResilienceHandler(UpdateSignalOptions options, ILogger<BuildInfoResilienceHandler> logger)
        : base("steamcmd.net", options, logger)
    {
    }
}
