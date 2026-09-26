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
