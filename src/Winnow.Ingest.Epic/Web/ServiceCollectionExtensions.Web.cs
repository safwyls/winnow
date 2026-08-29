using Winnow.Core.Repositories;
using Winnow.Ingest.Epic.Web.Auth;
using Winnow.Ingest.Epic.Web.Credentials;
using Winnow.Ingest.Epic.Web.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Winnow.Ingest.Epic.Web;

/// <summary>
/// Composition for the authenticated Epic module. Separate from
/// <c>AddEpicIngest</c> on purpose: the local readers are unconditional and
/// free, while this half is opt-in, needs credentials, and talks to the network.
/// A host can register either, both, or neither.
/// </summary>
public static class EpicWebServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IEpicAccountClient"/> and everything under it.
    /// No credentials are required; unconfigured installs decline every call.
    /// </summary>
    public static IServiceCollection AddEpicWebApi(this IServiceCollection services)
        => services.AddEpicWebApi(configure: null);

    /// <inheritdoc cref="AddEpicWebApi(IServiceCollection)"/>
    /// <param name="services">The container.</param>
    /// <param name="configure">Overrides for <see cref="EpicWebOptions"/>.</param>
    public static IServiceCollection AddEpicWebApi(
        this IServiceCollection services, Action<EpicWebOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new EpicWebOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IEpicLibraryCache, InMemoryEpicLibraryCache>();
        services.TryAddSingleton<IEpicCatalogCache, InMemoryEpicCatalogCache>();

        // DPAPI on Windows, and an implementation that REFUSES rather than
        // degrades anywhere else. There is deliberately no plaintext fallback:
        // an Epic refresh token is, in Epic's own words on the page that issues
        // the authorization code, full access to the user's account.
        if (OperatingSystem.IsWindows())
        {
            services.TryAddSingleton<IEpicSecretProtector, DpapiEpicSecretProtector>();
        }
        else
        {
            services.TryAddSingleton<IEpicSecretProtector, UnavailableEpicSecretProtector>();
        }

        // ISettingsRepository is resolved optionally — Winnow.Ingest.Epic does not
        // reference Winnow.Data, so it cannot register a concrete one. A host with
        // a settings table gets persistence; one without gets an in-memory
        // session. Neither is an error.
        services.TryAddSingleton<IEpicTokenStore>(sp => new SettingsEpicTokenStore(
            sp.GetService<ISettingsRepository>(),
            sp.GetRequiredService<IEpicSecretProtector>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<SettingsEpicTokenStore>>()));

        // Order is the resolution order: settings table first (the product
        // path), then IConfiguration (Epic__ClientId / Epic__ClientSecret and an
        // optional appsettings.local.json) for development, then the built-in
        // launcher pair LAST so that anything the user supplies wins. See
        // BuiltInEpicCredentialSource for why Winnow ships one at all — the
        // decision reversed on 2026-08-26 and the reasoning is recorded there
        // rather than here.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEpicCredentialSource, DefaultSettingsTableEpicCredentialSource>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEpicCredentialSource, DefaultConfigurationEpicCredentialSource>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEpicCredentialSource, BuiltInEpicCredentialSource>());
        services.TryAddSingleton<IEpicCredentialProvider, ChainedEpicCredentialProvider>();

        services.TryAddSingleton<IEpicTokenProvider, EpicTokenProvider>();

        // The interactive sign-in. It resolves IInteractiveAuthPrompt from
        // Winnow.Core, and this project registers NONE — the implementations are
        // a browser host and a console, both App-layer concerns, so the host
        // registers them and this module stays free of any UI dependency (§5.1).
        // A host that registers no prompt at all gets a clean
        // NoInteractivePrompt rather than a startup failure.
        services.TryAddSingleton<EpicInteractiveSignIn>();

        // AddLogger<T> resolves T from the container rather than activating it,
        // so the replacement logger has to be registered before the clients are.
        services.TryAddSingleton<RedactingEpicHttpClientLogger>();
        services.TryAddSingleton<EpicRateLimiter>();
        services.TryAddTransient<EpicResilienceHandler>();
        services.TryAddTransient<EpicRateLimitingHandler>();
        services.TryAddTransient<EpicAuthenticationHandler>();

        // The token client. No auth handler on this one — it IS the auth, and
        // adding one would recurse. It still gets retry and the shared rate
        // limiter, because a throttled token endpoint is exactly as capable of
        // 429ing as any other.
        services.AddHttpClient(EpicTokenProvider.HttpClientName, client =>
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
            })
            .RemoveAllLoggers()
            .AddLogger<RedactingEpicHttpClientLogger>()
            .AddHttpMessageHandler<EpicResilienceHandler>()
            .AddHttpMessageHandler<EpicRateLimitingHandler>();

        services.AddHttpClient<IEpicAccountClient, EpicAccountClient>(client =>
            {
                client.BaseAddress = options.LibraryBaseAddress;
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
            })
            .RemoveAllLoggers()
            .AddLogger<RedactingEpicHttpClientLogger>()
            .AddHttpMessageHandler<EpicAuthenticationHandler>()
            .AddHttpMessageHandler<EpicResilienceHandler>()
            .AddHttpMessageHandler<EpicRateLimitingHandler>();

        // The catalog service: a different host from the library service, so a
        // different client — but the SAME three handlers in the same order, and
        // the same shared EpicRateLimiter singleton, so catalog and library
        // requests spend one budget between them rather than two independent
        // ones. That is the point of the limiter being a singleton: Epic
        // publishes no rate limit, so the conservative ceiling has to apply to
        // Winnow's total Epic traffic, not per endpoint.
        services.AddHttpClient<IEpicCatalogClient, EpicCatalogClient>(client =>
            {
                client.BaseAddress = options.CatalogBaseAddress;
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
            })
            .RemoveAllLoggers()
            .AddLogger<RedactingEpicHttpClientLogger>()
            .AddHttpMessageHandler<EpicAuthenticationHandler>()
            .AddHttpMessageHandler<EpicResilienceHandler>()
            .AddHttpMessageHandler<EpicRateLimitingHandler>();

        return services;
    }

    /// <summary>
    /// Adds <c>appsettings.local.json</c> and environment variables for Epic
    /// credential configuration. Optional if the host already provides these.
    /// </summary>
    public static IConfigurationBuilder AddEpicWebLocalConfiguration(this IConfigurationBuilder builder)
        => builder
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();
}
