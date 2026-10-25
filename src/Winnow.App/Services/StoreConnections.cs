using Winnow.Api.Contracts.Connections;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Ingest.Epic.Web.Auth;

namespace Winnow.App.Services;

public sealed class StoreConnections(IStoreConnectionApi api, EpicSignInService epic) : IStoreConnections
{
    public async ValueTask<bool> IsSteamWebApiConfiguredAsync(CancellationToken ct = default) => (await GetSteamConnectionAsync(ct)).HasUsableCredential;
    public async ValueTask<SteamConnection> GetSteamConnectionAsync(CancellationToken ct = default) => (await api.GetAsync(ct)).Steam;
    public Task<SteamApiKeySaveOutcome> SaveSteamApiKeyAsync(string? key, CancellationToken ct = default) => api.SaveSteamKeyAsync(key, ct);
    public async Task ClearSteamApiKeyAsync(CancellationToken ct = default) => await api.SaveSteamKeyAsync(null, ct);
    public async ValueTask<StoreSession?> GetEpicSessionAsync(CancellationToken ct = default) => (await api.GetAsync(ct)).Epic;
    public Task SignOutOfEpicAsync(CancellationToken ct = default) => api.SignOutEpicAsync(ct);
    public async Task<StoreSignInOutcome> SignInToEpicAsync(CancellationToken ct = default)
    {
        EpicSignInResult result;
        try { result = await epic.SignInAsync(ct); }
        catch (OperationCanceledException) { return new(false, null, false, StoreSignInProblem.Cancelled, StoreSignInMessages.Cancelled); }
        var problem = result.Failure switch
        {
            EpicSignInFailure.None => StoreSignInProblem.None,
            EpicSignInFailure.Cancelled => StoreSignInProblem.Cancelled,
            EpicSignInFailure.NoInteractivePrompt => StoreSignInProblem.NoPromptAvailable,
            EpicSignInFailure.NoCodeCaptured => StoreSignInProblem.NoCodeCaptured,
            EpicSignInFailure.InvalidAuthorizationCode => StoreSignInProblem.CodeRejected,
            EpicSignInFailure.InvalidClientCredentials => StoreSignInProblem.ClientRejected,
            EpicSignInFailure.Unreachable => StoreSignInProblem.Unreachable,
            EpicSignInFailure.NotConfigured => StoreSignInProblem.NotConfigured,
            _ => StoreSignInProblem.Unexpected
        };
        return new(result.Succeeded, string.IsNullOrWhiteSpace(result.DisplayName) ? null : result.DisplayName.Trim(), result.Persisted, problem, EpicSignInService.Explain(result.Failure));
    }
}
