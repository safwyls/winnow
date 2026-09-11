using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Polly.RateLimiting;
using Polly.Retry;

namespace Winnow.Enrich.SteamGridDb;

/// <summary>One process-wide polite request budget; every retry spends a permit.</summary>
public sealed class SteamGridDbResilience : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter = new(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 1, TokensPerPeriod = 1, ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        AutoReplenishment = true, QueueLimit = 1000, QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });

    public SteamGridDbResilience(SteamGridDbOptions options)
    {
        Pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = options.RetryDelay,
                MaxDelay = options.MaxRetryDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>().Handle<HttpRequestException>()
                    .HandleResult(response => response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
                        || (int)response.StatusCode >= 500),
                DelayGenerator = args => ValueTask.FromResult(RetryAfter(args.Outcome.Result, options.MaxRetryDelay)),
                OnRetry = args => { args.Outcome.Result?.Dispose(); return default; },
            })
            .AddRateLimiter(new RateLimiterStrategyOptions
            {
                RateLimiter = args => _limiter.AcquireAsync(1, args.Context.CancellationToken),
            }).Build();
    }

    public ResiliencePipeline<HttpResponseMessage> Pipeline { get; }

    public static TimeSpan? RetryAfter(HttpResponseMessage? response, TimeSpan maximum)
    {
        var header = response?.Headers.RetryAfter;
        var delay = header?.Delta ?? (header?.Date is { } date ? date - (response!.Headers.Date ?? DateTimeOffset.UtcNow) : null);
        return delay is null ? null : delay < TimeSpan.Zero ? TimeSpan.Zero : delay > maximum ? maximum : delay;
    }

    public void Dispose() => _limiter.Dispose();
}

public sealed class SteamGridDbHttpHandler(SteamGridDbResilience resilience) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => await resilience.Pipeline.ExecuteAsync(async token =>
        {
            using var attempt = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers) attempt.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return await base.SendAsync(attempt, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
}

public static class SteamGridDbServiceCollectionExtensions
{
    public static IServiceCollection AddSteamGridDb(this IServiceCollection services, Action<SteamGridDbOptions>? configure = null)
    {
        services.TryAddSingleton(_ => { var options = new SteamGridDbOptions(); configure?.Invoke(options); return options; });
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.TryAddSingleton<ISteamGridDbSecretProtector, SteamGridDbSecretProtector>();
        services.TryAddSingleton<SteamGridDbSettingsStore>();
        services.TryAddSingleton<ISteamGridDbSettingsStore>(sp => sp.GetRequiredService<SteamGridDbSettingsStore>());
        services.TryAddSingleton<ISteamGridDbKeyProvider>(sp => sp.GetRequiredService<SteamGridDbSettingsStore>());
        services.TryAddSingleton<SteamGridDbAvailability>();
        services.TryAddSingleton<SteamGridDbResilience>();
        services.AddTransient<SteamGridDbHttpHandler>();
        services.AddHttpClient<ISteamGridDbClient, SteamGridDbClient>(SteamGridDbClient.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(90);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Winnow/0.1 (+https://github.com/winnow-app/winnow)");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .AddHttpMessageHandler<SteamGridDbHttpHandler>()
            .RedactLoggedHeaders(["Authorization"]);
        return services;
    }
}
