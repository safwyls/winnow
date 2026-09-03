namespace Winnow.App.Services;

/// <summary>
/// Why a sign-in attempt produced no session, in the Stores panel's vocabulary.
/// Translated from ingest-layer failures by <see cref="StoreConnections"/>.
/// </summary>
public enum StoreSignInProblem
{
    /// <summary>It worked.</summary>
    None = 0,

    /// <summary>
    /// The user closed the window or declined the notice. Deliberate — so the
    /// panel words it as a fact, never as a fault, and never offers to retry
    /// automatically.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Nothing on this machine can show a sign-in window: no WebView2 runtime,
    /// and no console to fall back to. The remedy is <c>--epic-login</c> from a
    /// terminal, which is exactly what the console peer exists for.
    /// </summary>
    NoPromptAvailable,

    /// <summary>
    /// A browser ran and the flow ended without a code. The realistic failure
    /// mode (<c>docs/spikes/epic-oauth.md</c> §12.3): Epic changed its page.
    /// Same remedy as <see cref="NoPromptAvailable"/> — the manual route still
    /// works while the embedded one is fixed.
    /// </summary>
    NoCodeCaptured,

    /// <summary>
    /// Epic rejected the code. Codes are single-use and die within minutes, so
    /// the remedy is a fresh attempt and NOT a credential check.
    /// </summary>
    CodeRejected,

    /// <summary>
    /// Epic rejected the OAuth client itself. Nothing the user can fix by
    /// retrying; the built-in launcher pair has probably been rotated.
    /// </summary>
    ClientRejected,

    /// <summary>Network, DNS, TLS or a 5xx. Try again later.</summary>
    Unreachable,

    /// <summary>No client credentials at all. Effectively unreachable since the built-in pair shipped.</summary>
    NotConfigured,

    /// <summary>Epic answered with something Winnow could not read.</summary>
    Unexpected,
}

/// <summary>
/// What Winnow remembers about a storefront account, with no network call made
/// to find out.
/// </summary>
/// <param name="IsLive">Whether the session is still worth trying. Not proof Epic will honour it.</param>
/// <param name="DisplayName">
/// The account's display name, or null when the provider never supplied one.
/// Never an id, never a token — this is the only account-identifying value the
/// panel is allowed to render.
/// </param>
public sealed record StoreSession(bool IsLive, string? DisplayName);

/// <summary>
/// The outcome of one interactive sign-in, already reduced to what a panel can
/// draw.
/// </summary>
/// <param name="Succeeded">Whether a session now exists.</param>
/// <param name="DisplayName">The signed-in account's display name, when Epic supplied one.</param>
/// <param name="Persisted">
/// False means the session is held in memory for this run only, because this
/// host cannot encrypt it at rest. Worth saying out loud: the user will be asked
/// to sign in again after a restart and would otherwise read that as a bug.
/// </param>
/// <param name="Problem">Why there is no session.</param>
/// <param name="Message">One sentence the panel shows verbatim. Never contains a code, token or URL.</param>
public sealed record StoreSignInOutcome(
    bool Succeeded,
    string? DisplayName,
    bool Persisted,
    StoreSignInProblem Problem,
    string Message);

/// <summary>
/// Sentences shared by the seam and its caller, so that a message the user
/// reads has exactly one definition.
///
/// <para>Only one is here, and only because cancellation can arrive down two
/// paths — a prompt that reports it, and an <c>OperationCanceledException</c>
/// thrown out of a cancelled sign-in — which would otherwise be two places
/// writing the same sentence slightly differently.</para>
/// </summary>
public static class StoreSignInMessages
{
    /// <summary>
    /// Mirrors <c>EpicSignInService.Explain(EpicSignInFailure.Cancelled)</c>
    /// verbatim. Backing out is deliberate, so it is stated as a fact and never
    /// as a fault, and the second sentence is the one that matters: a cancelled
    /// sign-in leaves the local Epic readers exactly as they were.
    /// </summary>
    public const string Cancelled = "Sign-in cancelled. Nothing was changed.";
}

/// <summary>
/// Which Steam credentials exist, with no secret in it.
///
/// <para>The App-layer projection of the credential inventory, in the same
/// spirit as <see cref="StoreSession"/>: the panel learns what is connected
/// without learning that a key chain, a session store or an HTTP client exists.
/// Every field answers a question the screen asks out loud.</para>
/// </summary>
/// <param name="HasApiKey">Whether a Web API key is in force, from any source.</param>
/// <param name="ApiKeyIsAppManaged">
/// Whether the key in force is the one the Stores screen's own field owns — the
/// settings-table key. False for a key supplied through <c>Steam__ApiKey</c> or
/// <c>appsettings.local.json</c>, which the screen can be superseded by but
/// cannot delete, and must therefore say so rather than offering a Clear that
/// would appear not to work.
/// </param>
/// <param name="HasSession">
/// Whether a sign-in session is on the books, live or lapsed. Lapsed is
/// deliberately not absent: "signed in and expired" and "never signed in" need
/// different sentences.
/// </param>
/// <param name="SessionUsable">Whether the session's access token can actually be sent right now.</param>
/// <param name="SessionExpiresAt">When the session's access token dies, read from the token itself.</param>
/// <param name="SessionAccount">
/// The SteamID64 the session belongs to, as an invariant decimal string, or null
/// when there is no session. The only account-identifying value on this record,
/// and never a token.
/// </param>
public sealed record SteamConnection(
    bool HasApiKey,
    bool ApiKeyIsAppManaged,
    bool HasSession,
    bool SessionUsable,
    DateTimeOffset? SessionExpiresAt,
    string? SessionAccount)
{
    /// <summary>The state of an install that has connected nothing.</summary>
    public static readonly SteamConnection None = new(false, false, false, false, null, null);

    /// <summary>Whether anything is registered, usable or not.</summary>
    public bool HasAnyCredential => HasApiKey || HasSession;

    /// <summary>Whether a registered credential can actually be sent right now.</summary>
    public bool HasUsableCredential => HasApiKey || SessionUsable;
}

/// <summary>
/// Everything the Stores panel needs to know and to do, expressed without a
/// single ingest or repository type. Nothing throws; status reads are local only.
/// </summary>
public interface IStoreConnections
{
    /// <summary>
    /// Whether Steam's Web API can be reached on the user's behalf at all —
    /// <b>by any credential</b>, a Web API key or a live sign-in session.
    ///
    /// <para>Answered off the credential inventory rather than off the key chain.
    /// Reading the key alone was TASK-55 S1's noted gap: a keyless user who has
    /// signed in has a working credential and was being told they had none, which
    /// is the "silently degrading" failure the decision note forbids.</para>
    ///
    /// <para>False is the ordinary state of an install nobody has configured, not
    /// an error — the panel says what it costs rather than treating it as a
    /// fault.</para>
    /// </summary>
    ValueTask<bool> IsSteamWebApiConfiguredAsync(CancellationToken ct = default);

    /// <summary>
    /// What Steam credentials exist, in one read and with no secret in the
    /// answer. Makes no request.
    /// </summary>
    ValueTask<SteamConnection> GetSteamConnectionAsync(CancellationToken ct = default);

    /// <summary>
    /// Stores a user-supplied Web API key and puts it into force immediately.
    ///
    /// <para>The credential provider is invalidated as part of the write, so the
    /// next Steam call uses the new key without a restart; a stale memoised key
    /// outliving the field that replaced it is exactly what makes an in-app input
    /// read as broken. Blank input clears instead of storing whitespace.</para>
    /// </summary>
    Task SaveSteamApiKeyAsync(string? key, CancellationToken ct = default);

    /// <summary>
    /// Removes the stored Web API key and puts the removal into force
    /// immediately. A key supplied through configuration is not this method's to
    /// remove and survives; <see cref="SteamConnection.ApiKeyIsAppManaged"/> is
    /// what lets the screen say so first.
    /// </summary>
    Task ClearSteamApiKeyAsync(CancellationToken ct = default);

    /// <summary>
    /// The stored Epic session — live or lapsed — or null when there has never
    /// been one. Makes no request.
    /// </summary>
    ValueTask<StoreSession?> GetEpicSessionAsync(CancellationToken ct = default);

    /// <summary>Runs the interactive Epic sign-in (consent, browser, code, encrypted session).</summary>
    Task<StoreSignInOutcome> SignInToEpicAsync(CancellationToken ct = default);

    /// <summary>Ends the Epic session and deletes the stored credential. Ownership falls back to local files.</summary>
    Task SignOutOfEpicAsync(CancellationToken ct = default);
}
