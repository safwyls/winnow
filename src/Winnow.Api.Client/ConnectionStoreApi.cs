using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;
using Winnow.Core.Auth;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Ingest.Epic.Web.Auth;

namespace Winnow.Api.Client;

public sealed class ConnectionStoreApi(WinnowApiClient api) : IStoreConnectionApi
{
    private readonly string _clientId = Guid.NewGuid().ToString("N");
    public Task<StoreConnectionSnapshot> GetAsync(CancellationToken ct = default) => api.GetAsync<StoreConnectionSnapshot>("connections/stores", ct);
    public Task<SteamApiKeySaveOutcome> SaveSteamKeyAsync(string? key, CancellationToken ct = default) =>
        api.SendAsync<SaveSteamApiKey, SteamApiKeySaveOutcome>(HttpMethod.Put, "connections/stores/steam/key", new(key), ct: ct);
    public Task<SteamAuthChallenge> BeginSteamAsync(SteamSignInRequest request, CancellationToken ct = default) =>
        api.SendAsync<SteamAuthBegin, SteamAuthChallenge>(HttpMethod.Post, "connections/stores/steam/sign-in", new(_clientId, request), ct: ct);
    public async Task<SteamSignInReport> CompleteSteamAsync(SteamAuthChallenge challenge, SteamSignInResult result, CancellationToken ct = default)
    {
        if (!result.HasSession) throw new ArgumentException("A captured Steam session is required.");
        var response = await api.SendAsync<SteamAuthComplete, SteamSignInReport>(HttpMethod.Post, "connections/stores/steam/sign-in/complete",
            new(_clientId, challenge.AttemptId, result.SteamId!, result.AccessToken!, result.RefreshToken), ct: ct);
        return response with { Pages = challenge.Request.CapturePurchaseHistory ? result.Pages : null };
    }
    public Task<EpicAuthChallenge> BeginEpicAsync(CancellationToken ct = default) =>
        api.SendAsync<PluginSignInRequest, EpicAuthChallenge>(HttpMethod.Post, "connections/stores/epic/sign-in", new(_clientId), ct: ct);
    public Task<EpicSignInResult> CompleteEpicAsync(EpicAuthChallenge challenge, AuthCodeResult result, CancellationToken ct = default)
    {
        if (result.Outcome != AuthPromptOutcome.Captured || result.Code is null) throw new ArgumentException("A captured Epic authorization code is required.");
        return api.SendAsync<EpicAuthComplete, EpicSignInResult>(HttpMethod.Post, "connections/stores/epic/sign-in/complete",
            new(_clientId, challenge.AttemptId, result.Code, result.Kind, challenge.Request.ExpectedState), ct: ct);
    }
    public Task CancelAsync(string attemptId, CancellationToken ct = default) =>
        api.SendAsync(HttpMethod.Post, "connections/stores/sign-in/cancel", new StoreAuthCancel(_clientId, attemptId), ct);
    public Task SignOutSteamAsync(CancellationToken ct = default) => api.SendAsync<object?>(HttpMethod.Post, "connections/stores/steam/sign-out", null, ct);
    public Task SignOutEpicAsync(CancellationToken ct = default) => api.SendAsync<object?>(HttpMethod.Post, "connections/stores/epic/sign-out", null, ct);
}
