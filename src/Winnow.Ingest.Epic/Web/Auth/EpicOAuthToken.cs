using System.Globalization;

namespace Winnow.Ingest.Epic.Web.Auth;

/// <summary>
/// One Epic OAuth session: bearer token, refresh token, account, and two
/// independent expiries. Bound to the client that minted it.
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
    /// <summary>Redacted to prevent credentials and account ids from reaching log files.</summary>
    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"EpicOAuthToken(expires={ExpiresAt:O}, refreshExpires={RefreshExpiresAt:O}, values redacted)");

    /// <summary>Whether the access token is still usable at <paramref name="now"/>, allowing <paramref name="skew"/> headroom.</summary>
    public bool IsAccessUsable(DateTimeOffset now, TimeSpan skew) => ExpiresAt - now > skew;

    /// <summary>
    /// Whether the refresh token is worth spending. A null <see cref="RefreshExpiresAt"/>
    /// counts as usable since Epic does not always state one.
    /// </summary>
    public bool IsRefreshUsable(DateTimeOffset now, TimeSpan skew)
        => RefreshExpiresAt is not { } expiry || expiry - now > skew;
}
