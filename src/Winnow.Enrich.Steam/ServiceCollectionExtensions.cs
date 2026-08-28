using Winnow.Enrich.Steam.Http;
using Winnow.Enrich.Steam.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Winnow.Enrich.Steam;

/// <summary>
/// Composition for the Steam store module. The host's composition root calls
/// <see cref="AddSteamStoreEnrichment"/>; nothing outside this assembly needs to
/// know which handlers, limiters or stores are involved.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISteamStoreClient"/> and everything under it.
    ///
    /// <para>No credentials, no configuration, no setup step: both endpoints are
    /// keyless (verified live — <c>IStoreService/GetAppList</c> 403s without a
    /// key while <c>GetItems</c> returns 200), so there is no "not configured"
    /// state to handle. That is the entire reason this module exists alongside
    /// IGDB.</para>
    ///
    /// <para>Storage defaults to the SQLite-backed <c>metadata_cache</c> table
    /// and therefore expects an <c>ISqliteConnectionFactory</c> in the container.
    /// Register an <see cref="IStoreMetadataCache"/> of your own beforehand to
    /// override — every registration here is <c>TryAdd</c>.</para>
    ///
    /// <para>Handler order on the pipeline is deliberate and outermost first:
    /// retry (owns 429/5xx backoff and <c>Retry-After</c>) → rate limiter (owns
    /// the request budget). The limiter sits innermost so retried attempts spend
    /// permits like any other request and a backoff storm cannot exceed the
    /// configured rate.</para>
    /// </summary>
    public static IServiceCollection AddSteamStoreEnrichment(this IServiceCollection services)
        => services.AddSteamStoreEnrichment(configure: null);

    /// <inheritdoc cref="AddSteamStoreEnrichment(IServiceCollection)"/>
    /// <param name="services">The container.</param>
    /// <param name="configure">Overrides for <see cref="SteamStoreOptions"/>.</param>
    public static IServiceCollection AddSteamStoreEnrichment(
        this IServiceCollection services, Action<SteamStoreOptions>? configure)
    {
        var options = new SteamStoreOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IStoreMetadataCache, SqliteStoreMetadataCache>();

        services.TryAddSingleton<SteamStoreRateLimiter>();
        services.TryAddTransient<SteamStoreResilienceHandler>();
        services.TryAddTransient<SteamStoreRateLimitingHandler>();

        services.AddHttpClient<ISteamStoreClient, SteamStoreClient>(client =>
            {
                client.BaseAddress = options.BaseAddress;

                // §4.3: a descriptive User-Agent so Valve can attribute — and if
                // necessary contact — this traffic.
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
            })
            .AddHttpMessageHandler<SteamStoreResilienceHandler>()
            .AddHttpMessageHandler<SteamStoreRateLimitingHandler>();

        return services;
    }
}
