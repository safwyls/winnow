using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Hoard.Covers;

/// <summary>Composition root hook for the cover cache. The app wires this; the cache wires itself.</summary>
public static class CoverCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers the cover pipeline: Steam's portrait capsule as the first
    /// source, the <c>%LOCALAPPDATA%\Hoard\covers\</c> disk cache, and the
    /// bounded in-memory <see cref="ICoverCache"/>. Registering another
    /// <see cref="ICoverSource"/> afterwards makes it the gap-filler for keys
    /// Steam declines — that is where IGDB covers plug in, with no dependency
    /// here on IGDB or any credential.
    /// </summary>
    public static IServiceCollection AddCoverCache(this IServiceCollection services)
        => services.AddCoverCache(null);

    /// <inheritdoc cref="AddCoverCache(IServiceCollection)"/>
    public static IServiceCollection AddCoverCache(
        this IServiceCollection services,
        Action<CoverCacheOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(_ =>
        {
            var options = new CoverCacheOptions();
            configure?.Invoke(options);
            return options;
        });

        services.AddCoverImageHttpClient(SteamCapsuleSource.HttpClientName);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICoverSource, SteamCapsuleSource>());
        services.TryAddSingleton<CoverDiskCache>();
        services.TryAddSingleton<CoverPipeline>();
        services.TryAddSingleton<ICoverCache, CoverCache>();

        return services;
    }

    /// <summary>
    /// A named <see cref="HttpClient"/> for pulling cover art off a public,
    /// unauthenticated image CDN: identifying User-Agent, 30 second timeout, and
    /// the retry policy below. Shared so that a cover source added outside this
    /// assembly — <c>Hoard.Covers.Igdb</c> — inherits the conventions rather
    /// than approximating them.
    /// </summary>
    public static IServiceCollection AddCoverImageHttpClient(this IServiceCollection services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddHttpClient(name, client =>
            {
                // Descriptive and contactable: these are unauthenticated public
                // CDNs and we would rather be identified than rate-limited.
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Hoard/0.1 (+https://github.com/hoard-app/hoard; local-first game library manager)");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddResilienceHandler(name, builder => builder
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(400),
                    // 404 is an answer, not a failure: most Steam "apps" that
                    // are tools or redistributables will never have a capsule,
                    // and retrying them three times each would triple the cost
                    // of the misses. Only transport and server-side faults retry.
                    //
                    // 403 DOES retry. The capsule CDN is unauthenticated, so a
                    // Forbidden is never a statement about this appid — it is a
                    // WAF or edge node refusing traffic, usually the burst a
                    // cold library produces on first launch. Retrying with
                    // backoff is exactly right for that, and a 403 that survives
                    // the retries surfaces as a failure rather than as "this
                    // game has no art" (SteamCapsuleSource).
                    ShouldHandle = args => ValueTask.FromResult(
                        args.Outcome.Exception is HttpRequestException or TaskCanceledException
                        || args.Outcome.Result is
                        {
                            StatusCode: HttpStatusCode.RequestTimeout
                                or HttpStatusCode.TooManyRequests
                                or HttpStatusCode.Forbidden,
                        }
                        || (int?)args.Outcome.Result?.StatusCode >= 500),
                }));

        return services;
    }
}
