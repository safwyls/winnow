using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;
using Winnow.Core.Auth;
using Winnow.Core.Repositories;
using Winnow.Enrich.SteamWeb.Credentials;
using Xunit;

namespace Winnow.Backend.Tests;

/// <summary>Exercises the production HTTP coordinator, session provider and encrypted settings store.</summary>
public sealed class SteamSessionParityTests
{
    private const string Account = "76561198000000001";

    [Fact]
    public async Task Minted_session_reaches_the_real_selector_and_survives_backend_restart()
    {
        await using var host = await Host.Start();
        var options = new SteamSignInRequest { ConsentGranted = true, CapturePurchaseHistory = false,
            StaySignedIn = true, MaxLoadMoreClicks = 7, MaxLicensesPages = 3, Timeout = TimeSpan.FromMinutes(9) };
        var challenge = await host.Begin(options);
        Assert.Equal(options, challenge.Request);
        var expires = host.Clock.Now.AddHours(24);
        var access = Token(expires);
        var refresh = Token(host.Clock.Now.AddDays(30));
        var report = await host.Complete(challenge, access, refresh);
        Assert.True(report.SignedIn);
        Assert.True(report.Persisted);
        Assert.True(report.AccountConfirmed);
        Assert.True(report.RefreshTokenCaptured);
        Assert.Null(report.Pages);
        Assert.Equal(Account, report.SteamId);
        Assert.Equal(expires, report.ExpiresAt);
        Assert.Equal(1, host.Refreshes);
        var credentials = host.Service<ISteamCredentialProvider>();
        var chosen = await credentials.GetCurrentAsync(SteamCredentialPurpose.UserInitiated);
        Assert.NotNull(chosen);
        Assert.Equal(SteamCredentialKind.SessionToken, chosen.Kind);
        Assert.Equal(access, chosen.Value);
        var stored = await host.Stored();
        Assert.False(string.IsNullOrEmpty(stored));
        Assert.DoesNotContain(access, stored);
        Assert.DoesNotContain(refresh, stored);
        Assert.DoesNotContain(Account, stored);
        using (var payload = JsonDocument.Parse(host.Protector.Unprotect(stored!)!))
            Assert.Equal(new[] { "access_token", "audience", "expires_at", "issuer", "last_failure_kind",
                "last_renewed_at", "minted_at", "refresh_expires_at", "refresh_token", "renewal_failures", "steamid64" },
                payload.RootElement.EnumerateObject().Select(field => field.Name).Order().ToArray());
        Assert.DoesNotContain(access, JsonSerializer.Serialize(report));
        Assert.DoesNotContain(refresh, JsonSerializer.Serialize(report));
        await host.Restart();
        Assert.Equal(stored, await host.Stored());
        var snapshot = await host.Snapshot();
        Assert.True(snapshot.Steam.HasSession);
        Assert.True(snapshot.Steam.SessionUsable);
        Assert.False(snapshot.Steam.HasApiKey);
        Assert.True(snapshot.Steam.HasUsableCredential);
        Assert.Equal(expires, snapshot.Steam.SessionExpiresAt);
        Assert.Equal(SteamSessionHealth.Live, snapshot.SteamHealth);
        Assert.Equal(Account, snapshot.Steam.SessionAccount);
        Assert.True((await host.Visibility()).AccountConfirmed);
        Assert.Equal(access, (await host.Service<ISteamSessionCredentialSource>().TryGetAsync())?.Value);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, " ")]
    [InlineData(false, "provided-but-not-consented")]
    public async Task Token_only_session_is_kept_explained_live_then_expired_and_can_reconnect(bool staySignedIn, string? refresh)
    {
        await using var host = await Host.Start();
        var start = host.Clock.Now;
        var challenge = await host.Begin(new() { ConsentGranted = true, StaySignedIn = staySignedIn });
        var report = await host.Complete(challenge, Token(start.AddHours(24)), refresh);
        Assert.True(report.SignedIn);
        Assert.True(report.Persisted);
        Assert.False(report.RefreshTokenCaptured);
        Assert.Contains("refresh token", report.Detail);
        Assert.Contains("cannot be renewed", report.Detail);
        Assert.False((await host.Service<ISteamSessionStore>().LoadAsync())!.HasRefreshToken);
        host.Clock.Now = start.AddHours(23).AddMinutes(30);
        Assert.Equal(SteamSessionHealth.Live, (await host.Snapshot()).SteamHealth);
        host.Clock.Now = start.AddHours(25);
        var expired = await host.Snapshot();
        Assert.True(expired.Steam.HasSession);
        Assert.False(expired.Steam.SessionUsable);
        Assert.True(expired.Steam.HasAnyCredential);
        Assert.False(expired.Steam.HasUsableCredential);
        Assert.Equal(Account, expired.Steam.SessionAccount);
        Assert.Equal(SteamSessionHealth.Expired, expired.SteamHealth);
        Assert.Null(await host.Service<ISteamCredentialProvider>().GetCurrentAsync(SteamCredentialPurpose.UserInitiated));
        var replacement = await host.Complete(await host.Begin(), Token(host.Clock.Now.AddHours(24)), Token(host.Clock.Now.AddDays(30)));
        Assert.True(replacement.RefreshTokenCaptured);
        Assert.Equal(SteamSessionHealth.Live, replacement.Health);
        Assert.True((await host.Snapshot()).Steam.SessionUsable);
        Assert.Equal(2, host.Refreshes);
    }

    [Fact]
    public async Task Sign_out_clears_memory_disk_identity_and_pending_attempts_and_requests_refresh()
    {
        await using var host = await Host.Start();
        await host.Complete(await host.Begin(), Token(host.Clock.Now.AddHours(24)), Token(host.Clock.Now.AddDays(30)));
        var pending = await host.Begin();
        await host.Api.SendAsync<object?>(HttpMethod.Post, "connections/stores/steam/sign-out", null);
        Assert.Equal(2, host.Refreshes);
        Assert.Null(await host.Service<ISteamSessionProvider>().GetAsync());
        Assert.Null(await host.Service<ISteamSessionStore>().LoadAsync());
        Assert.True(string.IsNullOrEmpty(await host.Stored()));
        var snapshot = await host.Snapshot();
        Assert.False(snapshot.Steam.HasSession);
        Assert.Null(snapshot.Steam.SessionAccount);
        Assert.Equal(SteamSessionHealth.NotSignedIn, snapshot.SteamHealth);
        Assert.False((await host.Visibility()).AccountConfirmed);
        var rejected = await Assert.ThrowsAsync<BackendApiException>(() => host.Complete(pending, Token(host.Clock.Now.AddHours(24)), null));
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        await host.Restart();
        Assert.False((await host.Snapshot()).Steam.HasSession);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refused_or_cancelled_sign_in_changes_no_session_and_requests_no_refresh(bool cancelled)
    {
        await using var host = await Host.Start();
        var challenge = await host.Begin();
        if (cancelled)
            await host.Api.SendAsync(HttpMethod.Post, "connections/stores/sign-in/cancel", new StoreAuthCancel(host.ClientId, challenge.AttemptId));
        var failure = await Assert.ThrowsAsync<BackendApiException>(() => host.Complete(challenge,
            Token(host.Clock.Now.AddHours(24), "76561198000000002"), null));
        Assert.Equal(cancelled ? HttpStatusCode.NotFound : HttpStatusCode.BadRequest, failure.StatusCode);
        Assert.Equal(0, host.Refreshes);
        Assert.Null(await host.Service<ISteamSessionProvider>().GetAsync());
        Assert.True(string.IsNullOrEmpty(await host.Stored()));
        Assert.False((await host.Visibility()).AccountConfirmed);
    }

    [Fact]
    public async Task Unavailable_encryption_keeps_the_session_for_this_run_and_reports_that_it_will_not_persist()
    {
        await using var host = await Host.Start(canEncrypt: false);
        var access = Token(host.Clock.Now.AddHours(24));
        var report = await host.Complete(await host.Begin(), access, Token(host.Clock.Now.AddDays(30)));
        Assert.True(report.SignedIn);
        Assert.False(report.Persisted);
        Assert.Equal(SteamSessionHealth.NotPersisted, report.Health);
        Assert.Equal(access, (await host.Service<ISteamSessionProvider>().GetAsync())!.AccessToken);
        Assert.True(string.IsNullOrEmpty(await host.Stored()));
        await host.Restart();
        Assert.False((await host.Snapshot()).Steam.HasSession);
    }

    private static string Token(DateTimeOffset expires, string account = Account)
    {
        static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Encode("{\"alg\":\"none\"}")}.{Encode(JsonSerializer.Serialize(new { sub = account, exp = expires.ToUnixTimeSeconds(), aud = new[] { "web:store" }, iss = "steam" }))}.fixture";
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    // Tests replace only platform encryption, keeping the production store and selector.
    private sealed class Protector(bool available) : ISteamSecretProtector
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        public bool IsAvailable => available;
        public string Name => "test AES-GCM";
        public string? Protect(string plaintext)
        {
            if (!IsAvailable) return null;
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var encrypted = new byte[bytes.Length];
            using var aes = new AesGcm(_key, tag.Length);
            aes.Encrypt(nonce, bytes, encrypted, tag);
            return Convert.ToBase64String([.. nonce, .. tag, .. encrypted]);
        }
        public string? Unprotect(string value)
        {
            if (!IsAvailable) return null;
            var bytes = Convert.FromBase64String(value);
            var plaintext = new byte[bytes.Length - 28];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "winnow-steam-session-tests", Guid.NewGuid().ToString("N"));
        private WebApplication _app = null!;
        public WinnowApiClient Api { get; private set; } = null!;
        public Clock Clock { get; } = new();
        public Protector Protector { get; }
        public string ClientId { get; } = Guid.NewGuid().ToString("N");
        public int Refreshes { get; private set; }
        private Host(bool canEncrypt) => Protector = new(canEncrypt);
        public static async Task<Host> Start(bool canEncrypt = true)
        {
            var host = new Host(canEncrypt);
            Directory.CreateDirectory(host._directory);
            try { await host.Open(); return host; }
            catch { await host.DisposeAsync(); throw; }
        }
        private async Task Open()
        {
            _app = BackendApplication.Build(["--data-dir", _directory, "--no-sync"], services =>
            {
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<ISteamSecretProtector>(Protector);
                // Never consult the developer's optional credentials during an isolated test.
                services.RemoveAll<ISteamApiKeySource>();
                services.AddSingleton<ISteamApiKeySource, SettingsTableApiKeySource>();
            });
            await _app.StartAsync();
            Api = WinnowApiClient.Attach(_directory);
            Service<OwnershipRefreshRequests>().Requested += () => Refreshes++;
        }
        public T Service<T>() where T : notnull => _app.Services.GetRequiredService<T>();
        public Task<string?> Stored() => Service<ISettingsRepository>().GetAsync(SettingsSteamSessionStore.SessionSetting);
        public Task<StoreConnectionSnapshot> Snapshot() => Api.GetAsync<StoreConnectionSnapshot>("connections/stores");
        public Task<AccountVisibilityState> Visibility() => Api.GetAsync<AccountVisibilityState>("connections/account-visibility");
        public Task<SteamAuthChallenge> Begin(SteamSignInRequest? options = null) => Api.SendAsync<SteamAuthBegin, SteamAuthChallenge>(HttpMethod.Post,
            "connections/stores/steam/sign-in", new(ClientId, options ?? new SteamSignInRequest { ConsentGranted = true }));
        public Task<SteamSignInReport> Complete(SteamAuthChallenge challenge, string access, string? refresh) => Api.SendAsync<SteamAuthComplete, SteamSignInReport>(HttpMethod.Post,
            "connections/stores/steam/sign-in/complete", new(ClientId, challenge.AttemptId, Account, access, refresh));
        public async Task Restart()
        {
            Api.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            await Open();
        }
        public async ValueTask DisposeAsync()
        {
            Api?.Dispose();
            if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }
}
