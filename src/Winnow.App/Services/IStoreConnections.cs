namespace Winnow.App.Services;

/// <summary>
/// Why a sign-in attempt produced no session, in the Stores panel's own
/// vocabulary.
///
/// <para><b>Why this is not just <c>EpicSignInFailure</c>.</b> §5.1 says the UI
/// reads the database and raises commands and never touches an ingest
/// component. A view model that switched on <c>EpicSignInFailure</c> would be
/// referencing a type out of <c>Winnow.Ingest.Epic</c> — the boundary deleted by
/// an enum rather than by a method call, which is the version of it nobody
/// notices. The translation happens once, in
/// <see cref="StoreConnections"/>, which is an App-layer service and is allowed
/// to see both sides.</para>
///
/// <para>The cases are kept apart because the <i>remedies</i> are genuinely
/// different, and the panel routes on them: two of these send the user to the
/// console flow, one says "try again", one says nothing is wrong at all.</para>
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
///
/// <para><b><see cref="IsLive"/> and "a session exists" are different
/// questions, and the panel needs both.</b> A refresh token that lapsed while
/// the app was closed leaves a stored session that is real, names a real
/// account, and cannot be used. Collapsing the two into one boolean is what
/// turns that into the app silently forgetting who you were — the user signed
/// in, and the panel would say "not connected" with no explanation of what
/// changed.</para>
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
/// Everything the Stores panel needs to know and to do, expressed without a
/// single ingest or repository type.
///
/// <para><b>This is the §5.1 seam for the whole panel, not just for the
/// button.</b> "Is a Steam Web API key set" and "who is the Epic session for"
/// are both questions whose only honest answers live inside ingest modules, so
/// the temptation is to resolve <c>ISteamWebApiClient</c> and
/// <c>IEpicTokenStore</c> in the view model and read them. That is the same
/// boundary violation as calling ingest to do work, arriving through a
/// getter.</para>
///
/// <para><b>Nothing here throws, and nothing here touches the network.</b> The
/// panel is a user-facing path (§5.1, §9 pitfall 3): a status read that could
/// spend a minute inside a retry policy would freeze the screen that exists to
/// explain itself. The two status calls read local state only — a settings row,
/// a DPAPI unprotect — and every failure is a value, never an exception.</para>
///
/// <para><see cref="SignInToEpicAsync"/> is the one long-running member. It
/// waits on a human typing a password and a 2FA code, so it can legitimately
/// take minutes; it must be awaited off the UI thread's critical path and it
/// must be cancellable.</para>
/// </summary>
public interface IStoreConnections
{
    /// <summary>
    /// Whether a Steam Web API key is available. False is the ordinary state of
    /// an install nobody has configured, not an error — and the panel says what
    /// it costs rather than treating it as a fault.
    /// </summary>
    ValueTask<bool> IsSteamWebApiConfiguredAsync(CancellationToken ct = default);

    /// <summary>
    /// The stored Epic session — live or lapsed — or null when there has never
    /// been one. Makes no request.
    /// </summary>
    ValueTask<StoreSession?> GetEpicSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs the interactive Epic sign-in: the consent notice, Epic's own page,
    /// a code, an encrypted session.
    ///
    /// <para><b>The consent notice belongs to this call and must not be
    /// duplicated above it.</b> Every <c>IInteractiveAuthPrompt</c> is required
    /// to show it and take a deliberate accept before anything navigates, which
    /// is where the moment went when the embedded flow stopped showing the user
    /// a code (<c>docs/spikes/embedded-auth.md</c> §8). A second confirmation in
    /// the panel would not add protection; it would train the user to click
    /// through the one that matters.</para>
    /// </summary>
    Task<StoreSignInOutcome> SignInToEpicAsync(CancellationToken ct = default);

    /// <summary>
    /// Ends the Epic session and deletes the stored credential. Epic ownership
    /// falls back to the local launcher files, which is where it comes from by
    /// default anyway — this costs playtime and acquisition dates, never games.
    /// </summary>
    Task SignOutOfEpicAsync(CancellationToken ct = default);
}
