using System.Net;
using Hoard.Core.Repositories;
using Hoard.Ingest.Epic.Web;
using Hoard.Ingest.Epic.Web.Auth;
using Hoard.Ingest.Epic.Web.Credentials;
using Hoard.Ingest.Epic.Web.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hoard.Tests.EpicWeb;

/// <summary>
/// Nothing secret, and nothing identifying, reaches a log or the database in the
/// clear.
///
/// <para>These run against the <b>most verbose sink there is</b> — every logger
/// at Trace, capturing structured argument values as well as rendered messages,
/// which is what a JSON sink would emit. A leak that only appears at Debug is
/// still a leak.</para>
/// </summary>
public sealed class EpicSecretsTests
{
    private const string ClientSecret = "SUPER-SECRET-CLIENT-VALUE";
    private const string AuthCode = "SUPER-SECRET-AUTH-CODE";
    private const string AccessToken = "FAKE_ACCESS_TOKEN_0000000000000000";
    private const string RefreshToken = "FAKE_REFRESH_TOKEN_000000000000000";
    private const string AccountId = "00000000000000000000000000000001";

    [Fact]
    public async Task No_secret_and_no_account_id_reaches_the_log()
    {
        using var host = new EpicWebTestHost(
            EpicWebTestHost.Healthy(), clientId: "SECRET-CLIENT-ID", clientSecret: ClientSecret);

        await host.Client.SignInAsync(AuthCode);

        // Drive the full pipeline: library, playtime, a refresh, and a failure.
        await host.Client.GetOwnedLibraryAsync();
        host.Clock.Now = new DateTimeOffset(2026, 8, 27, 6, 0, 0, TimeSpan.Zero);
        await host.Client.GetOwnedLibraryAsync(TimeSpan.Zero);

        var log = host.Logs.AllText;

        Assert.DoesNotContain(ClientSecret, log, StringComparison.Ordinal);
        Assert.DoesNotContain(AuthCode, log, StringComparison.Ordinal);
        Assert.DoesNotContain(AccessToken, log, StringComparison.Ordinal);
        Assert.DoesNotContain(RefreshToken, log, StringComparison.Ordinal);

        // The account id is not a credential, but it identifies a real person's
        // Epic account and appears in every library and playtime path.
        Assert.DoesNotContain(AccountId, log, StringComparison.Ordinal);

        // The client id identifies WHICH Epic client is being impersonated. A log
        // that names it is a log that documents the impersonation.
        Assert.DoesNotContain("SECRET-CLIENT-ID", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_leaks_when_every_request_fails()
    {
        // The failure paths are where secrets usually escape, via exception
        // messages and "here is what we sent" diagnostics.
        using var host = new EpicWebTestHost(
            (_, _) => throw new HttpRequestException("connection reset while sending to " + AccountId),
            clientSecret: ClientSecret);

        var result = await host.Client.SignInAsync(AuthCode);
        Assert.False(result.Succeeded);

        var log = host.Logs.AllText;
        Assert.DoesNotContain(ClientSecret, log, StringComparison.Ordinal);
        Assert.DoesNotContain(AuthCode, log, StringComparison.Ordinal);

        // The handler's own exception message quoted the account id. It must not
        // be echoed — which is why the module logs exception TYPES, never the
        // exception object and never its message.
        Assert.DoesNotContain(AccountId, log, StringComparison.Ordinal);
    }

    [Fact]
    public void Redaction_removes_account_and_artifact_ids_from_a_path()
    {
        // The structural difference from the Steam redactor: Epic's identifiers
        // are PATH SEGMENTS, not query values. A redactor that only walked the
        // query string would print the account id on every request while looking
        // like it was working.
        var described = EpicRedaction.Describe(
            new Uri("https://library-service.live.use1a.on.epicgames.com/library/api/public/playtime/account/"
                + AccountId + "/artifact/Bluebird"));

        Assert.DoesNotContain(AccountId, described, StringComparison.Ordinal);
        Assert.DoesNotContain("Bluebird", described, StringComparison.Ordinal);
        Assert.Contains(EpicRedaction.Placeholder, described, StringComparison.Ordinal);

        // The shape is still legible, which is the point of redacting rather than
        // suppressing.
        Assert.Contains("/library/api/public/playtime/account/", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Redaction_allowlists_query_values_rather_than_denylisting_them()
    {
        var described = EpicRedaction.Describe(
            new Uri("https://library-service.live.use1a.on.epicgames.com/library/api/public/items"
                + "?includeMetadata=true&cursor=OPAQUE-POSITION-IN-A-REAL-LIBRARY"));

        // Allowlisted: the request-shape flag is worth confirming from a log.
        Assert.Contains("includeMetadata=true", described, StringComparison.Ordinal);

        // Not allowlisted: an opaque token encoding a position in a specific
        // account's library.
        Assert.DoesNotContain("OPAQUE-POSITION-IN-A-REAL-LIBRARY", described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_token_never_prints_itself()
    {
        // The compiler-generated record ToString would print every property the
        // first time anyone interpolated one of these into a log line.
        var token = new EpicOAuthToken(
            "client", AccessToken, RefreshToken, AccountId, "SomeDisplayName",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var text = token.ToString();

        Assert.DoesNotContain(AccessToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(RefreshToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(AccountId, text, StringComparison.Ordinal);
        Assert.DoesNotContain("SomeDisplayName", text, StringComparison.Ordinal);
        Assert.Contains("redacted", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Credentials_never_print_themselves()
    {
        var credentials = EpicClientCredentials.TryCreate("the-client-id", ClientSecret, "settings");

        var text = credentials!.ToString();

        Assert.DoesNotContain(ClientSecret, text, StringComparison.Ordinal);
        Assert.DoesNotContain("the-client-id", text, StringComparison.Ordinal);
        Assert.Contains("redacted", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_stored_session_is_never_cleartext_in_the_settings_table()
    {
        var settings = new SteamWeb.InMemorySettingsRepository();
        var protector = new ReversibleTestProtector();
        var store = new SettingsEpicTokenStore(settings, protector);

        var token = new EpicOAuthToken(
            "client", AccessToken, RefreshToken, AccountId, "SomeDisplayName",
            DateTimeOffset.UtcNow.AddHours(8), DateTimeOffset.UtcNow.AddDays(23));

        await store.SaveAsync(token);

        var stored = await settings.GetAsync(SettingsEpicTokenStore.SessionSetting);

        Assert.NotNull(stored);
        Assert.DoesNotContain(AccessToken, stored, StringComparison.Ordinal);
        Assert.DoesNotContain(RefreshToken, stored, StringComparison.Ordinal);
        Assert.DoesNotContain(AccountId, stored, StringComparison.Ordinal);
        Assert.DoesNotContain("SomeDisplayName", stored, StringComparison.Ordinal);

        // And it round-trips, so the protection is real rather than lossy.
        var loaded = await store.LoadAsync();
        Assert.Equal(token.AccessToken, loaded!.AccessToken);
        Assert.Equal(token.RefreshToken, loaded.RefreshToken);
        Assert.Equal(token.AccountId, loaded.AccountId);
    }

    [Fact]
    public async Task A_session_that_cannot_be_encrypted_is_not_written_at_all()
    {
        // The rule with no exceptions: there is no plaintext fallback. The
        // failure mode of one is silent and permanent; the failure mode of
        // refusing is a login the user repeats after a restart.
        var settings = new SteamWeb.InMemorySettingsRepository();
        var store = new SettingsEpicTokenStore(settings, new UnavailableEpicSecretProtector());

        Assert.False(store.CanPersist);

        await store.SaveAsync(new EpicOAuthToken(
            "client", AccessToken, RefreshToken, AccountId, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        Assert.Null(await settings.GetAsync(SettingsEpicTokenStore.SessionSetting));
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task An_undecryptable_stored_session_degrades_to_no_session()
    {
        // A profile restored onto another machine, a different Windows user, a
        // truncated value. All of them mean "sign in again", none of them mean
        // "crash".
        var settings = new SteamWeb.InMemorySettingsRepository();
        await settings.SetAsync(SettingsEpicTokenStore.SessionSetting, "not-decryptable-by-anyone");

        var store = new SettingsEpicTokenStore(settings, new ReversibleTestProtector { FailToUnprotect = true });

        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task A_session_missing_a_required_field_is_discarded_rather_than_half_built()
    {
        var settings = new SteamWeb.InMemorySettingsRepository();
        var protector = new ReversibleTestProtector();

        // An access token with no refresh token cannot be renewed and no account
        // id means the playtime route cannot be built. Half a session would only
        // move the failure to the first request.
        await settings.SetAsync(
            SettingsEpicTokenStore.SessionSetting,
            protector.Protect("""{"client_id":"c","access_token":"a","refresh_token":"","account_id":""}""")!);

        var store = new SettingsEpicTokenStore(settings, protector);

        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public void The_windows_protector_is_selected_only_on_windows()
    {
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());

        var protector = host.Resolve<IEpicSecretProtector>();

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<DpapiEpicSecretProtector>(protector);
            Assert.True(protector.IsAvailable);
            Assert.Equal("dpapi:CurrentUser", protector.Name);
        }
        else
        {
            // Not a plaintext store. A refusal.
            Assert.IsType<UnavailableEpicSecretProtector>(protector);
            Assert.False(protector.IsAvailable);
        }
    }

    [Fact]
    public void Dpapi_round_trips_a_session_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var protector = new DpapiEpicSecretProtector();

        var cipher = protector.Protect(RefreshToken);

        Assert.NotNull(cipher);
        Assert.DoesNotContain(RefreshToken, cipher, StringComparison.Ordinal);
        Assert.Equal(RefreshToken, protector.Unprotect(cipher!));

        // Garbage in, null out — never an exception.
        Assert.Null(protector.Unprotect("not base64 at all !!"));
        Assert.Null(protector.Unprotect(Convert.ToBase64String([1, 2, 3, 4])));
    }

    /// <summary>
    /// A reversible stand-in for DPAPI, so the storage tests assert the
    /// <i>shape</i> of protection — that nothing readable is written and that it
    /// round-trips — without depending on a real Windows user profile.
    ///
    /// <para>Base64 is not encryption and is not pretending to be. The test that
    /// matters for real encryption is
    /// <see cref="Dpapi_round_trips_a_session_on_windows"/>; this one exists so
    /// the store's own logic is testable anywhere.</para>
    /// </summary>
    private sealed class ReversibleTestProtector : IEpicSecretProtector
    {
        public bool FailToUnprotect { get; init; }

        public bool IsAvailable => true;

        public string Name => "test:reversible";

        public string? Protect(string plaintext)
            => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));

        public string? Unprotect(string protectedBase64)
        {
            if (FailToUnprotect)
            {
                return null;
            }

            try
            {
                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedBase64));
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}

/// <summary>
/// The composition itself, which several security properties depend on.
/// </summary>
public sealed class EpicRegistrationTests
{
    [Fact]
    public void The_rate_limiter_is_shared_across_both_clients()
    {
        // A per-client limiter would multiply the ceiling by the number of typed
        // clients — the token client and the library client would each get the
        // full budget, and the account would walk into a throttle nobody chose.
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());

        Assert.Same(host.Resolve<EpicRateLimiter>(), host.Resolve<EpicRateLimiter>());
    }

    [Fact]
    public void The_token_provider_is_a_singleton()
    {
        // Two providers would each hold their own cached session and each spend
        // the refresh token, which Epic rotates on use.
        using var host = new EpicWebTestHost(EpicWebTestHost.Healthy());

        Assert.Same(host.Resolve<IEpicTokenProvider>(), host.Resolve<IEpicTokenProvider>());
    }

    [Fact]
    public async Task Registering_the_module_without_credentials_makes_no_requests_on_any_call()
    {
        // The property that makes it safe to register unconditionally in
        // Program.cs, exactly like AddSteamWebApi.
        using var host = new EpicWebTestHost(
            EpicWebTestHost.Healthy(), clientId: null, clientSecret: null);

        await host.Client.IsConfiguredAsync();
        await host.Client.IsSignedInAsync();
        await host.Client.GetOwnedLibraryAsync();
        await host.Client.GetOwnershipCandidatesAsync();
        await host.Client.AuthorizationCodeUrl();
        await host.Client.SignOutAsync();

        Assert.Empty(host.Handler.Requests);
    }

    [Fact]
    public async Task A_host_with_no_settings_repository_still_works_in_memory()
    {
        // Hoard.Ingest.Epic does not reference Hoard.Data, so ISettingsRepository
        // is resolved optionally. Its absence must degrade to "no persistence",
        // never to a DI resolution failure at startup.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEpicWebApi();
        services.AddHttpClient<IEpicAccountClient, EpicAccountClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new FakeEpicHandler(EpicWebTestHost.Healthy()));

        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IEpicAccountClient>();

        Assert.False(await client.IsConfiguredAsync());
        Assert.Null(provider.GetService<ISettingsRepository>());
        Assert.False(provider.GetRequiredService<IEpicTokenStore>().CanPersist);
    }
}
