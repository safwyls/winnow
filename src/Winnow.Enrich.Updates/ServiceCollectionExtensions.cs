using Winnow.Enrich.Updates.Http;
using Winnow.Enrich.Updates.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Winnow.Enrich.Updates;

/// <summary>DI composition for the update-signal module.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="UpdateSignalPoller"/> and everything under it.
    /// Both endpoints are keyless; no credentials or settings screen needed.
    /// Storage interfaces are <c>TryAdd</c> -- register your own beforehand to override.
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
