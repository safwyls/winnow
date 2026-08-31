using System.Globalization;
using Winnow.Core.Ingest;

namespace Winnow.Core.Auth;

/// <summary>How one sign-in attempt ended. All outcomes are non-exceptional.</summary>
public enum SteamSignInOutcome
{
    /// <summary>The user signed in, a store page minted a token, and the token names the account the page did.</summary>
    SignedIn = 0,

    /// <summary>The user signed in, but no store page carried a token. Nothing was minted, so there is no session.</summary>
    NoToken = 1,

    /// <summary>Nobody signed in before the window closed or the time ran out. The remedy is to sign in, not to retry.</summary>
    NotSignedIn = 2,

    /// <summary>
    /// A token was minted and REFUSED: the page said one account and the token's
    /// subject said another. Never retried automatically, and never persisted.
    /// </summary>
    IdentityMismatch = 3,

    /// <summary>The user backed out, or consent was never recorded. Never retried on its own.</summary>
    Cancelled = 4,

    /// <summary>No embedded browser can run here, or it cannot open a private profile. The key path is unaffected.</summary>
    Unavailable = 5,

    /// <summary>The browser failed in a way the session could not continue past.</summary>
    Failed = 6,
}

/// <summary>
/// One request to sign the user in to Steam in an embedded, user-present browser
/// and mint a <c>webapi_token</c> from the session.
/// </summary>
public sealed record SteamSignInRequest
{
    /// <summary>
    /// Whether the user has been shown what this does and agreed to it.
    ///
    /// <para>Required, and the session refuses to open a window without it,
    /// exactly as <see cref="SteamPageHarvestRequest.ConsentGranted"/> does. The
    /// consent surface belongs to the caller: this flow signs a user into Steam
    /// and keeps a credential that re-mints access to their account, and the
    /// record that they agreed to it must not be something the mechanism can
    /// grant itself.</para>
    /// </summary>
    public required bool ConsentGranted { get; init; }

    /// <summary>
    /// Whether the user separately agreed to have their account pages captured
    /// in the same session.
    ///
    /// <para><b>Default false, and declining is a complete answer.</b> Acceptance
    /// criterion 2: a sign-in with this false is fully functional for account
    /// identity and playtime backfill, and the account pages are then never
    /// navigated to, never scripted and never read. This is a second consent, not
    /// a detail of the first, because signing in and handing over what you bought
    /// are different things to agree to.</para>
    /// </summary>
    public bool CapturePurchaseHistory { get; init; }

    /// <summary>
    /// Whether the session may keep the refresh token Steam left behind, so the
    /// access token can be re-minted for unattended work.
    ///
    /// <para>Default true, because a session that cannot be renewed is a
    /// credential that dies in about a day and cannot serve a scheduled sync —
    /// which is the whole of what section 4.7's second amendment was written to
    /// permit. Setting it false is a real choice with a real cost, and S5's
    /// screen is where the user makes it.</para>
    ///
    /// <para><b>It cannot manufacture a refresh token.</b> Whether Steam issues
    /// one at all depends on the "remember me" box on Steam's own login form, and
    /// that form is never scripted, so nothing here can tick it. This flag says
    /// only whether a refresh token that <i>already exists in the session</i> is
    /// read out of it; <see cref="SteamSignInResult.RefreshTokenCaptured"/> says
    /// whether one actually was.</para>
    /// </summary>
    public bool StaySignedIn { get; init; } = true;

    /// <summary>
    /// How long the whole session may take, sign-in included.
    ///
    /// <para>Generous by default because most of it is the user typing a
    /// password, waiting for a Steam Guard code and reading a captcha.</para>
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How many times the purchase-history load-more control may be clicked.
    /// Ignored unless <see cref="CapturePurchaseHistory"/> is true. Clamped to
    /// <see cref="SteamAccountPagePolicy.MaxAllowedLoadMoreClicks"/>.
    /// </summary>
    public int MaxLoadMoreClicks { get; init; } = SteamAccountPagePolicy.DefaultMaxLoadMoreClicks;

    /// <summary>
    /// How many further licences pages may be followed past the first. Ignored
    /// unless <see cref="CapturePurchaseHistory"/> is true. Clamped to
    /// <see cref="SteamAccountPagePolicy.MaxAllowedLicensesPages"/>.
    /// </summary>
    public int MaxLicensesPages { get; init; } = SteamAccountPagePolicy.DefaultMaxLicensesPages;
}

/// <summary>
/// The outcome of one sign-in: a minted credential and what it says about
/// itself, or a reason there is none. Never an exception.
///
/// <para><b>This type carries live bearer credentials and must never be logged,
/// never printed, and never persisted anywhere but the session store.</b>
/// <see cref="AccessToken"/> spends against Steam's Web API as the account, and
/// <see cref="RefreshToken"/> re-mints access tokens for months. The discipline
/// is the same one <c>SteamPageHarvestResult</c> follows:
/// <see cref="ToString"/> is redacted, so a
/// log line, a debugger watch or a crash dump reports an outcome, an account and
/// an expiry rather than a credential. The compiler-generated record
/// <c>ToString</c> would have printed both tokens the first time anyone
/// interpolated a result into a message.</para>
/// </summary>
public sealed record SteamSignInResult
{
    private SteamSignInResult(
        SteamSignInOutcome outcome,
        string? detail,
        string? accessToken,
        DateTimeOffset? expiresAt,
        string? steamId,
        IReadOnlyList<string> audiences,
        string? issuer,
        string? refreshToken,
        bool refreshTokenCaptured,
        SteamAccountPages? pages,
        int loadMoreClicks,
        SteamLoadMoreDecision? loadMoreStoppedBecause,
        int licensesPagesWalked,
        SteamLoadMoreDecision? licensesStoppedBecause)
    {
        Outcome = outcome;
        Detail = detail;
        AccessToken = accessToken;
        ExpiresAt = expiresAt;
        SteamId = steamId;
        Audiences = audiences;
        Issuer = issuer;
        RefreshToken = refreshToken;
        RefreshTokenCaptured = refreshTokenCaptured;
        Pages = pages;
        LoadMoreClicks = loadMoreClicks;
        LoadMoreStoppedBecause = loadMoreStoppedBecause;
        LicensesPagesWalked = licensesPagesWalked;
        LicensesStoppedBecause = licensesStoppedBecause;
    }

    /// <summary>How the attempt ended.</summary>
    public SteamSignInOutcome Outcome { get; }

    /// <summary>A safe one-line reason, fit to show a user. Never contains a credential or page content.</summary>
    public string? Detail { get; }

    /// <summary>
    /// The minted <c>webapi_token</c>, or null when none was minted. NEVER log,
    /// print or persist this outside the session store.
    /// </summary>
    public string? AccessToken { get; }

    /// <summary>The access token's <c>exp</c> claim, read from the token itself and never assumed.</summary>
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>
    /// The SteamID64 the session belongs to: the token's <c>sub</c> claim, which
    /// was checked against what the page reported before this result was built.
    /// A string rather than a parsed id because Core owns no Steam vocabulary.
    /// </summary>
    public string? SteamId { get; }

    /// <summary>The token's <c>aud</c> claim. Kept because a token minted for the wrong audience is the failure a 401 will not explain.</summary>
    public IReadOnlyList<string> Audiences { get; }

    /// <summary>The token's <c>iss</c> claim, for the same diagnostic reason as <see cref="Audiences"/>.</summary>
    public string? Issuer { get; }

    /// <summary>
    /// The <c>steamRefresh_steam</c> refresh token, or null when none was
    /// captured. NEVER log, print or persist this outside the session store: it
    /// re-mints access to the account for as long as it lives.
    /// </summary>
    public string? RefreshToken { get; }

    /// <summary>
    /// Whether a refresh token was actually captured.
    ///
    /// <para>Reported rather than inferred, and never optimistic. Renewal is the
    /// whole difference between a credential that survives a night and one that
    /// does not, so a caller has to be able to tell "the user declined", "Steam
    /// issued none" and "one was captured" apart. False with
    /// <see cref="Outcome"/> of <see cref="SteamSignInOutcome.SignedIn"/> is a
    /// complete, working sign-in that cannot be renewed.</para>
    /// </summary>
    public bool RefreshTokenCaptured { get; }

    /// <summary>
    /// The captured account pages, or null when the user did not agree to that
    /// capture, or the session never reached them. Null is the ordinary case.
    /// </summary>
    public SteamAccountPages? Pages { get; }

    /// <summary>How many times the purchase-history load-more control was clicked. Zero when no capture was consented.</summary>
    public int LoadMoreClicks { get; }

    /// <summary>
    /// Why the clicking stopped, or null when the history page was never
    /// reached. <see cref="SteamLoadMoreDecision.ReachedCap"/> means the document
    /// is truncated and the parser is not seeing the whole account.
    /// </summary>
    public SteamLoadMoreDecision? LoadMoreStoppedBecause { get; }

    /// <summary>How many further licences pages were followed past the first and merged into the captured document.</summary>
    public int LicensesPagesWalked { get; }

    /// <summary>Why the licences walk stopped, or null when the licences page was never reached.</summary>
    public SteamLoadMoreDecision? LicensesStoppedBecause { get; }

    /// <summary>Whether a usable credential came back. The only condition under which a session may be written.</summary>
    public bool HasSession
        => Outcome == SteamSignInOutcome.SignedIn && !string.IsNullOrWhiteSpace(AccessToken);

    /// <summary>Whether any page content came back. False whenever the purchase-history capture was declined.</summary>
    public bool HasPages => Pages is { IsEmpty: false };

    /// <summary>A sign-in that minted a token whose subject matches the account the page reported.</summary>
    public static SteamSignInResult SignedIn(
        string accessToken,
        DateTimeOffset? expiresAt,
        string? steamId,
        IReadOnlyList<string> audiences,
        string? issuer,
        string? refreshToken,
        SteamAccountPages? pages = null,
        int loadMoreClicks = 0,
        SteamLoadMoreDecision? loadMoreStoppedBecause = null,
        int licensesPagesWalked = 0,
        SteamLoadMoreDecision? licensesStoppedBecause = null,
        string? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentNullException.ThrowIfNull(audiences);

        return new(
            SteamSignInOutcome.SignedIn,
            detail,
            accessToken,
            expiresAt,
            steamId,
            audiences,
            issuer,
            string.IsNullOrWhiteSpace(refreshToken) ? null : refreshToken,
            refreshTokenCaptured: !string.IsNullOrWhiteSpace(refreshToken),
            pages,
            loadMoreClicks,
            loadMoreStoppedBecause,
            licensesPagesWalked,
            licensesStoppedBecause);
    }

    /// <summary>Signed in, and no store page ever handed over a token.</summary>
    public static SteamSignInResult NoToken(string? detail = null)
        => Barren(SteamSignInOutcome.NoToken, detail);

    /// <summary>Nobody signed in.</summary>
    public static SteamSignInResult NotSignedIn(string? detail = null)
        => Barren(SteamSignInOutcome.NotSignedIn, detail);

    /// <summary>
    /// A token arrived and was thrown away because the page's account and the
    /// token's subject named different people. No credential is returned: the
    /// refusal is the result.
    /// </summary>
    public static SteamSignInResult IdentityMismatch(string? detail = null)
        => Barren(SteamSignInOutcome.IdentityMismatch, detail);

    /// <summary>The user backed out, or never agreed in the first place.</summary>
    public static SteamSignInResult Cancelled(string? detail = null)
        => Barren(SteamSignInOutcome.Cancelled, detail);

    /// <summary>No embedded browser here, or no private profile to run it in.</summary>
    public static SteamSignInResult Unavailable(string? detail = null)
        => Barren(SteamSignInOutcome.Unavailable, detail);

    /// <summary>The session ran and broke.</summary>
    public static SteamSignInResult Failed(string? detail = null)
        => Barren(SteamSignInOutcome.Failed, detail);

    private static SteamSignInResult Barren(SteamSignInOutcome outcome, string? detail)
        => new(outcome, detail, null, null, null, [], null, null, false, null, 0, null, 0, null);

    /// <summary>
    /// Outcome, account, expiry and sizes. Neither token is ever rendered into a
    /// string, and the flags say whether one is held rather than showing it.
    /// </summary>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"SteamSignInResult({Outcome}"
        + $"{(Detail is null ? string.Empty : ": " + Detail)}"
        + $", account={SteamId ?? "none"}"
        + $", expires={(ExpiresAt is { } expiry ? expiry.ToString("O", CultureInfo.InvariantCulture) : "unknown")}"
        + $", access token {(AccessToken is null ? "absent" : "held")}"
        + $", refresh token {(RefreshTokenCaptured ? "held" : "absent")}"
        + $"{(Pages is null ? string.Empty : ", " + Pages)}"
        + $", tokens redacted)");
}

/// <summary>
/// Signs the user into Steam in a browser they can see and mints a session
/// credential from it. Contract only (no IO); the implementation lives in
/// Winnow.Auth.WebView.
///
/// <para>Expected failures return a <see cref="SteamSignInResult"/> with a
/// reason, never throw. A host with no embedded browser gets
/// <see cref="SteamSignInOutcome.Unavailable"/>, and the Web API key remains a
/// complete alternative for that user.</para>
/// </summary>
public interface ISteamSignInSession
{
    /// <summary>Short human name, for logs and for telling the user which route ran.</summary>
    string Name { get; }

    /// <summary>Whether this can run on this machine right now. Must not open a window or do IO.</summary>
    ValueTask<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Opens the session, waits for the user to sign in, and mints a token from the signed-in store.</summary>
    Task<SteamSignInResult> SignInAsync(SteamSignInRequest request, CancellationToken ct = default);
}
