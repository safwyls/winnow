using Hoard.Core.Repositories;
using Hoard.Data.Repositories;
using Hoard.Enrich.SteamWeb.Credentials;
using Hoard.Enrich.SteamWeb.Http;
using Hoard.Enrich.SteamWeb.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hoard.Enrich.SteamWeb;

/// <summary>
/// Composition for the Steam Web API module. The host's composition root calls
/// <see cref="AddSteamWebApi(IServiceCollection)"/>; nothing outside this
/// assembly needs to know which handlers, limiters or stores are involved.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISteamWebApiClient"/> and everything under it.
    ///
    /// <para><b>No key is required to call this.</b> A host that registers the
    /// module and a user who never enters a key produce a client that declines
    /// every call and makes no requests (§5.1) — there is no startup failure and
    /// no configuration step.</para>
    ///
    /// <para>Storage defaults to the SQLite-backed <c>settings</c> and
    /// <c>metadata_cache</c> tables and therefore expects an
    /// <c>ISqliteConnectionFactory</c> in the container. Register an
    /// <see cref="ISettingsRepository"/> or <see cref="ISteamWebMetadataCache"/>
    /// of your own beforehand to override — every registration here is
    /// <c>TryAdd</c>.</para>
    ///
    /// <para>Handler order on the pipeline is deliberate and outermost first:
    /// retry (owns 429/5xx backoff and <c>Retry-After</c>, §4.2) → rate limiter
    /// (owns the request budget). The limiter sits innermost so retried attempts
    /// spend permits like any other request and a backoff storm cannot exceed the
    /// configured rate.</para>
    ///
    /// <para><b>The logging change is a security control, not a preference.</b>
    /// <c>RemoveAllLoggers()</c> strips the two loggers
    /// <see cref="IHttpClientFactory"/> attaches by default, both of which write
    /// the full request URI at <c>Information</c>. §4.2's <c>GetOwnedGames</c>
    /// carries the user's API key in that URI and offers no header or body
    /// alternative, so leaving them in place would write the key to the log on
    /// every sync. <see cref="RedactingHttpClientLogger"/> replaces them.
    /// This affects only this module's client.</para>
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

        return services;
    }

    /// <summary>
    /// Adds the two configuration sources the developer key path reads: a
    /// git-ignored <c>appsettings.local.json</c> beside the executable, and
    /// environment variables (<c>Steam__ApiKey</c>).
    ///
    /// <para>Optional. A host that already configures these — as the default
    /// generic host does for environment variables — needs nothing from here.
    /// Neither source is ever written to; Hoard only reads keys it did not
    /// create.</para>
    /// </summary>
    public static IConfigurationBuilder AddSteamWebLocalConfiguration(this IConfigurationBuilder builder)
        => builder
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();
}
