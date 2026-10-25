using Winnow.App.Services;
using Winnow.Core.Auth;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Ingest.Epic.Web.Auth;

namespace Winnow.Api.Contracts.Connections;

public sealed record StoreConnectionSnapshot(SteamConnection Steam, SteamSessionHealth SteamHealth, StoreSession? Epic);
public sealed record SteamAuthBegin(string ClientId, SteamSignInRequest Request);
public sealed record SteamAuthChallenge(string AttemptId, DateTimeOffset ExpiresAt, SteamSignInRequest Request);
public sealed record SteamAuthComplete(string ClientId, string AttemptId, string SteamId, string AccessToken, string? RefreshToken)
{
    public override string ToString() => "SteamAuthComplete(credentials redacted)";
}
public sealed record EpicAuthChallenge(string AttemptId, DateTimeOffset ExpiresAt, AuthPromptRequest Request);
public sealed record EpicAuthComplete(string ClientId, string AttemptId, string Code, AuthCodeKind Kind, string? State)
{
    public override string ToString() => "EpicAuthComplete(code redacted)";
}
public sealed record StoreAuthCancel(string ClientId, string AttemptId);
public sealed record SaveSteamApiKey(string? Key)
{
    public override string ToString() => "SaveSteamApiKey(key redacted)";
}

public interface IStoreConnectionApi
{
    Task<StoreConnectionSnapshot> GetAsync(CancellationToken ct = default);
    Task<SteamApiKeySaveOutcome> SaveSteamKeyAsync(string? key, CancellationToken ct = default);
    Task<SteamAuthChallenge> BeginSteamAsync(SteamSignInRequest request, CancellationToken ct = default);
    Task<SteamSignInReport> CompleteSteamAsync(SteamAuthChallenge challenge, SteamSignInResult result, CancellationToken ct = default);
    Task<EpicAuthChallenge> BeginEpicAsync(CancellationToken ct = default);
    Task<EpicSignInResult> CompleteEpicAsync(EpicAuthChallenge challenge, AuthCodeResult result, CancellationToken ct = default);
    Task CancelAsync(string attemptId, CancellationToken ct = default);
    Task SignOutSteamAsync(CancellationToken ct = default);
    Task SignOutEpicAsync(CancellationToken ct = default);
}
