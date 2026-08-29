namespace Winnow.Ingest.Epic.Web.Auth;

/// <summary>
/// Why a sign-in attempt did not produce a session. Carried instead of an
/// exception because every one of these is an ordinary outcome the caller
/// handles by falling back to the local readers.
/// </summary>
public enum EpicSignInFailure
{
    /// <summary>No failure.</summary>
    None = 0,

    /// <summary>No client id/secret pair is configured, so there was nothing to sign in with.</summary>
    NotConfigured,

    /// <summary>
    /// Epic rejected the client credentials themselves (<c>invalid_client</c>,
    /// numeric 18033 — verified live 2026-08-26). The pair is wrong, not the code.
    /// </summary>
    InvalidClientCredentials,

    /// <summary>Epic rejected the authorization code: mistyped, spent, or expired.</summary>
    InvalidAuthorizationCode,

    /// <summary>Network, DNS, TLS, timeout, or a 5xx the retry policy could not outlast.</summary>
    Unreachable,

    /// <summary>Epic answered with something this client could not parse.</summary>
    UnexpectedResponse,

    /// <summary>The user deliberately cancelled the interactive sign-in.</summary>
    Cancelled,

    /// <summary>No interactive prompt could run on this host.</summary>
    NoInteractivePrompt,

    /// <summary>A prompt ran but produced no code.</summary>
    NoCodeCaptured,

    /// <summary>Epic's code endpoint answered with no signed-in account.</summary>
    NoAuthenticatedSession,
}

/// <summary>The outcome of one sign-in attempt. Never an exception.</summary>
/// <param name="Succeeded">Whether a session now exists.</param>
/// <param name="Failure">Why not, when <paramref name="Succeeded"/> is false.</param>
/// <param name="AccountId">The Epic account id, when it succeeded.</param>
/// <param name="DisplayName">The Epic display name, when it succeeded and Epic supplied one.</param>
/// <param name="Persisted">
/// Whether the session was written to encrypted storage. False means the sign-in
/// holds for this run only — see <see cref="IEpicTokenStore.CanPersist"/>.
/// </param>
public sealed record EpicSignInResult(
    bool Succeeded,
    EpicSignInFailure Failure,
    string? AccountId,
    string? DisplayName,
    bool Persisted)
{
    public static EpicSignInResult Failed(EpicSignInFailure failure)
        => new(false, failure, null, null, false);

    /// <summary>Diagnostics. Carries the outcome, never the account it belongs to.</summary>
    public override string ToString()
        => Succeeded ? "EpicSignInResult(succeeded, account redacted)" : $"EpicSignInResult(failed={Failure})";
}

/// <summary>
/// Owns the Epic OAuth session: exchange, refresh, persistence, and graceful
/// degradation. Returns null from <see cref="GetAsync"/> on any expected failure.
/// </summary>
public interface IEpicTokenProvider
{
    /// <summary>
    /// Whether a client id/secret pair is configured. False is the ordinary
    /// state of an install nobody has opted in on, not an error.
    /// </summary>
    ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>
    /// Whether a session exists that is still worth trying — i.e. a stored
    /// session whose <i>refresh</i> token has not lapsed. Does not make a
    /// request, and does not prove Epic will honour it.
    /// </summary>
    ValueTask<bool> IsSignedInAsync(CancellationToken ct = default);

    /// <summary>Exchanges an authorization code for a session and stores it encrypted.</summary>
    Task<EpicSignInResult> SignInWithAuthorizationCodeAsync(string authorizationCode, CancellationToken ct = default);

    /// <summary>Exchanges a launcher exchange code for a session and stores it encrypted.</summary>
    Task<EpicSignInResult> SignInWithExchangeCodeAsync(string exchangeCode, CancellationToken ct = default);

    /// <summary>
    /// A usable access token, refreshing first if the current one is spent, or
    /// null when there is no session to be had.
    /// </summary>
    Task<EpicOAuthToken?> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Discards <paramref name="staleToken"/> and refreshes. Called by the auth
    /// handler on a 401. Passing the token that failed makes the call idempotent
    /// under concurrency: if another caller already replaced it, this returns the
    /// replacement rather than spending the refresh token a second time.
    /// </summary>
    Task<EpicOAuthToken?> RefreshAsync(EpicOAuthToken? staleToken, CancellationToken ct = default);

    /// <summary>Forgets the session, in memory and in storage.</summary>
    Task SignOutAsync(CancellationToken ct = default);
}
