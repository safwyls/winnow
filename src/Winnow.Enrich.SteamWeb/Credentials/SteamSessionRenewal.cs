namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// How one renewal attempt ended. The caller acts on each value differently:
/// <see cref="Renewed"/> replaces the access token, <see cref="Rejected"/>
/// is unrecoverable and costs the user their session, <see cref="Transient"/>
/// keeps everything and retries next tick.
/// </summary>
public enum SteamRenewalStatus
{
    /// <summary>Steam issued a fresh access token.</summary>
    Renewed,

    /// <summary>
    /// Steam refused the refresh token: revoked, spent, rotated out from under
    /// us, or invalidated by a sign-in elsewhere. No later attempt can succeed;
    /// only a fresh sign-in can. node-steam-session issue #56 (2026-05-20,
    /// unresolved) reports <c>AccessDenied</c> on every refresh route, so this
    /// may be the common case rather than the rare one.
    /// </summary>
    Rejected,

    /// <summary>
    /// Offline, a timeout, a 429 or a 5xx the retry policy could not outlast,
    /// or a response this client could not read. Nothing is cleared and nothing
    /// is latched; the next tick tries again.
    /// </summary>
    Transient,

    /// <summary>
    /// The session carries no refresh token, so no request was sent. Not a
    /// failure and not counted as one.
    /// </summary>
    NotRenewable,
}

/// <summary>
/// What one renewal attempt produced. Carries the two opaque strings and
/// nothing else. <see cref="Reason"/> is drawn from a fixed set of literals
/// and can never quote a response body, a URI or a token.
/// <see cref="ToString"/> is redacted for the same reason
/// <see cref="SteamSession.ToString"/> is.
/// </summary>
public sealed record SteamRenewalOutcome
{
    private SteamRenewalOutcome(
        SteamRenewalStatus status, string? accessToken, string? refreshToken, string reason)
    {
        Status = status;
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        Reason = reason;
    }

    /// <summary>How the attempt ended.</summary>
    public SteamRenewalStatus Status { get; }

    /// <summary>
    /// The freshly minted access token, non-null only when
    /// <see cref="Status"/> is <see cref="SteamRenewalStatus.Renewed"/>.
    /// Never logged.
    /// </summary>
    public string? AccessToken { get; }

    /// <summary>
    /// A rotated refresh token, when Steam issued one. Null means "keep the
    /// one you have", never "you now have none": spending a refresh token can
    /// invalidate the previous one, so discarding the old value on a renewal
    /// that did not replace it would be a self-inflicted sign-out.
    /// </summary>
    public string? RefreshToken { get; }

    /// <summary>
    /// A fixed literal naming which step failed. Safe to log by construction:
    /// no caller can put a response body into it.
    /// </summary>
    public string Reason { get; }

    public override string ToString() => $"SteamRenewalOutcome({Status}, {Reason}, tokens redacted)";

    /// <summary>A successful renewal carrying the fresh access token and an optional rotated refresh token.</summary>
    public static SteamRenewalOutcome Renewed(string accessToken, string? rotatedRefreshToken)
        => new(SteamRenewalStatus.Renewed, accessToken, rotatedRefreshToken, "renewed");

    /// <summary>Steam refused the refresh token. The reason is a fixed literal, never a response body.</summary>
    public static SteamRenewalOutcome Rejected(string reason)
        => new(SteamRenewalStatus.Rejected, null, null, reason);

    /// <summary>A failure worth retrying next tick. Nothing is cleared and nothing is latched.</summary>
    public static SteamRenewalOutcome Transient(string reason)
        => new(SteamRenewalStatus.Transient, null, null, reason);

    /// <summary>No refresh token to spend, so no request was sent.</summary>
    public static SteamRenewalOutcome NotRenewable(string reason)
        => new(SteamRenewalStatus.NotRenewable, null, null, reason);
}

/// <summary>
/// Spends a refresh token for a fresh access token. Deliberately knows
/// nothing about storage, caching, single-flight or health:
/// <see cref="SteamSessionProvider"/> owns all four, and this contract exists
/// so the three-request HTTP exchange can be replaced by a canned responder
/// in a test.
/// </summary>
public interface ISteamSessionRenewer
{
    /// <summary>
    /// One renewal attempt. Never throws for an expected failure; every network
    /// and protocol outcome comes back as a <see cref="SteamRenewalStatus"/>.
    /// </summary>
    Task<SteamRenewalOutcome> RenewAsync(SteamSession session, CancellationToken ct = default);
}
