using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;
using Winnow.Core.Auth;
using Winnow.Core.Repositories;
using Winnow.Ingest.Epic.Web;
using Winnow.Ingest.Epic.Web.Auth;
using Winnow.Ingest.Epic.Web.Credentials;
using Xunit;

namespace Winnow.Backend.Tests;

/// <summary>Exercises the Electron-facing HTTP boundary and the real provider token pipeline.</summary>
public sealed partial class EpicSignInParityTests
{
    private const string Code = "SECRET-FIXTURE-CODE";
    private const string Access = "SECRET-FIXTURE-ACCESS";
    private const string Refresh = "SECRET-FIXTURE-REFRESH";

    [Theory]
    [InlineData(AuthCodeKind.AuthorizationCode, "authorization_code", "code", "exchange_code")]
    [InlineData(AuthCodeKind.ExchangeCode, "exchange_code", "exchange_code", "code")]
    public async Task Captured_codes_use_exact_grant_fields_and_never_reach_urls_logs_or_results(
        AuthCodeKind kind, string grant, string field, string absent)
    {
        await using var host = await Host.Start();
        var result = await host.Complete(await host.Begin(), kind);
        Assert.True(result.Succeeded);
        Assert.True(result.Persisted);
        var sent = Assert.Single(host.Handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("application/x-www-form-urlencoded", sent.ContentType);
        Assert.Equal(grant, sent.Form["grant_type"]);
        Assert.Equal(Code, sent.Form[field]);
        Assert.Equal("eg1", sent.Form["token_type"]);
        Assert.False(sent.Form.ContainsKey(absent));
        Assert.Equal(3, sent.Form.Count);
        Assert.DoesNotContain(Code, sent.Address);
        Assert.DoesNotContain(Code, JsonSerializer.Serialize(result));
        Assert.DoesNotContain(Access, JsonSerializer.Serialize(result));
        Assert.DoesNotContain(Refresh, JsonSerializer.Serialize(result));
        Assert.Contains(host.Logs.Lines, line => line.Contains("Signed in to Epic", StringComparison.Ordinal));
        foreach (var secret in new[] { Code, Access, Refresh, "fixture-client-secret" })
            Assert.DoesNotContain(secret, string.Join('\n', host.Logs.Lines));
        Assert.Equal(1, host.Refreshes);
    }

    [Fact]
    public async Task Each_challenge_carries_original_consent_cold_login_redirect_harvest_and_fresh_state()
    {
        await using var host = await Host.Start();
        var first = await host.Begin();
        var second = await host.Begin();
        var request = first.Request;
        Assert.Equal(EpicInteractiveSignIn.ConsentNotice, request.ConsentNotice);
        Assert.Contains("Do not share this code with any 3rd party service.", request.ConsentNotice);
        Assert.Contains("access to your Epic account.", request.ConsentNotice);
        Assert.Contains("Winnow is a 3rd party service", request.ConsentNotice);
        Assert.NotNull(request.RedirectUrl);
        Assert.Equal(new Uri("https://localhost/launcher/authorized"), request.RedirectUrl);
        Assert.Equal("/id/authorize", request.StartUrl.AbsolutePath);
        Assert.NotNull(request.HarvestUrl);
        Assert.Equal("/id/api/redirect", request.HarvestUrl.AbsolutePath);
        var query = QueryHelpers.ParseQuery(request.StartUrl.Query);
        Assert.Equal(request.RedirectUrl.AbsoluteUri, query["redirect_uri"].ToString());
        Assert.False(string.IsNullOrWhiteSpace(request.ExpectedState));
        Assert.Equal("state", request.StateParameter);
        Assert.Equal(request.ExpectedState, query["state"].ToString());
        Assert.NotEqual(request.ExpectedState, second.Request.ExpectedState);
        Assert.NotEqual(first.AttemptId, second.AttemptId);
        Assert.Equal(AuthCaptureStrategies.All, request.Strategies);
        Assert.Equal(host.Clock.Now.Add(request.Timeout), first.ExpiresAt);
        Assert.Empty(host.Handler.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("attacker-state")]
    public async Task Missing_or_mismatched_state_is_consumed_before_any_provider_request(string? state)
    {
        await using var host = await Host.Start();
        var challenge = await host.Begin();
        var rejected = await Assert.ThrowsAsync<BackendApiException>(() => host.Api.SendAsync<EpicAuthComplete, EpicSignInResult>(HttpMethod.Post,
            "connections/stores/epic/sign-in/complete", new(host.ClientId, challenge.AttemptId, Code, AuthCodeKind.AuthorizationCode, state)));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var replay = await Assert.ThrowsAsync<BackendApiException>(() => host.Complete(challenge));
        Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode);
        Assert.Empty(host.Handler.Requests);
        Assert.Equal(0, host.Refreshes);
        Assert.Null((await host.Snapshot()).Epic);
    }

    [Fact]
    public async Task Explicit_code_endpoint_opt_out_retains_harvest_without_requiring_unsent_state()
    {
        await using var host = await Host.Start(authorize: false);
        var challenge = await host.Begin();
        Assert.Equal("/id/api/redirect", challenge.Request.StartUrl.AbsolutePath);
        Assert.Equal(challenge.Request.StartUrl, challenge.Request.HarvestUrl);
        Assert.Null(challenge.Request.ExpectedState);
        Assert.False(QueryHelpers.ParseQuery(challenge.Request.StartUrl.Query).ContainsKey("state"));
        Assert.True((await host.Complete(challenge)).Succeeded);
        Assert.Single(host.Handler.Requests);
    }

    [Theory]
    [InlineData("invalid_grant", EpicSignInFailure.InvalidAuthorizationCode)]
    [InlineData("invalid_client", EpicSignInFailure.InvalidClientCredentials)]
    public async Task Provider_refusals_preserve_specific_remedies_without_reflecting_codes(
        string error, EpicSignInFailure expected)
    {
        await using var host = await Host.Start();
        host.Handler.Error = error;
        var result = await host.Complete(await host.Begin(), AuthCodeKind.ExchangeCode);
        Assert.False(result.Succeeded);
        Assert.Equal(expected, result.Failure);
        Assert.False(result.Persisted);
        Assert.DoesNotContain(Code, JsonSerializer.Serialize(result));
        Assert.DoesNotContain(Code, string.Join('\n', host.Logs.Lines));
        Assert.Null((await host.Snapshot()).Epic);
        Assert.Equal(0, host.Refreshes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Successful_sign_in_persists_only_encrypted_sessions_and_sign_out_clears_identity(bool canEncrypt)
    {
        await using var host = await Host.Start(canEncrypt: canEncrypt);
        var result = await host.Complete(await host.Begin());
        Assert.True(result.Succeeded);
        Assert.Equal(canEncrypt, result.Persisted);
        var stored = await host.Stored();
        if (canEncrypt)
        {
            Assert.False(string.IsNullOrEmpty(stored));
            Assert.DoesNotContain(Access, stored);
            Assert.DoesNotContain(Refresh, stored);
            Assert.Contains(Access, host.Protector.Unprotect(stored!));
        }
        else Assert.True(string.IsNullOrEmpty(stored));
        Assert.True((await host.Snapshot()).Epic?.IsLive);
        await host.Restart();
        Assert.Equal(canEncrypt, (await host.Snapshot()).Epic?.IsLive == true);
        if (!canEncrypt) await host.Complete(await host.Begin());
        var pending = await host.Begin();
        await host.Api.SendAsync<object?>(HttpMethod.Post, "connections/stores/epic/sign-out", null);
        Assert.Null((await host.Snapshot()).Epic);
        Assert.True(string.IsNullOrEmpty(await host.Stored()));
        var rejected = await Assert.ThrowsAsync<BackendApiException>(() => host.Complete(pending));
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("expire")]
    [InlineData("foreign")]
    public async Task Unusable_attempts_never_exchange_a_code(string cause)
    {
        await using var host = await Host.Start();
        var challenge = await host.Begin();
        if (cause == "cancel") await host.Api.SendAsync(HttpMethod.Post,
            "connections/stores/sign-in/cancel", new StoreAuthCancel(host.ClientId, challenge.AttemptId));
        if (cause == "expire") host.Clock.Now = host.Clock.Now.AddHours(1);
        var owner = cause == "foreign" ? Guid.NewGuid().ToString("N") : host.ClientId;
        var rejected = await Assert.ThrowsAsync<BackendApiException>(() => host.Api.SendAsync<EpicAuthComplete, EpicSignInResult>(HttpMethod.Post,
            "connections/stores/epic/sign-in/complete", new(owner, challenge.AttemptId, Code, AuthCodeKind.AuthorizationCode, challenge.Request.ExpectedState)));
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        Assert.Empty(host.Handler.Requests);
        Assert.Equal(0, host.Refreshes);
        if (cause == "foreign") Assert.True((await host.Complete(challenge)).Succeeded);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Credentials : IEpicCredentialProvider
    {
        public ValueTask<EpicClientCredentials?> GetAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(EpicClientCredentials.TryCreate("fixture-client-id", "fixture-client-secret", "test"));
        public void Invalidate() { }
    }
    private sealed record SentRequest(HttpMethod Method, string Address, string? ContentType, Dictionary<string, string> Form);
    private sealed class ProviderHandler(Clock clock) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];
        public string? Error { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct))
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
            Requests.Add(new(request.Method, request.RequestUri!.AbsoluteUri, request.Content.Headers.ContentType?.MediaType, form));
            var body = Error is { } error
                ? JsonSerializer.Serialize(new { error, error_description = Code,
                    errorCode = error == "invalid_client" ? "errors.com.epicgames.account.invalid_client_credentials" : "errors.com.epicgames.account.auth_token.invalid_refresh_token" })
                : JsonSerializer.Serialize(new { access_token = Access, refresh_token = Refresh,
                    expires_at = clock.Now.AddHours(8), refresh_expires_at = clock.Now.AddDays(30),
                    account_id = "00000000000000000000000000000001", client_id = "fixture-client-id", displayName = "Fixture account" });
            return new(Error is null ? HttpStatusCode.OK : HttpStatusCode.BadRequest)
                { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
    private sealed class Protector(bool available) : IEpicSecretProtector
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        public bool IsAvailable => available;
        public string Name => "test AES-GCM";
        public string? Protect(string value)
        {
            if (!available) return null;
            var nonce = RandomNumberGenerator.GetBytes(12);
            var plain = Encoding.UTF8.GetBytes(value);
            var cipher = new byte[plain.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(_key, 16);
            aes.Encrypt(nonce, plain, cipher, tag);
            return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
        }
        public string? Unprotect(string value)
        {
            if (!available) return null;
            var bytes = Convert.FromBase64String(value);
            var plain = new byte[bytes.Length - 28];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain);
            return Encoding.UTF8.GetString(plain);
        }
    }
    private sealed class Logs : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Sink(Lines);
        public void Dispose() { }
        private sealed class Sink(ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => lines.Enqueue(formatter(state, exception));
        }
    }
    private sealed class Host : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "winnow-epic-parity", Guid.NewGuid().ToString("N"));
        public string DirectoryPath => _directory;
        public ConcurrentQueue<string> Routes { get; } = new();
        private WebApplication _app = null!;
        private readonly bool _authorize;
        public Clock Clock { get; } = new();
        public Logs Logs { get; } = new();
        public Protector Protector { get; }
        public ProviderHandler Handler { get; private set; } = null!;
        public WinnowApiClient Api { get; private set; } = null!;
        public string ClientId { get; } = Guid.NewGuid().ToString("N");
        public int Refreshes { get; private set; }
        private Host(bool authorize, bool canEncrypt) { _authorize = authorize; Protector = new(canEncrypt); }
        public static async Task<Host> Start(bool authorize = true, bool canEncrypt = true)
        {
            var host = new Host(authorize, canEncrypt);
            Directory.CreateDirectory(host._directory);
            try { await host.Open(); return host; }
            catch { await host.DisposeAsync(); throw; }
        }
        private async Task Open()
        {
            Handler = new(Clock);
            _app = BackendApplication.Build(["--data-dir", _directory, "--no-sync"], services =>
            {
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<IEpicCredentialProvider, Credentials>();
                services.AddSingleton<IEpicSecretProtector>(Protector);
                var options = (EpicWebOptions)services.Last(item => item.ServiceType == typeof(EpicWebOptions)).ImplementationInstance!;
                options.UseAuthorizeEndpointForSignIn = _authorize;
                options.RequestsPerSecond = 1000;
                services.AddHttpClient(EpicTokenProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Handler);
                services.AddLogging(logging => logging.AddProvider(Logs).AddFilter<Logs>(null, LogLevel.Trace));
            });
            _app.Use(async (context, next) =>
            {
                Routes.Enqueue(context.Request.Path.Value!);
                await next();
            });
            await _app.StartAsync();
            Api = WinnowApiClient.Attach(_directory);
            Service<OwnershipRefreshRequests>().Requested += () => Refreshes++;
        }
        private T Service<T>() where T : notnull => _app.Services.GetRequiredService<T>();
        public Task<string?> Stored() => Service<ISettingsRepository>().GetAsync(SettingsEpicTokenStore.SessionSetting);
        public Task<StoreConnectionSnapshot> Snapshot() => Api.GetAsync<StoreConnectionSnapshot>("connections/stores");
        public Task<EpicAuthChallenge> Begin() => Api.SendAsync<PluginSignInRequest, EpicAuthChallenge>(HttpMethod.Post,
            "connections/stores/epic/sign-in", new(ClientId));
        public Task<EpicSignInResult> Complete(EpicAuthChallenge challenge, AuthCodeKind kind = AuthCodeKind.AuthorizationCode) =>
            Api.SendAsync<EpicAuthComplete, EpicSignInResult>(HttpMethod.Post, "connections/stores/epic/sign-in/complete",
                new(ClientId, challenge.AttemptId, Code, kind, challenge.Request.ExpectedState));
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
