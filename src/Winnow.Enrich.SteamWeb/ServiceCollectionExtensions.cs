using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Http;
using Winnow.Enrich.SteamWeb.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Winnow.Enrich.SteamWeb;

/// <summary>
/// Composition for the Steam Web API module. The host's composition root calls
/// <see cref="AddSteamWebApi(IServiceCollection)"/>; nothing outside this
/// assembly needs to know which handlers, limiters or stores are involved.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISteamWebApiClient"/> and everything under it. No
    /// key is required; a user who never enters one gets a client that declines
    /// every call.
    /// </summary>
    public static IServiceCollection AddSteamWebApi(this IServiceCollection services)
        => services.AddSteamWebApi(configure: null);

    /// <inheritdoc cref="AddSteamWebApi(IServiceCollection)"/>
    /// <param name="services">The container.</param>
    /// <param name="configure">Overrides for <see cref="SteamWebOptions"/>.</param>
    public static IServiceCollection AddSteamWebApi(
        this IServiceCollection services, Action<SteamWebOptions>? configure)
    {
        var options = new SteamWebOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISettingsRepository, SettingsRepository>();
        services.TryAddSingleton<ISteamWebMetadataCache, SqliteSteamWebMetadataCache>();

        // Order is the resolution order: settings table first (the product path
        // — §4.2 keys are user-supplied and stored locally), then
        // IConfiguration (the Steam__ApiKey environment variable and an optional
        // appsettings.local.json) for development.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ISteamApiKeySource, SettingsTableApiKeySource>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ISteamApiKeySource, DefaultConfigurationApiKeySource>());
        services.TryAddSingleton<ISteamApiKeyProvider, ChainedSteamApiKeyProvider>();

        // AddLogger<T> resolves T from the container rather than activating it,
        // so the replacement logger has to be registered before the client is.
        services.TryAddSingleton<RedactingHttpClientLogger>();
        services.TryAddSingleton<SteamWebRateLimiter>();
        services.TryAddTransient<SteamWebResilienceHandler>();
        services.TryAddTransient<SteamWebRateLimitingHandler>();

        services.AddHttpClient<ISteamWebApiClient, SteamWebApiClient>(client =>
            {
                client.BaseAddress = options.BaseAddress;

                // §4.3's rule applied here too: a descriptive User-Agent so Valve
                // can attribute — and if necessary contact — this traffic.
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
            })
            .RemoveAllLoggers()
            .AddLogger<RedactingHttpClientLogger>()
            .AddHttpMessageHandler<SteamWebResilienceHandler>()
            .AddHttpMessageHandler<SteamWebRateLimitingHandler>();

        // M5's two history endpoints, on an identical pipeline. A second typed
        // client rather than more methods on the first, because they are a
        // different job (a background backfill that runs a handful of times
        // per install, not the per-sync ownership read), and a caller that
        // only wants one should not be able to reach the other.
        //
        // The handlers are separate INSTANCES (both are transient) but the
        // limiter they close over is the same singleton, so the two clients pace
        // themselves against one another rather than each staying under a limit
        // they are jointly over. That is the whole reason SteamWebRateLimiter is
        // registered as a singleton above and not built inside the handler.
        services.AddHttpClient<ISteamHistoryClient, SteamHistoryClient>(client =>
            {
                client.BaseAddress = options.BaseAddress;
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
            })
            .RemoveAllLoggers()
            .AddLogger<RedactingHttpClientLogger>()
            .AddHttpMessageHandler<SteamWebResilienceHandler>()
            .AddHttpMessageHandler<SteamWebRateLimitingHandler>();

        return services;
    }

    /// <summary>
    /// Adds <c>appsettings.local.json</c> and environment variables as
    /// configuration sources for the developer key path.
    /// </summary>
    public static IConfigurationBuilder AddSteamWebLocalConfiguration(this IConfigurationBuilder builder)
        => builder
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();
}
