using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;
using Winnow.Enrich.Igdb.Auth;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.Igdb.Storage;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class IgdbRuntimeHttpParityTests
{
    [Fact]
    public async Task HTTP_save_activates_cached_absence_and_rotates_the_same_client_on_the_existing_runtime_singleton()
    {
        await using var host = await Host.StartAsync(fallback: false, legacySaved: false);
        var tokenProvider = host.Tokens;
        Assert.Same(tokenProvider, host.App.Services.GetRequiredService<IIgdbCredentialUpdater>());
        Assert.Same(tokenProvider, host.App.Services.GetRequiredService<TwitchTokenProvider>());
        Assert.Same(host.App.Services.GetRequiredService<IgdbSettingsService>(), host.App.Services.GetRequiredService<IIgdbSettingsService>());
        Assert.Null(await tokenProvider.GetAsync());
        Assert.Empty(host.Remote.Bodies);

        Assert.Equal(IgdbSettingsSaveResult.Saved, await host.Save("same-client", "first-secret"));
        var first = Assert.IsType<IgdbAccessToken>(await tokenProvider.GetAsync());
        Assert.Equal("same-client", first.ClientId);
        Assert.Contains("client_secret=first-secret", Assert.Single(host.Remote.Bodies));
        Assert.Equal(new IgdbSettingsSnapshot("same-client", true, true, false), await host.Snapshot());

        Assert.Equal(IgdbSettingsSaveResult.Saved, await host.Save("same-client", "second-secret"));
        var second = Assert.IsType<IgdbAccessToken>(await tokenProvider.GetAsync());
        Assert.Equal("same-client", second.ClientId);
        Assert.NotEqual(first.AccessToken, second.AccessToken);
        Assert.Equal(2, host.Changes);
        Assert.Equal(2, host.Remote.Bodies.Count);
        Assert.Contains("client_secret=second-secret", host.Remote.Bodies.Last());
        Assert.Same(tokenProvider, host.Tokens);
        Assert.Equal(second, await tokenProvider.GetAsync());
        Assert.Equal(2, host.Remote.Bodies.Count);
        Assert.Null(await host.Store.GetAsync(SettingsTableCredentialSource.ClientSecretKey));
        var publicSnapshot = (await host.Api.GetAsync<JsonElement>("connections/igdb")).GetRawText();
        Assert.DoesNotContain("first-secret", publicSnapshot);
        Assert.DoesNotContain("second-secret", publicSnapshot);
        Assert.DoesNotContain(first.AccessToken, publicSnapshot);
        Assert.DoesNotContain(second.AccessToken, publicSnapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HTTP_removal_immediately_uses_the_same_configuration_fallback_or_disables_the_warm_token_provider(bool fallback)
    {
        await using var host = await Host.StartAsync(fallback, legacySaved: true);
        var oldToken = Assert.IsType<IgdbAccessToken>(await host.Tokens.GetAsync());
        Assert.Equal("test-client", oldToken.ClientId);
        Assert.Contains("client_secret=test-secret", Assert.Single(host.Remote.Bodies));
        Assert.Equal(string.Empty, await host.Store.GetAsync(SettingsTableCredentialSource.ClientSecretKey));
        Assert.NotNull(await host.Store.GetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey));
        Assert.Equal(new IgdbSettingsSnapshot("test-client", true, true, fallback), await host.Snapshot());

        Assert.Equal(fallback, await host.Api.SendAsync<object?, bool>(HttpMethod.Delete, "connections/igdb", null));
        Assert.Equal(1, host.Changes);
        Assert.Null(await host.Store.GetAsync(SettingsTableCredentialSource.ClientIdKey));
        Assert.Null(await host.Store.GetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey));
        Assert.Null(await host.Store.GetAsync(TwitchTokenProvider.TokenBlobKey));
        var current = await host.Tokens.RefreshAsync(oldToken);
        if (fallback)
        {
            Assert.Equal("external-client", current?.ClientId);
            Assert.NotEqual(oldToken.AccessToken, current?.AccessToken);
            Assert.Equal(2, host.Remote.Bodies.Count);
            Assert.Contains("client_secret=external-secret", host.Remote.Bodies.Last());
            Assert.Equal("external-client", host.Configuration["Igdb:ClientId"]);
            Assert.Equal("external-secret", host.Configuration["Igdb:ClientSecret"]);
        }
        else
        {
            Assert.Null(current);
            Assert.Null(await host.Store.GetAsync(TwitchTokenProvider.TokenBlobKey));
            Assert.Single(host.Remote.Bodies);
        }
        Assert.Equal(new IgdbSettingsSnapshot(string.Empty, false, false, fallback), await host.Snapshot());
        var publicSnapshot = (await host.Api.GetAsync<JsonElement>("connections/igdb")).GetRawText();
        Assert.DoesNotContain("external-client", publicSnapshot);
        Assert.DoesNotContain("external-secret", publicSnapshot);
        Assert.DoesNotContain(oldToken.AccessToken, publicSnapshot);
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "winnow-igdb-runtime-http-" + Guid.NewGuid().ToString("N"));
        public WebApplication App { get; private set; } = null!;
        public WinnowApiClient Api { get; private set; } = null!;
        public IConfiguration Configuration { get; private set; } = null!;
        public TokenRemote Remote { get; } = new();
        public ISettingsStore Store => App.Services.GetRequiredService<ISettingsStore>();
        public IIgdbTokenProvider Tokens => App.Services.GetRequiredService<IIgdbTokenProvider>();
        public int Changes { get; private set; }

        public static async Task<Host> StartAsync(bool fallback, bool legacySaved)
        {
            var host = new Host
            {
                Configuration = new ConfigurationBuilder().AddInMemoryCollection(fallback
                    ? new Dictionary<string, string?> { ["Igdb:ClientId"] = "external-client", ["Igdb:ClientSecret"] = "external-secret" }
                    : []).Build(),
            };
            Directory.CreateDirectory(host._directory);
            try
            {
                host.App = BackendApplication.Build(["--data-dir", host._directory, "--no-sync"], services =>
                {
                    // One controlled configuration feeds both real consumers, excluding machine credentials.
                    services.AddSingleton(host.Configuration);
                    services.AddSingleton<IIgdbSecretProtector, ReversibleProtector>();
                    services.AddSingleton<TimeProvider>(new SourceClock());
                    services.AddHttpClient(TwitchTokenProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => host.Remote);
                });
                if (legacySaved)
                {
                    await host.Store.SetAsync(SettingsTableCredentialSource.ClientIdKey, "test-client");
                    await host.Store.SetAsync(SettingsTableCredentialSource.ClientSecretKey, "test-secret");
                }
                await host.App.StartAsync();
                host.Api = WinnowApiClient.Attach(host._directory);
                host.App.Services.GetRequiredService<IgdbSettingsService>().CredentialsChanged += () => host.Changes++;
                return host;
            }
            catch { await host.DisposeAsync(); throw; }
        }

        public Task<IgdbSettingsSaveResult> Save(string id, string secret)
            => Api.SendAsync<SaveIgdbCredentials, IgdbSettingsSaveResult>(HttpMethod.Put, "connections/igdb", new(id, secret));
        public Task<IgdbSettingsSnapshot> Snapshot() => Api.GetAsync<IgdbSettingsSnapshot>("connections/igdb");
        public async ValueTask DisposeAsync()
        {
            Api?.Dispose();
            if (App is not null) { await App.StopAsync(); await App.DisposeAsync(); }
            Remote.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class TokenRemote : HttpMessageHandler
    {
        public ConcurrentQueue<string> Bodies { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://id.twitch.tv/oauth2/token", request.RequestUri?.AbsoluteUri);
            Bodies.Enqueue(await request.Content!.ReadAsStringAsync(ct));
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"access_token":"fixture-token-{{Bodies.Count}}","expires_in":5184000,"token_type":"bearer"}""", Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class SourceClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    // Matches the original reversible fixture boundary; cryptographic protection is not under test here.
    private sealed class ReversibleProtector : IIgdbSecretProtector
    {
        public bool IsAvailable => true;
        public string Name => "test:reversible";
        public string? Protect(string plaintext) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
        public string? Unprotect(string? protectedBase64)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(protectedBase64 ?? string.Empty)); }
            catch (FormatException) { return null; }
        }
    }
}
