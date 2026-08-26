using System.Globalization;

namespace Hoard.Ingest.Epic.Web.Auth;

/// <summary>
/// One Epic OAuth session: the bearer token, the refresh token that renews it,
/// the account they belong to, and the two independent expiries.
///
/// <para><b>Two clocks, not one.</b> Epic's token response carries
/// <c>expires_at</c> and <c>refresh_expires_at</c> separately and they are
/// nothing like each other — the access token is hours, the refresh token is
/// weeks. Collapsing them into a single "expiry" is what turns a routine refresh
/// into a surprise logout: the code would either refresh far too often or
/// discover the session was dead only when a request failed. They are modelled,
/// and checked, apart.</para>
///
/// <para><b>Refresh is rolling.</b> Each successful refresh returns a <i>new</i>
/// refresh token with a new expiry, so a session that is exercised at least
/// occasionally renews indefinitely. The session dies only when the refresh
/// token itself lapses — from a long idle period, a password change, or
/// revocation Epic's side — and that is the case
/// <see cref="IEpicTokenProvider"/> is required to survive by degrading to the
/// local readers rather than by throwing.</para>
///
/// <para><b>Bound to the client that minted it.</b> The client credentials are
/// user-supplied and can change; a token minted by one client must never be sent
/// on behalf of another. <see cref="ClientId"/> makes that check cheap and makes
/// a credential edit invalidate the stored session automatically.</para>
/// </summary>
/// <param name="ClientId">OAuth client the token was minted by. Not a secret, but not logged either.</param>
/// <param name="AccessToken">Bearer credential. Goes into an Authorization header and nowhere else.</param>
/// <param name="RefreshToken">Renewal credential. Goes into a form body and nowhere else.</param>
/// <param name="AccountId">Epic account id — the <c>{accountId}</c> path segment the playtime and library routes need.</param>
/// <param name="DisplayName">Epic display name, or null. Held only so a settings screen can say which account is connected.</param>
/// <param name="ExpiresAt">When <paramref name="AccessToken"/> stops working (UTC).</param>
/// <param name="RefreshExpiresAt">
/// When <paramref name="RefreshToken"/> stops working (UTC), or <b>null when Epic
/// did not say</b> — which is not the same as "expired". See
/// <see cref="IsRefreshUsable"/>.
/// </param>
public sealed record EpicOAuthToken(
    string ClientId,
    string AccessToken,
    string RefreshToken,
    string AccountId,
    string? DisplayName,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RefreshExpiresAt)
{
    /// <summary>
    /// Redacted, and comprehensively so. The compiler-generated record
    /// <c>ToString</c> prints every property, which for this type means both
    /// credentials, the account id and the user's display name — the first time
    /// anyone interpolates one into a log line or an exception message.
    ///
    /// <para>The account id and display name are redacted alongside the tokens
    /// even though neither is a credential: they identify a real person's Epic
    /// account, and a log file is not the place for that.</para>
    /// </summary>
    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"EpicOAuthToken(expires={ExpiresAt:O}, refreshExpires={RefreshExpiresAt:O}, values redacted)");

    /// <summary>Whether the access token is still usable at <paramref name="now"/>, allowing <paramref name="skew"/> headroom.</summary>
    public bool IsAccessUsable(DateTimeOffset now, TimeSpan skew) => ExpiresAt - now > skew;

    /// <summary>
    /// Whether the refresh token is worth spending at <paramref name="now"/>.
    ///
    /// <para><b>A null <see cref="RefreshExpiresAt"/> counts as usable</b>, and
    /// that is the important case. Epic's token response is not guaranteed to
    /// carry a refresh expiry at all — Legendary, the reference implementation
    /// for this whole flow, never reads one and simply retries the refresh — so
    /// this client has to cope with never being told. Treating "not stated" as
    /// "expired" would refuse to refresh a perfectly live session and silently
    /// disable the Epic API forever; treating it as usable costs, at worst, one
    /// request that comes back rejected and degrades exactly like any other
    /// lapsed session. The asymmetry is entirely one-sided, so the unknown is
    /// carried as an unknown rather than collapsed into a false.</para>
    ///
    /// <para>A stated expiry is checked with the same skew as the access token:
    /// a refresh token with thirty seconds left is not worth the request, and
    /// treating it as live only produces a failed call whose outcome is the same
    /// degradation one step later.</para>
    /// </summary>
    public bool IsRefreshUsable(DateTimeOffset now, TimeSpan skew)
        => RefreshExpiresAt is not { } expiry || expiry - now > skew;
}
