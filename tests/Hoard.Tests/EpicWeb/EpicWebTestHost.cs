using System.Net;
using Hoard.Core.Auth;
using Hoard.Core.Repositories;
using Hoard.Ingest.Epic.Web;
using Hoard.Ingest.Epic.Web.Auth;
using Hoard.Ingest.Epic.Web.Credentials;
using Hoard.Tests.SteamWeb;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hoard.Tests.EpicWeb;

/// <summary>Canned Epic bodies, read from <c>tests/fixtures/epic-oauth/</c>.</summary>
public static class EpicFixturesWeb
{
    public static string Read(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "epic-oauth", name));

    public static string Token() => Read("oauth-token.json");

    public static string RefreshedToken() => Read("oauth-token-refreshed.json");

    public static string InvalidClient() => Read("oauth-invalid-client.json");

    public static string InvalidRefresh() => Read("oauth-invalid-refresh.json");

    public static string LibraryPage1() => Read("library-items-page1.json");

    public static string LibraryPage2() => Read("library-items-page2.json");

    public static string Playtime() => Read("playtime-all.json");

    public static string Unauthenticated() => Read("library-unauthenticated-401.json");

    /// <summary>
    /// Verbatim: what <c>id/api/redirect</c> returns to a browser with no Epic
    /// session. Every code field present and null.
    /// </summary>
    public static string RedirectNoSession() => Read("redirect-no-session.json");

    /// <summary>The same shape with a (fabricated) authorization code in it.</summary>
    public static string RedirectWithCode() => Read("redirect-with-code.json");

    /// <summary>Catalog item id of Fez — shared with <c>tests/fixtures/epic/</c> so the halves join.</summary>
    public const string FezCatalogItemId = "7a70b499513441c792b541d53505e0b2";

    /// <summary>Epic's per-artifact codename for Fez. Never a title.</summary>
    public const string FezAppName = "Bluebird";

    /// <summary>Catalog item id of Watch Dogs — the third-party-managed title in the local fixtures.</summary>
    public const string WatchDogsCatalogItemId = "6dc445f656de4e029834b2d32b6a2f77";

    /// <summary>Catalog item id present in the API fixtures with NO playtime entry — the null-not-zero case.</summary>
    public const string NoPlaytimeCatalogItemId = "b1000000000000000000000000000002";

    /// <summary>That title's artifact codename.</summary>
    public const string NoPlaytimeAppName = "Skylark";
}

/// <summary>
/// A real service provider wired by
/// <see cref="EpicWebServiceCollectionExtensions.AddEpicWebApi(IServiceCollection)"/>,
/// with only the primary transport swapped for <see cref="FakeEpicHandler"/>.
///
/// <para>Going through the actual DI extension rather than newing up an
/// <see cref="EpicAccountClient"/> is the point: it is the registration —
/// handler order, singleton lifetimes, the shared rate limiter, the DPAPI
/// protector selection, and above all the removal of the framework's
/// URI-printing loggers — that several of these tests are asserting on.</para>
///
/// <para>The token store defaults to <see cref="InMemoryEpicTokenStore"/> so the
/// tests never touch real DPAPI or a real settings table; the tests that care
/// about persistence supply their own.</para>
/// </summary>
public sealed class EpicWebTestHost : IDisposable
{
    private readonly ServiceProvider _services;

    public EpicWebTestHost(
        Func<RecordedEpicRequest, int, HttpResponseMessage> responder,
        string? clientId = "test-client-id",
        string? clientSecret = "test-client-secret",
        Action<EpicWebOptions>? configure = null,
        IEpicTokenStore? tokenStore = null,
        ISettingsRepository? settings = null,
        DateTimeOffset? now = null,
        bool builtInCredentials = false,
        IEnumerable<IInteractiveAuthPrompt>? prompts = null)
    {
        Handler = new FakeEpicHandler(responder);
        Clock = new SteamWebTestClock(now ?? new DateTimeOffset(2026, 8, 26, 20, 0, 0, TimeSpan.Zero));
        Settings = settings ?? new InMemorySettingsRepository();
        TokenStore = tokenStore ?? new InMemoryEpicTokenStore();
        Logs = new CapturingLoggerProvider();

        if (clientId is not null)
        {
            Settings.SetAsync(SettingsTableEpicCredentialSource.ClientIdSetting, clientId)
                .GetAwaiter().GetResult();
        }

        if (clientSecret is not null)
        {
            Settings.SetAsync(SettingsTableEpicCredentialSource.ClientSecretSetting, clientSecret)
                .GetAwaiter().GetResult();
        }

        var services = new ServiceCollection();

        // Trace, not Warning: the point of several of these tests is that even
        // the most verbose sink never sees a token, a code or a secret.
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(Logs);
        });

        // Registered before AddEpicWebApi so its TryAdd calls defer to these.
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Settings);
        services.AddSingleton(TokenStore);

        services.AddEpicWebApi(options =>
        {
            // Keep the backoff schedule and the rate limiter out of the way; the
            // tests that care about either override these deliberately.
            options.RetryBaseDelay = TimeSpan.FromMilliseconds(5);
            options.MaxRetryDelay = TimeSpan.FromMilliseconds(20);
            options.RequestsPerSecond = 1000;
            configure?.Invoke(options);
        });

        // Both named clients get the fake transport. The library client is typed,
        // the token client is named, so they need separate registrations.
        services.AddHttpClient<IEpicAccountClient, EpicAccountClient>()
            .ConfigurePrimaryHttpMessageHandler(() => Handler);
        services.AddHttpClient(EpicTokenProvider.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => Handler);

        foreach (var prompt in prompts ?? [])
        {
            // Registration order IS the fallback order, so the tests that assert
            // on fall-through hand them over in the order they want tried.
            services.AddSingleton(prompt);
        }

        if (!builtInCredentials)
        {
            // Off unless a test asks for it. Hoard ships Epic's launcher pair as
            // the LAST credential source, which means every real install is
            // configured — so leaving it on here would silently turn every
            // "unconfigured" test into a "configured with the built-in pair"
            // test, and the no-op-when-unconfigured contract would stop being
            // asserted anywhere. The tests that care about the built-in source
            // turn it on and say so.
            foreach (var descriptor in services
                .Where(d => d.ServiceType == typeof(IEpicCredentialSource)
                    && d.ImplementationType == typeof(BuiltInEpicCredentialSource))
                .ToList())
            {
                services.Remove(descriptor);
            }
        }

        _services = services.BuildServiceProvider();
    }

    public FakeEpicHandler Handler { get; }

    public SteamWebTestClock Clock { get; }

    public ISettingsRepository Settings { get; }

    public IEpicTokenStore TokenStore { get; }

    public CapturingLoggerProvider Logs { get; }

    public IEpicAccountClient Client => _services.GetRequiredService<IEpicAccountClient>();

    public IEpicTokenProvider Tokens => _services.GetRequiredService<IEpicTokenProvider>();

    public EpicInteractiveSignIn SignIn => _services.GetRequiredService<EpicInteractiveSignIn>();

    public T Resolve<T>()
        where T : notnull
        => _services.GetRequiredService<T>();

    /// <summary>
    /// The happy-path responder: a token for any grant, both library pages, and
    /// the playtime list.
    /// </summary>
    public static Func<RecordedEpicRequest, int, HttpResponseMessage> Healthy()
        => (request, _) => request.Endpoint switch
        {
            EpicEndpoint.Token => FakeEpicHandler.Json(
                HttpStatusCode.OK,
                request.GrantType == "refresh_token"
                    ? EpicFixturesWeb.RefreshedToken()
                    : EpicFixturesWeb.Token()),
            EpicEndpoint.LibraryItems => FakeEpicHandler.Json(
                HttpStatusCode.OK,
                request.Query("cursor") is null
                    ? EpicFixturesWeb.LibraryPage1()
                    : EpicFixturesWeb.LibraryPage2()),
            EpicEndpoint.Playtime => FakeEpicHandler.Json(HttpStatusCode.OK, EpicFixturesWeb.Playtime()),
            _ => FakeEpicHandler.Json(HttpStatusCode.NotFound, "{}"),
        };

    /// <summary>Signs in with a canned authorization code, so a test can get straight to the library.</summary>
    public async Task SignInAsync()
    {
        var result = await Client.SignInAsync("FAKE-AUTH-CODE");
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Test host could not sign in: " + result.Failure);
        }
    }

    public void Dispose() => _services.Dispose();
}
