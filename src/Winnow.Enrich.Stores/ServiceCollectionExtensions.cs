using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Polly.RateLimiting;
using Polly.Retry;
using Polly.Timeout;
using Winnow.Http;
using Winnow.Core.Repositories;

namespace Winnow.Enrich.Stores;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStorefrontEnrichment(
        this IServiceCollection services, Action<StorefrontTransportOptions>? configure = null)
    {
        var options = new StorefrontTransportOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<StorefrontCache>();
        services.TryAddSingleton<IStorefrontRepository>(sp => sp.GetRequiredService<StorefrontCache>());
        services.TryAddSingleton<StorefrontBudget>();
        services.AddTransient<StorefrontHandler>();
        services.AddHttpClient<StorefrontClient>(http =>
        {
            ProviderHttpTransport.Configure(http, options.MaxResponseBytes, options.OverallTimeout);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Winnow/1.0 (local game library; storefront metadata)");
        }).AddHttpMessageHandler<StorefrontHandler>();
        return services;
    }
}

public sealed class StorefrontTransportOptions
{
    public long MaxResponseBytes { get; set; } = 2 * 1024 * 1024;
    public TimeSpan AttemptTimeout { get; set; } = ProviderHttpTransport.DefaultAttemptTimeout;
    public TimeSpan OverallTimeout { get; set; } = ProviderHttpTransport.DefaultOverallTimeout;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>A conservative shared budget for both anonymous services; no vendor budget is published.</summary>
public sealed class StorefrontBudget : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter = new(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 1, TokensPerPeriod = 1, ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueLimit = 1000, QueueProcessingOrder = QueueProcessingOrder.OldestFirst, AutoReplenishment = true,
    });

    public ResiliencePipeline<HttpResponseMessage> CreatePipeline(
        TimeSpan? retryDelay = null, StorefrontTransportOptions? options = null)
        => new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddTimeout(options?.OverallTimeout ?? ProviderHttpTransport.DefaultOverallTimeout)
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 2,
                Delay = retryDelay ?? options?.RetryBaseDelay ?? TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>(exception => exception is not ProviderPayloadTooLargeException)
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(r => r.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int)r.StatusCode >= 500),
                DelayGenerator = args =>
                {
                    return ValueTask.FromResult(ProviderHttpTransport.GetRetryAfter(args.Outcome.Result, TimeSpan.FromSeconds(30)));
                },
                OnRetry = args => { args.Outcome.Result?.Dispose(); return default; },
            })
            .AddRateLimiter(new RateLimiterStrategyOptions
            {
                RateLimiter = args => _limiter.AcquireAsync(1, args.Context.CancellationToken),
            })
            .AddTimeout(options?.AttemptTimeout ?? ProviderHttpTransport.DefaultAttemptTimeout)
            .Build();

    public void Dispose() => _limiter.Dispose();
}

public sealed class StorefrontHandler(StorefrontBudget budget, StorefrontTransportOptions? options = null) : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline = budget.CreatePipeline(options: options);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => ProviderHttpTransport.SendAsync(_pipeline, request, (attempt, token) => base.SendAsync(attempt, token),
            options?.MaxResponseBytes ?? 2 * 1024 * 1024, ct);
}
