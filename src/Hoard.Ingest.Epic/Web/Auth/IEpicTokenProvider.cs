namespace Hoard.Ingest.Epic.Web.Auth;

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

    /// <summary>
    /// Epic rejected the authorization code: mistyped, already spent, or past
    /// its very short life. Authorization codes are single-use and expire in
    /// minutes, so this is the common first-attempt failure and the message for
    /// it must say "get a fresh code", not "check your credentials".
    /// </summary>
    InvalidAuthorizationCode,

    /// <summary>Network, DNS, TLS, timeout, or a 5xx the retry policy could not outlast.</summary>
    Unreachable,

    /// <summary>Epic answered with something this client could not parse.</summary>
    UnexpectedResponse,

    /// <summary>
    /// The user backed out of the interactive sign-in: closed the browser
    /// window, declined the consent step, or entered nothing. Deliberate, so the
    /// caller must not retry it, escalate to another prompt, or word it as a
    /// fault.
    /// </summary>
    Cancelled,

    /// <summary>
    /// No interactive prompt could run here. Nothing is wrong with the
    /// credentials or the network: this is a headless host, or one with no
    /// WebView2 runtime and no attached console. The remedy is the documented
    /// console flow (<c>--epic-login</c>), not a retry.
    /// </summary>
    NoInteractivePrompt,

    /// <summary>
    /// A prompt ran but produced no code — Epic changed its sign-in page, or the
    /// flow ended somewhere this client did not recognise. This is the failure
    /// mode <c>docs/spikes/epic-oauth.md</c> §12.3 names as the realistic one,
    /// and the remedy is the console flow while it is fixed.
    /// </summary>
    NoCodeCaptured,

    /// <summary>
    /// Epic's code endpoint answered, and answered that no account is signed in —
    /// every code field present and null.
    ///
    /// <para><b>Separate from <see cref="NoCodeCaptured"/> because the remedies
    /// are opposite.</b> That one means the capture broke and the manual flow is
    /// the way round it; this means the sign-in never completed, which the user
    /// can simply do. The first real run of the embedded flow reported the former
    /// while the latter was true, because the flow started on an endpoint that
    /// only answers for an already-authenticated browser — and the symptom hid
    /// the cause completely.</para>
    /// </summary>
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
/// Owns the Epic OAuth session: exchanges an authorization code for one,
/// refreshes it before it lapses, and gives up cleanly when it cannot.
///
/// <para><b>Nothing here throws for an expected condition.</b> Not configured,
/// not signed in, a refresh token that has expired, Epic unreachable — all of
/// them answer null from <see cref="GetAsync"/>. The caller's response to null is
/// always the same and always safe: contribute no candidates this pass and let
/// the local readers stand. That is the fallback requirement, expressed as a
/// type rather than as a convention someone has to remember.</para>
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

    /// <summary>
    /// Exchanges an authorization code — the one the user copies out of Epic's
    /// redirect page — for a session, and stores it encrypted.
    ///
    /// <para>The code is single-use and short-lived. It is never logged, never
    /// stored, and never placed in a URI.</para>
    /// </summary>
    Task<EpicSignInResult> SignInWithAuthorizationCodeAsync(string authorizationCode, CancellationToken ct = default);

    /// <summary>
    /// Exchanges a launcher <i>exchange</i> code for a session, and stores it
    /// encrypted.
    ///
    /// <para><b>A second grant, not a second spelling of the first.</b> Epic's
    /// sign-in page hands an <c>exchange_code</c> — never an authorization code —
    /// to a host that implements the launcher's <c>window.ue</c> JavaScript
    /// bridge, and that value is only redeemable as
    /// <c>grant_type=exchange_code</c>. Both grants are on the launcher client's
    /// allowlist (<c>docs/spikes/epic-oauth.md</c> §2), so this costs one form
    /// field rather than a second client.</para>
    ///
    /// <para>The embedded-browser prompt is what produces these, and it says
    /// which kind it captured; nothing infers the grant from the string's
    /// shape.</para>
    /// </summary>
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
