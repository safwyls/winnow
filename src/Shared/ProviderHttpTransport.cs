using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Winnow.Http;

/// <summary>Linked into provider modules; shares mechanics without a dependency between them.</summary>
internal static class ProviderHttpTransport
{
    internal const long DefaultMaxResponseBytes = 16 * 1024 * 1024;
    internal const long MaxRequestBytes = 1024 * 1024;
    internal static readonly TimeSpan DefaultAttemptTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan DefaultOverallTimeout = TimeSpan.FromSeconds(90);

    internal static void Configure(HttpClient client, long maxResponseBytes, TimeSpan overallTimeout)
    {
        client.Timeout = overallTimeout;
        client.MaxResponseContentBufferSize = maxResponseBytes;
    }

    internal static ResiliencePipeline<HttpResponseMessage> CreatePipeline(
        ILogger log, string provider, Func<HttpStatusCode, bool> isTransient,
        int retries, TimeSpan retryDelay, TimeSpan maxRetryDelay,
        TimeSpan attemptTimeout, TimeSpan overallTimeout)
        => new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddTimeout(overallTimeout)
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(response => isTransient(response.StatusCode))
                    .Handle<HttpRequestException>(exception => exception is not ProviderPayloadTooLargeException)
                    .Handle<TimeoutException>()
                    .Handle<TimeoutRejectedException>(),
                MaxRetryAttempts = retries,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = retryDelay,
                MaxDelay = maxRetryDelay,
                DelayGenerator = args => ValueTask.FromResult(GetRetryAfter(args.Outcome.Result, maxRetryDelay)),
                OnRetry = args =>
                {
                    log.LogWarning("{Provider} request failed ({Outcome}); retry {Attempt} in {Delay}.",
                        provider, args.Outcome.Result is { } response
                            ? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                            : args.Outcome.Exception?.GetType().Name ?? "unknown",
                        args.AttemptNumber + 1, args.RetryDelay);
                    args.Outcome.Result?.Dispose();
                    return default;
                },
            })
            .AddTimeout(attemptTimeout)
            .Build();

    internal static async Task<HttpResponseMessage> SendAsync(
        ResiliencePipeline<HttpResponseMessage> pipeline, HttpRequestMessage request,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        long maxResponseBytes, CancellationToken ct)
    {
        var body = await BufferAsync(request, ct).ConfigureAwait(false);
        try
        {
            return await pipeline.ExecuteAsync(async token =>
            {
                using var attempt = Clone(request, body);
                var response = await send(attempt, token).ConfigureAwait(false);
                try
                {
                    // Buffer within the attempt budget, including responses whose
                    // Content-Length is absent or describes compressed bytes.
                    await response.Content.LoadIntoBufferAsync(maxResponseBytes, token).ConfigureAwait(false);
                    return response;
                }
                catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded)
                {
                    response.Dispose();
                    throw new ProviderPayloadTooLargeException(ex);
                }
                catch
                {
                    response.Dispose();
                    throw;
                }
            }, ct).ConfigureAwait(false);
        }
        catch (TimeoutRejectedException ex)
        {
            // Clients already soft-fail transport errors. Caller cancellation keeps
            // its original exception and is never converted into a retryable timeout.
            throw new HttpRequestException("Provider request exceeded its time budget.", ex);
        }
    }

    internal static async Task<byte[]?> BufferAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is null) return null;
        await request.Content.LoadIntoBufferAsync(MaxRequestBytes, ct).ConfigureAwait(false);
        return await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    internal static HttpRequestMessage Clone(HttpRequestMessage template, byte[]? body)
    {
        var clone = new HttpRequestMessage(template.Method, template.RequestUri)
        {
            Version = template.Version,
            VersionPolicy = template.VersionPolicy,
        };
        foreach (var header in template.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var option in template.Options)
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            if (template.Content is not null)
                foreach (var header in template.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }

    internal static TimeSpan? GetRetryAfter(HttpResponseMessage? response, TimeSpan maximum)
    {
        var header = response?.Headers.RetryAfter;
        var delay = header?.Delta ?? (header?.Date is { } date
            ? date - (response!.Headers.Date ?? DateTimeOffset.UtcNow) : (TimeSpan?)null);
        return delay is { } value ? value < TimeSpan.Zero ? TimeSpan.Zero : value > maximum ? maximum : value : null;
    }
}

internal sealed class ProviderPayloadTooLargeException(Exception inner)
    : HttpRequestException("Provider response exceeded its byte limit.", inner);
