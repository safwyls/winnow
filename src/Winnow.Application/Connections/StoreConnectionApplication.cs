using System.Globalization;
using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;
using Winnow.Core.Auth;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Ingest.Epic.Web;
using Winnow.Ingest.Epic.Web.Auth;
using Winnow.Ingest.Epic.Web.Credentials;

namespace Winnow.Application.Connections;

public sealed class StoreConnectionApplication(ISteamSessionProvider steamSessions,
    ISteamCredentialProvider steamCredentials, ISteamApiKeyStore steamKeys, ISteamAccountConfirmation confirmation,
    IEpicAccountClient epic, IEpicTokenStore epicStore, IEpicTokenProvider epicTokens,
    IEpicCredentialProvider epicCredentials, EpicInteractiveSignIn epicSignIn,
    OwnershipRefreshRequests refresh, IApplicationChangePublisher changes, TimeProvider clock)
{
    private sealed record Attempt(string ClientId, string Provider, DateTimeOffset ExpiresAt, object Request);
    private readonly object _gate = new();
    private readonly Dictionary<string, Attempt> _attempts = [];

    public async Task<StoreConnectionSnapshot> GetAsync(CancellationToken ct)
    {
        var inventory = await steamCredentials.GetInventoryAsync(ct);
        var connection = new SteamConnection(inventory.HasApiKey,
            inventory.ApiKeySource == SettingsTableApiKeySource.SourceName, inventory.HasSession,
            inventory.SessionUsable, inventory.SessionExpiresAt, inventory.SessionAccount?.Value.ToString(CultureInfo.InvariantCulture));
        var live = await epic.IsSignedInAsync(ct);
        var stored = await epicStore.LoadAsync(ct);
        return new(connection, await steamSessions.GetHealthAsync(ct),
            live || stored is not null ? new StoreSession(live, stored?.DisplayName) : null);
    }

    public async Task<SteamApiKeySaveOutcome> SaveSteamKeyAsync(string? key, CancellationToken ct)
    {
        if (key is { Length: > 4096 } || key?.Any(char.IsControl) == true) throw new ArgumentException("Invalid API key.");
        var result = await steamKeys.SaveAsync(key?.Trim() ?? string.Empty, ct);
        if (result == SteamApiKeySaveOutcome.Stored)
        {
            steamCredentials.Invalidate();
            await confirmation.ReconcileAsync(ct);
            Changed("steam");
        }
        return result;
    }

    public SteamAuthChallenge BeginSteam(SteamAuthBegin request)
    {
        ArgumentNullException.ThrowIfNull(request.Request);
        if (!request.Request.ConsentGranted) throw new ArgumentException("Steam sign-in requires consent.");
        if (request.Request.Timeout <= TimeSpan.Zero || request.Request.Timeout > TimeSpan.FromMinutes(15))
            throw new ArgumentException("Sign-in timeout must be between zero and fifteen minutes.");
        var expires = clock.GetUtcNow() + request.Request.Timeout;
        var id = Add(request.ClientId, "steam", expires, request.Request);
        return new(id, expires, request.Request);
    }

    public async Task<SteamSignInReport> CompleteSteamAsync(SteamAuthComplete request, CancellationToken ct)
    {
        var attempt = Consume(request.ClientId, request.AttemptId, "steam");
        var options = (SteamSignInRequest)attempt.Request;
        if (request.AccessToken is null or { Length: > 32768 } || request.RefreshToken is { Length: > 32768 })
            throw new ArgumentException("Invalid Steam credentials.");
        var session = SteamSession.TryCreate(request.AccessToken, options.StaySignedIn ? request.RefreshToken : null, clock.GetUtcNow());
        if (session is null || session.SteamId.Value.ToString(CultureInfo.InvariantCulture) != request.SteamId)
            throw new ArgumentException("Steam account identity did not match the captured session.");
        await steamSessions.SaveAsync(session, ct);
        var confirmed = await confirmation.ConfirmAsync(session.SteamId, SteamAccountConfirmationSource.Session, ct);
        var health = await steamSessions.GetHealthAsync(ct);
        Changed("steam");
        return new(SteamSignInOutcome.SignedIn,
            session.HasRefreshToken ? null : "Steam issued no refresh token. This session cannot be renewed and will need a fresh sign-in when it expires.",
            request.SteamId, session.ExpiresAt, session.HasRefreshToken, health != SteamSessionHealth.NotPersisted, health, null, confirmed);
    }

    public async Task<EpicAuthChallenge> BeginEpicAsync(string clientId, CancellationToken ct)
    {
        var credentials = await epicCredentials.GetAsync(ct) ?? throw new ApplicationConflictException("Epic sign-in is not configured.");
        var request = epicSignIn.BuildRequest(credentials.ClientId);
        var expires = clock.GetUtcNow().Add(request.Timeout);
        return new(Add(clientId, "epic", expires, request), expires, request);
    }

    public async Task<EpicSignInResult> CompleteEpicAsync(EpicAuthComplete request, CancellationToken ct)
    {
        var attempt = Consume(request.ClientId, request.AttemptId, "epic");
        var prompt = (AuthPromptRequest)attempt.Request;
        if (prompt.ExpectedState is not null && !AuthState.Matches(prompt.ExpectedState, request.State))
            throw new ArgumentException("The sign-in state did not match this attempt.");
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 4096 || request.Code.Any(char.IsControl) || !Enum.IsDefined(request.Kind))
            throw new ArgumentException("Invalid authorization code.");
        var result = request.Kind == AuthCodeKind.ExchangeCode
            ? await epicTokens.SignInWithExchangeCodeAsync(request.Code, ct)
            : await epicTokens.SignInWithAuthorizationCodeAsync(request.Code, ct);
        if (result.Succeeded) Changed("epic");
        return result;
    }

    public void Cancel(StoreAuthCancel request)
    {
        lock (_gate)
        {
            if (_attempts.TryGetValue(request.AttemptId, out var attempt) && attempt.ClientId == request.ClientId)
                _attempts.Remove(request.AttemptId);
        }
    }

    public async Task SignOutSteamAsync(CancellationToken ct)
    {
        CancelProvider("steam");
        await steamSessions.SignOutAsync(ct);
        await confirmation.ReconcileAsync(ct);
        Changed("steam");
    }

    public async Task SignOutEpicAsync(CancellationToken ct)
    {
        CancelProvider("epic");
        await epic.SignOutAsync(ct);
        Changed("epic");
    }

    private void Changed(string provider) { changes.Publish("connections.changed", provider); refresh.Request(); }
    private string Add(string clientId, string provider, DateTimeOffset expires, object request)
    {
        if (!Guid.TryParseExact(clientId, "N", out _)) throw new ArgumentException("A frontend instance ID is required.");
        lock (_gate)
        {
            foreach (var stale in _attempts.Where(a => a.Value.ExpiresAt <= clock.GetUtcNow()).Select(a => a.Key).ToArray()) _attempts.Remove(stale);
            if (_attempts.Count >= 256 || _attempts.Values.Count(a => a.ClientId == clientId) >= 8)
                throw new ApplicationConflictException("Too many pending sign-in attempts.");
            var id = Guid.NewGuid().ToString("N");
            _attempts.Add(id, new(clientId, provider, expires, request));
            return id;
        }
    }

    private Attempt Consume(string clientId, string id, string provider)
    {
        lock (_gate)
        {
            if (!_attempts.TryGetValue(id, out var attempt) || attempt.ClientId != clientId || attempt.Provider != provider || attempt.ExpiresAt <= clock.GetUtcNow())
                throw new ApplicationNotFoundException("The sign-in attempt is unavailable or expired.");
            _attempts.Remove(id);
            return attempt;
        }
    }

    private void CancelProvider(string provider)
    {
        lock (_gate)
            foreach (var id in _attempts.Where(a => a.Value.Provider == provider).Select(a => a.Key).ToArray()) _attempts.Remove(id);
    }
}
