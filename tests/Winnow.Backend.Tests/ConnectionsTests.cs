using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;
using Winnow.Core.Repositories;
using Winnow.PluginSdk;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class ConnectionsTests
{
    [Fact]
    public async Task SignInAttemptsAreClientScopedAndProviderAttemptIdsStayPrivate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-backend-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var provider = new TestPluginSettings();
        try
        {
            await using var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], services =>
                services.AddSingleton<IPluginSettingsBackend>(provider));
            await app.StartAsync();
            using var first = WinnowApiClient.Attach(directory);
            using var second = WinnowApiClient.Attach(directory);
            var firstId = Guid.NewGuid().ToString("N");
            var secondId = Guid.NewGuid().ToString("N");
            var response = await first.SendAsync<PluginSignInRequest, PluginChallengeResponse>(HttpMethod.Post,
                "connections/plugins/example/sign-in", new(firstId));
            var challenge = Assert.IsType<PluginSignInChallenge>(response.Challenge);
            Assert.NotEqual("provider-private-attempt", challenge.AttemptId);
            var rejected = await Assert.ThrowsAsync<BackendApiException>(() => second.SendAsync<PluginSignInAttempt, PluginSignInResult>(HttpMethod.Post,
                "connections/plugins/example/sign-in/poll", new(secondId, challenge.AttemptId)));
            Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
            Assert.Equal(0, provider.Polls);
            var result = await first.SendAsync<PluginSignInAttempt, PluginSignInResult>(HttpMethod.Post,
                "connections/plugins/example/sign-in/poll", new(firstId, challenge.AttemptId));
            Assert.Equal(PluginSignInState.Connected, result.State);
            Assert.Equal("provider-private-attempt", provider.LastPolledId);
            await app.StopAsync();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task PreferencesCannotReadOrWriteSecretStorage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-backend-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            await app.StartAsync();
            var settings = app.Services.GetRequiredService<ISettingsRepository>();
            await settings.SetAsync("epic.session", "private-secret-marker");
            using var client = WinnowApiClient.Attach(directory);
            var response = await client.GetAsync<System.Text.Json.JsonElement>("preferences/presentation");
            Assert.DoesNotContain("private-secret-marker", response.GetRawText());
            Assert.DoesNotContain("epic.session", response.GetRawText());
            var invalid = await Assert.ThrowsAsync<BackendApiException>(() => client.SendAsync(HttpMethod.Put,
                "preferences/presentation/epic.session", new { value = "replaced" }));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("private-secret-marker", await settings.GetAsync("epic.session"));
            await app.StopAsync();
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class TestPluginSettings : IPluginSettingsBackend
    {
        public string UserPluginDirectory => "";
        public int Polls { get; private set; }
        public string? LastPolledId { get; private set; }
        public Task<IReadOnlyList<PluginSettingsSnapshot>> LoadAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PluginSettingsSnapshot>>([]);
        public Task SaveAsync(string pluginId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveSecretAsync(string pluginId, string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
        public Task RefreshAsync(string pluginId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<PluginSignInChallenge?> BeginSignInAsync(string pluginId, CancellationToken ct = default) =>
            Task.FromResult<PluginSignInChallenge?>(new("provider-private-attempt", "https://example.com", "ABC-123", DateTimeOffset.UtcNow.AddMinutes(10), 2));
        public Task<PluginSignInResult> PollSignInAsync(string pluginId, string attemptId, CancellationToken ct = default)
        {
            Polls++; LastPolledId = attemptId;
            return Task.FromResult(new PluginSignInResult(PluginSignInState.Connected, ""));
        }
    }
}
