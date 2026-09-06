using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Polly.RateLimiting;
using Polly.Retry;
using Winnow.Core.Repositories;

namespace Winnow.Enrich.Stores;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStorefrontEnrichment(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<StorefrontCache>();
        services.TryAddSingleton<IStorefrontRepository>(sp => sp.GetRequiredService<StorefrontCache>());
        services.TryAddSingleton<StorefrontBudget>();
        services.AddTransient<StorefrontHandler>();
        services.AddHttpClient<StorefrontClient>(http =>
        {
            http.Timeout = TimeSpan.FromSeconds(90);
            http.MaxResponseContentBufferSize = 2 * 1024 * 1024;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Winnow/1.0 (local game library; storefront metadata)");
        }).AddHttpMessageHandler<StorefrontHandler>();
        return services;
    }
}

/// <summary>A conservative shared budget for both anonymous services; no vendor budget is published.</summary>
public sealed class StorefrontBudget : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter = new(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 1, TokensPerPeriod = 1, ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueLimit = 1000, QueueProcessingOrder = QueueProcessingOrder.OldestFirst, AutoReplenishment = true,
    });

    public ResiliencePipeline<HttpResponseMessage> CreatePipeline(TimeSpan? retryDelay = null)
        => new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 2,
                Delay = retryDelay ?? TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>().Handle<HttpRequestException>()
                    .HandleResult(r => r.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int)r.StatusCode >= 500),
                DelayGenerator = args =>
                {
                    var header = args.Outcome.Result?.Headers.RetryAfter;
                    var delay = header?.Delta ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
                    return ValueTask.FromResult(delay is { } d ? (TimeSpan?)TimeSpan.FromSeconds(Math.Clamp(d.TotalSeconds, 0, 30)) : null);
                },
                OnRetry = args => { args.Outcome.Result?.Dispose(); return default; },
            })
            .AddRateLimiter(new RateLimiterStrategyOptions
            {
                RateLimiter = args => _limiter.AcquireAsync(1, args.Context.CancellationToken),
            }).Build();

    public void Dispose() => _limiter.Dispose();
}

public sealed class StorefrontHandler(StorefrontBudget budget) : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline = budget.CreatePipeline();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => await _pipeline.ExecuteAsync(async token =>
        {
            using var attempt = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers) attempt.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return await base.SendAsync(attempt, token);
        }, ct);
}
