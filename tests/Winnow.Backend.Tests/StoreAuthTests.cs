using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Connections;
using Winnow.Core.Auth;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class StoreAuthTests
{
    [Fact]
    public async Task SteamChallengesEnforceConsentClientBindingExpiryAndSingleUse()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-backend-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var clock = new TestClock();
        try
        {
            await using var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], services => services.AddSingleton<TimeProvider>(clock));
            await app.StartAsync();
            using var api = WinnowApiClient.Attach(directory);
            var clientId = Guid.NewGuid().ToString("N");
            var denied = await Assert.ThrowsAsync<BackendApiException>(() => api.SendAsync<SteamAuthBegin, SteamAuthChallenge>(HttpMethod.Post,
                "connections/stores/steam/sign-in", new(clientId, new SteamSignInRequest { ConsentGranted = false })));
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            var challenge = await api.SendAsync<SteamAuthBegin, SteamAuthChallenge>(HttpMethod.Post,
                "connections/stores/steam/sign-in", new(clientId, new SteamSignInRequest { ConsentGranted = true }));
            var stolen = await Assert.ThrowsAsync<BackendApiException>(() => api.SendAsync<SteamAuthComplete, object>(HttpMethod.Post,
                "connections/stores/steam/sign-in/complete", new(Guid.NewGuid().ToString("N"), challenge.AttemptId, "0", "invalid-token", null)));
            Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
            var invalid = await Assert.ThrowsAsync<BackendApiException>(() => api.SendAsync<SteamAuthComplete, object>(HttpMethod.Post,
                "connections/stores/steam/sign-in/complete", new(clientId, challenge.AttemptId, "0", "invalid-token", null)));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var replay = await Assert.ThrowsAsync<BackendApiException>(() => api.SendAsync<SteamAuthComplete, object>(HttpMethod.Post,
                "connections/stores/steam/sign-in/complete", new(clientId, challenge.AttemptId, "0", "invalid-token", null)));
            Assert.Equal(HttpStatusCode.NotFound, replay.StatusCode);
            var expiring = await api.SendAsync<SteamAuthBegin, SteamAuthChallenge>(HttpMethod.Post,
                "connections/stores/steam/sign-in", new(clientId, new SteamSignInRequest { ConsentGranted = true }));
            clock.Now = clock.Now.AddHours(1);
            var expired = await Assert.ThrowsAsync<BackendApiException>(() => api.SendAsync<SteamAuthComplete, object>(HttpMethod.Post,
                "connections/stores/steam/sign-in/complete", new(clientId, expiring.AttemptId, "0", "invalid-token", null)));
            Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
            Assert.False((await api.GetAsync<StoreConnectionSnapshot>("connections/stores")).Steam.HasSession);
            await app.StopAsync();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task EpicMismatchedStateIsRejectedBeforeCodeExchange()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-backend-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            await app.StartAsync();
            using var api = WinnowApiClient.Attach(directory);
            var clientId = Guid.NewGuid().ToString("N");
            var challenge = await api.SendAsync<PluginSignInRequest, EpicAuthChallenge>(HttpMethod.Post,
                "connections/stores/epic/sign-in", new(clientId));
            Assert.NotNull(challenge.Request.ExpectedState);
            var invalid = await Assert.ThrowsAsync<BackendApiException>(() => api.SendAsync<EpicAuthComplete, object>(HttpMethod.Post,
                "connections/stores/epic/sign-in/complete", new(clientId, challenge.AttemptId, "never-sent-code", AuthCodeKind.AuthorizationCode, "wrong-state")));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Null((await api.GetAsync<StoreConnectionSnapshot>("connections/stores")).Epic);
            await app.StopAsync();
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
