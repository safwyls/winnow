using Winnow.Core.Ingest;
using Winnow.Enrich.GamesDb.Http;
using Winnow.Enrich.GamesDb.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Winnow.Enrich.GamesDb;

/// <summary>
/// Composition for the gamesdb identity graph. The host's composition root calls
/// <see cref="AddGamesDbIdentityGraph"/>; nothing outside this assembly needs to
/// know which handlers, limiters or stores are involved.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IGameIdentityGraph"/> and everything under it.
    ///
    /// <para>Storage defaults to the SQLite-backed <c>metadata_cache</c> table
    /// and therefore expects an <c>ISqliteConnectionFactory</c> in the
    /// container. Register an <see cref="IGamesDbCache"/> of your own beforehand
    /// to override — every registration here is <c>TryAdd</c>.</para>
    ///
    /// <para>Handler order is deliberate and outermost first: retry (owns
    /// 429/5xx backoff and <c>Retry-After</c>) → rate limiter (owns the request
    /// budget). The limiter sits innermost so retried attempts spend permits
    /// like any other request. There is no auth handler: the endpoint is
    /// unauthenticated, which is most of why it is usable at all.</para>
    ///
    /// <para>No <see cref="IStoreArtifactAliasSource"/> is registered here. A
    /// host that wants the Epic route registers the ingest module that supplies
    /// the aliases; without one, Epic simply has no lookup key and is skipped,
    /// which is the correct behaviour on a machine with no Epic launcher.</para>
    /// </summary>
    public static IServiceCollection AddGamesDbIdentityGraph(
        this IServiceCollection services, Action<GamesDbOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new GamesDbOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IGamesDbCache, SqliteGamesDbCache>();
        services.TryAddSingleton<GamesDbRateLimiter>();
        services.TryAddTransient<GamesDbResilienceHandler>();
        services.TryAddTransient<GamesDbRateLimitingHandler>();

        services.AddHttpClient<IGameIdentityGraph, GamesDbClient>(client =>
            {
                client.BaseAddress = options.BaseAddress;
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
            })
            .AddHttpMessageHandler<GamesDbResilienceHandler>()
            .AddHttpMessageHandler<GamesDbRateLimitingHandler>();

        return services;
    }
}
