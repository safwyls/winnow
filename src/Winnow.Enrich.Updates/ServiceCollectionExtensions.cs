using Winnow.Enrich.Updates.Http;
using Winnow.Enrich.Updates.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Winnow.Enrich.Updates;

/// <summary>
/// Composition for the update-signal module. The host's composition root calls
/// <see cref="AddUpdateSignals(IServiceCollection)"/>; nothing outside this
/// assembly needs to know which handlers, limiters or stores are involved.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="UpdateSignalPoller"/> and everything under it.
    ///
    /// <para><b>No credentials, no configuration, no setup step.</b> Both
    /// endpoints are keyless — <c>GetNewsForApp</c> verified live returning 200
    /// with no <c>key=</c>, and steamcmd.net states outright that "no
    /// authentication or verification is required" — so unlike IGDB there is no
    /// "not configured" state to handle, and M2 needs no settings screen for API
    /// keys.</para>
    ///
    /// <para>Storage defaults to SQLite over the existing <c>metadata_cache</c>,
    /// <c>update_events</c> and ownership tables, and therefore expects an
    /// <c>ISqliteConnectionFactory</c> in the container. Register any of the
    /// storage interfaces yourself beforehand to override — every registration
    /// here is <c>TryAdd</c>.</para>
    ///
    /// <para>Two typed clients against two different hosts, each with its own
    /// pipeline and its own singleton rate limiter. Handler order is deliberate
    /// and outermost first: retry (which owns 429 backoff and <c>Retry-After</c>,
    /// and pointedly does NOT own 403) → rate limiter (which owns the request
    /// budget). The limiter sits innermost so retried attempts spend permits like
    /// any other request and a backoff storm cannot exceed the configured
    /// rate.</para>
    ///
    /// <para>The budgets are separate because the hosts are: Valve's API and a
    /// free volunteer PICS mirror do not share a courtesy budget.</para>
    /// </summary>
    public static IServiceCollection AddUpdateSignals(this IServiceCollection services)
        => services.AddUpdateSignals(configure: null);

    /// <inheritdoc cref="AddUpdateSignals(IServiceCollection)"/>
    /// <param name="services">The container.</param>
    /// <param name="configure">Overrides for <see cref="UpdateSignalOptions"/>.</param>
    public static IServiceCollection AddUpdateSignals(
        this IServiceCollection services, Action<UpdateSignalOptions>? configure)
    {
        var options = new UpdateSignalOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<IUpdateSignalCache, SqliteUpdateSignalCache>();
        services.TryAddSingleton<IUpdatePollStateStore, UpdatePollStateStore>();
        services.TryAddSingleton<IPollCandidateSource, SqlitePollCandidateSource>();
        services.TryAddSingleton<IUpdateEventWriter, SqliteUpdateEventWriter>();

        services.TryAddSingleton<SteamNewsRateLimiter>();
        services.TryAddSingleton<BuildInfoRateLimiter>();
        services.TryAddTransient<SteamNewsResilienceHandler>();
        services.TryAddTransient<SteamNewsRateLimitingHandler>();
        services.TryAddTransient<BuildInfoResilienceHandler>();
        services.TryAddTransient<BuildInfoRateLimitingHandler>();

        services.AddHttpClient<ISteamNewsClient, SteamNewsClient>(client =>
            {
                client.BaseAddress = options.NewsBaseAddress;
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
            })
            .AddHttpMessageHandler<SteamNewsResilienceHandler>()
            .AddHttpMessageHandler<SteamNewsRateLimitingHandler>();

        services.AddHttpClient<IBuildInfoClient, SteamCmdBuildInfoClient>(client =>
            {
                client.BaseAddress = options.BuildInfoBaseAddress;

                // The User-Agent matters more here than anywhere else in Winnow:
                // steamcmd.net is run by a volunteer with no SLA and no contact
                // channel other than whatever traffic identifies itself.
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
            })
            .AddHttpMessageHandler<BuildInfoResilienceHandler>()
            .AddHttpMessageHandler<BuildInfoRateLimitingHandler>();

        services.TryAddSingleton<UpdateSignalPoller>();

        return services;
    }
}
