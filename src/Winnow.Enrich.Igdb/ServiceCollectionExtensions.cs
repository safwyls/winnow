using Winnow.Enrich.Igdb.Auth;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.Igdb.Http;
using Winnow.Enrich.Igdb.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Winnow.Enrich.Igdb;

/// <summary>
/// Composition for the IGDB module. The host's composition root calls
/// <see cref="AddIgdbEnrichment"/>; nothing outside this assembly needs to know
/// which handlers, limiters or stores are involved.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the IGDB client and everything under it.
    ///
    /// <para>Storage defaults to the SQLite-backed <c>settings</c> and
    /// <c>metadata_cache</c> tables and therefore expects an
    /// <c>ISqliteConnectionFactory</c> in the container. Register an
    /// <see cref="ISettingsStore"/>/<see cref="IMetadataCache"/> of your own
    /// beforehand to override — every registration here is <c>TryAdd</c>.</para>
    ///
    /// <para>Handler order on the IGDB pipeline is deliberate and outermost
    /// first: auth (owns 401 and token refresh) → retry (owns 429/5xx backoff)
    /// → rate limiter (owns the 4 req/s budget). The limiter sits innermost so
    /// retried attempts spend permits like any other request.</para>
    /// </summary>
    public static IServiceCollection AddIgdbEnrichment(
        this IServiceCollection services, Action<IgdbOptions>? configure = null)
    {
        var options = new IgdbOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISettingsStore, SqliteSettingsStore>();
        services.TryAddSingleton<IMetadataCache, SqliteMetadataCache>();

        // Order is the resolution order: settings table first (the product
        // path — §4.2 keys are user-supplied and stored locally), then
        // IConfiguration (env vars Igdb__ClientId / Igdb__ClientSecret and an
        // optional appsettings.local.json) for development.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIgdbCredentialSource, SettingsTableCredentialSource>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IIgdbCredentialSource, DefaultConfigurationCredentialSource>());
        services.TryAddSingleton<IIgdbCredentialProvider, ChainedIgdbCredentialProvider>();

        services.TryAddSingleton<IIgdbTokenProvider, TwitchTokenProvider>();
        services.TryAddSingleton<IgdbRateLimiter>();
        services.TryAddTransient<IgdbAuthenticationHandler>();
        services.TryAddTransient<IgdbResilienceHandler>();
        services.TryAddTransient<IgdbRateLimitingHandler>();

        // Token minting talks to id.twitch.tv, not api.igdb.com: it is outside
        // IGDB's 4 req/s budget and must not be authenticated (it is what
        // produces the credential), so it gets retry only.
        services.AddHttpClient(TwitchTokenProvider.HttpClientName, client =>
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
            })
            .AddHttpMessageHandler<IgdbResilienceHandler>();

        services.AddHttpClient<IIgdbClient, IgdbClient>(client =>
            {
                client.BaseAddress = options.BaseAddress;
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
            })
            .AddHttpMessageHandler<IgdbAuthenticationHandler>()
            .AddHttpMessageHandler<IgdbResilienceHandler>()
            .AddHttpMessageHandler<IgdbRateLimitingHandler>();

        return services;
    }

    /// <summary>
    /// Adds the two configuration sources the developer credential path reads:
    /// a git-ignored <c>appsettings.local.json</c> beside the executable, and
    /// environment variables (<c>Igdb__ClientId</c> / <c>Igdb__ClientSecret</c>).
    ///
    /// <para>Optional. A host that already configures these — as the default
    /// generic host does for environment variables — needs nothing from here.
    /// Neither source is ever written to; Winnow only reads credentials it did
    /// not create.</para>
    /// </summary>
    public static IConfigurationBuilder AddIgdbLocalConfiguration(this IConfigurationBuilder builder)
        => builder
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();
}
