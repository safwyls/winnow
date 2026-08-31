namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// Why the last renewal attempt failed. Recorded rather than inferred, because
/// the two live cases need opposite handling and a caller cannot tell them apart
/// after the fact.
/// </summary>
public enum SteamSessionRenewalFailure
{
    /// <summary>Nothing has failed. The state of every session S2 can produce, because S2 never renews.</summary>
    None,

    /// <summary>Offline, a 5xx, a 429 the retry policy could not outlast. The next attempt may well work; nothing is latched and nothing is cleared.</summary>
    Transient,

    /// <summary>Steam rejected the refresh token: revoked, rotated out from under us, or invalidated by a sign-in elsewhere. No later attempt can succeed; only a fresh sign-in can.</summary>
    Rejected,

    /// <summary>The refresh token's own expiry has passed. Same remedy as <see cref="Rejected"/>, different cause, and worth distinguishing because this one was predictable and should have been surfaced before it happened.</summary>
    Expired,
}

/// <summary>
/// The state of the stored Steam session, as the Stores screen will render it.
///
/// <para>The enum exists because "signed in" is not a boolean. A session can be
/// present and working, present and about to need renewal, present and failing
/// to renew, present and dead, or present and never written to disk at all,
/// and section 4.7's eighth binding condition says the difference has to reach
/// the user <i>before</i> the credential dies. Silently degrading to
/// no-remote-data is the failure mode this enum exists to prevent.</para>
///
/// <para>S2 produces every member except <see cref="RenewalFailing"/>: nothing
/// renews yet, so nothing can fail to.</para>
/// </summary>
public enum SteamSessionHealth
{
    /// <summary>No session is stored. The ordinary state of a fresh install and of every key-only user.</summary>
    NotSignedIn,

    /// <summary>The access token is good and the session is stored encrypted. Nothing to say and nothing to do.</summary>
    Live,

    /// <summary>The access token has expired or is about to, and a refresh token that should be able to replace it is held. Renewal is owed; until S6 lands, nothing pays it.</summary>
    RenewalDue,

    /// <summary>Renewal has been attempted and failed. Surfaced promptly, with one-click re-sign-in, per the legibility condition.</summary>
    RenewalFailing,

    /// <summary>The access token is dead and no usable refresh token remains. Only a fresh sign-in recovers this; an API key is unaffected.</summary>
    Expired,

    /// <summary>The session works, but this host cannot encrypt it, so it was never written. It lasts until the process exits. A refusal, never a plaintext fallback.</summary>
    NotPersisted,
}

/// <summary>
/// A Steam WebView sign-in session: the two secrets it is permitted to hold and
/// the bookkeeping renewal needs.
///
/// <para><b>What is deliberately absent is the point.</b> Section 4.7's second
/// amendment permits exactly two secrets at rest: the minted access token and
/// the refresh token. This type has room for nothing else. No cookie jar,
/// no <c>steamLoginSecure</c>, no <c>sessionid</c>, no browser profile, no page
/// content, and no API key: the key is a different credential with a different
/// lifetime, and mixing the two into one blob would mean one unreadable blob
/// costs the user both.</para>
///
/// <para><see cref="ToString"/> is redacted for the same reason
/// <c>SteamCredential.ToString</c> is: the compiler-generated record
/// <c>ToString</c> would print both tokens the first time anyone interpolated a
/// session into a log line.</para>
/// </summary>
public sealed record SteamSession
{
    public SteamSession(
        string accessToken,
        DateTimeOffset expiresAt,
        IReadOnlyList<string> audience,
        string? issuer,
        SteamId steamId,
        string refreshToken,
        DateTimeOffset? refreshExpiresAt,
        DateTimeOffset mintedAt,
        DateTimeOffset? lastRenewedAt = null,
        int renewalFailures = 0,
        SteamSessionRenewalFailure lastFailureKind = SteamSessionRenewalFailure.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        ArgumentNullException.ThrowIfNull(audience);

        AccessToken = accessToken;
        ExpiresAt = expiresAt;
        Audience = audience;
        Issuer = issuer;
        SteamId = steamId;
        RefreshToken = refreshToken;
        RefreshExpiresAt = refreshExpiresAt;
        MintedAt = mintedAt;
        LastRenewedAt = lastRenewedAt;
        RenewalFailures = renewalFailures < 0 ? 0 : renewalFailures;
        LastFailureKind = lastFailureKind;
    }

    /// <summary>The minted <c>webapi_token</c>. Travels as <c>access_token</c> and nowhere else; never logged.</summary>
    public string AccessToken { get; }

    /// <summary>
    /// When the access token stops being accepted, read from its own <c>exp</c>
    /// claim and never assumed. The measured lifetime is about a day (24 h 22 m,
    /// 2026-08-30) but a client that hard-coded that would be wrong the moment
    /// Valve changed it, and wrong in the direction of sending dead tokens.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>The <c>aud</c> claim. Kept because a token minted for the wrong audience is the failure a 401 will not explain.</summary>
    public IReadOnlyList<string> Audience { get; }

    /// <summary>The <c>iss</c> claim, for the same diagnostic reason as <see cref="Audience"/>.</summary>
    public string? Issuer { get; }

    /// <summary>
    /// The account this session belongs to, from the token's <c>sub</c> claim.
    /// This is what makes sign-in a fuller credential than the API key: the key
    /// has to make a disclosure call to learn the same fact.
    /// </summary>
    public SteamId SteamId { get; }

    /// <summary>The <c>steamRefresh_steam</c> refresh token. The long-lived secret the second amendment was written for; never logged.</summary>
    public string RefreshToken { get; }

    /// <summary>
    /// When the refresh token itself lapses, or null when it did not decode.
    /// Null means "not known", and is never replaced by a guess: writing the
    /// measured ~207 days here would turn an unknown into a date, and a wrong
    /// date in this field either retires a working session early or keeps a dead
    /// one on the books.
    /// </summary>
    public DateTimeOffset? RefreshExpiresAt { get; }

    /// <summary>When the sign-in that produced this session happened.</summary>
    public DateTimeOffset MintedAt { get; }

    /// <summary>When renewal last replaced the access token, or null if it never has. Always null in S2.</summary>
    public DateTimeOffset? LastRenewedAt { get; }

    /// <summary>Consecutive failed renewals, reset to zero on success. Always zero in S2.</summary>
    public int RenewalFailures { get; }

    /// <summary>Why the last renewal failed. Always <see cref="SteamSessionRenewalFailure.None"/> in S2.</summary>
    public SteamSessionRenewalFailure LastFailureKind { get; }

    public override string ToString()
        => $"SteamSession(account={SteamId}, expires={ExpiresAt:O}, failures={RenewalFailures}, tokens redacted)";

    /// <summary>
    /// Whether the access token can still be sent at <paramref name="now"/>,
    /// allowing <paramref name="skew"/> for clock drift and network transit.
    /// </summary>
    public bool IsAccessUsable(DateTimeOffset now, TimeSpan skew) => now + skew < ExpiresAt;

    /// <summary>
    /// Whether the refresh token is worth spending at <paramref name="now"/>. An
    /// unknown expiry counts as usable: Steam is the authority on whether a
    /// refresh token is good, and refusing to try one whose lifetime we could not
    /// read would throw away a working session for lack of a claim.
    /// </summary>
    public bool IsRefreshUsable(DateTimeOffset now, TimeSpan skew)
        => RefreshExpiresAt is not { } expiry || now + skew < expiry;

    /// <summary>Copies this session with the renewal bookkeeping reset, for S6's success path.</summary>
    public SteamSession WithRenewedAccess(
        string accessToken, DateTimeOffset expiresAt, DateTimeOffset renewedAt, string? refreshToken = null)
        => new(
            accessToken,
            expiresAt,
            Audience,
            Issuer,
            SteamId,
            refreshToken ?? RefreshToken,
            RefreshExpiresAt,
            MintedAt,
            renewedAt,
            renewalFailures: 0,
            lastFailureKind: SteamSessionRenewalFailure.None);

    /// <summary>Copies this session with one more consecutive failure recorded, for S6's failure path.</summary>
    public SteamSession WithRenewalFailure(SteamSessionRenewalFailure kind)
        => new(
            AccessToken,
            ExpiresAt,
            Audience,
            Issuer,
            SteamId,
            RefreshToken,
            RefreshExpiresAt,
            MintedAt,
            LastRenewedAt,
            RenewalFailures + 1,
            kind);

    /// <summary>
    /// Builds a session from what a sign-in actually produces: two opaque
    /// strings and the moment they arrived. Everything else is read out of the
    /// tokens rather than supplied, so no caller can assert an expiry or an
    /// account the token does not claim.
    ///
    /// <para>Null when the access token does not decode, states no expiry, or
    /// names no individual account. A malformed token yields no session rather
    /// than an exception, and a half-built session would only move the failure
    /// to the first request.</para>
    /// </summary>
    public static SteamSession? TryCreate(string? accessToken, string? refreshToken, DateTimeOffset mintedAt)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var claims = SteamTokenClaims.Read(accessToken);
        if (!claims.Readable || claims.ExpiresAt is not { } expiresAt || claims.SteamId is not { } steamId)
        {
            return null;
        }

        return new SteamSession(
            accessToken.Trim(),
            expiresAt,
            claims.Audiences,
            claims.Issuer,
            steamId,
            refreshToken.Trim(),
            ReadRefreshExpiry(refreshToken),
            mintedAt);
    }

    /// <summary>
    /// The refresh token's own expiry, or null.
    ///
    /// <para>Steam's cookie values for this family are <c>steamid64||jwt</c>, so
    /// the JWT is taken from after the separator when one is present and from the
    /// whole value when it is not. Anything that does not decode gives null,
    /// which <see cref="IsRefreshUsable"/> treats as usable. A missing claim here
    /// must not become an assumption.</para>
    /// </summary>
    private static DateTimeOffset? ReadRefreshExpiry(string refreshToken)
    {
        var separator = refreshToken.LastIndexOf("||", StringComparison.Ordinal);
        var jwt = separator >= 0 ? refreshToken[(separator + 2)..] : refreshToken;

        return SteamTokenClaims.Read(jwt).ExpiresAt;
    }
}
