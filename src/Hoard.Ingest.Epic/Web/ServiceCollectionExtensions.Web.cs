using Hoard.Core.Repositories;
using Hoard.Ingest.Epic.Web.Auth;
using Hoard.Ingest.Epic.Web.Credentials;
using Hoard.Ingest.Epic.Web.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hoard.Ingest.Epic.Web;

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
    ///
    /// <para><b>No credentials are required to call this.</b> A host that
    /// registers the module and a user who never enters a client pair produce a
    /// client that declines every call and makes no requests (§5.1) — there is no
    /// startup failure and no configuration step. Registering it is safe on every
    /// machine, exactly like <c>AddSteamWebApi</c>.</para>
    ///
    /// <para><b>This does not replace <c>AddEpicIngest</c>.</b> The two are a
    /// union, not a choice (§4.2's rule applied to Epic): the local readers see
    /// install state, install paths and third-party-managed titles, and the API
    /// sees the true entitlement list, acquisition dates and playtime. A host
    /// that registers only this one loses install state entirely. Register both.</para>
    ///
    /// <para><b>Handler order is deliberate and outermost first:</b> auth (owns
    /// the bearer header and the single 401 refresh) → retry (owns 429/5xx
    /// backoff) → rate limiter (owns the request budget). The limiter sits
    /// innermost so retried attempts spend permits like any other request and a
    /// backoff storm cannot exceed the configured rate; auth sits outermost so
    /// its re-auth attempt goes back through both.</para>
    ///
    /// <para><b>The logging change is a security control, not a preference.</b>
    /// <c>RemoveAllLoggers()</c> strips the two loggers
    /// <c>IHttpClientFactory</c> attaches by default, both of which write the
    /// full request URI at <c>Information</c>. Every path on the library service
    /// carries the user's Epic account id, and the playtime paths additionally
    /// name a specific owned game, so leaving them in place would write both to
    /// the log on every sync. <see cref="RedactingEpicHttpClientLogger"/>
    /// replaces them. This affects only this module's clients.</para>
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

        // ISettingsRepository is resolved optionally — Hoard.Ingest.Epic does not
        // reference Hoard.Data, so it cannot register a concrete one. A host with
        // a settings table gets persistence; one without gets an in-memory
        // session. Neither is an error.
        services.TryAddSingleton<IEpicTokenStore>(sp => new SettingsEpicTokenStore(
            sp.GetService<ISettingsRepository>(),
            sp.GetRequiredService<IEpicSecretProtector>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<SettingsEpicTokenStore>>()));

        // Order is the resolution order: settings table first (the product
        // path), then IConfiguration (Epic__ClientId / Epic__ClientSecret and an
        // optional appsettings.local.json) for development.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEpicCredentialSource, DefaultSettingsTableEpicCredentialSource>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEpicCredentialSource, DefaultConfigurationEpicCredentialSource>());
        services.TryAddSingleton<IEpicCredentialProvider, ChainedEpicCredentialProvider>();

        services.TryAddSingleton<IEpicTokenProvider, EpicTokenProvider>();

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

        return services;
    }

    /// <summary>
    /// Adds the two configuration sources the developer credential path reads: a
    /// git-ignored <c>appsettings.local.json</c> beside the executable, and
    /// environment variables (<c>Epic__ClientId</c>, <c>Epic__ClientSecret</c>).
    ///
    /// <para>Optional. A host that already configures these — as the default
    /// generic host does for environment variables — needs nothing from here.
    /// Neither source is ever written to; Hoard only reads credentials it did not
    /// create.</para>
    /// </summary>
    public static IConfigurationBuilder AddEpicWebLocalConfiguration(this IConfigurationBuilder builder)
        => builder
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();
}
