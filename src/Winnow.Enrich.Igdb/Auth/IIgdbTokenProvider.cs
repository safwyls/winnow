namespace Winnow.Enrich.Igdb.Auth;

/// <summary>A minted Twitch app access token and the moment it stops being valid (UTC).</summary>
public sealed record IgdbAccessToken(string ClientId, string AccessToken, DateTimeOffset ExpiresAt)
{
    /// <summary>Redacted: the access token is a bearer credential.</summary>
    public override string ToString() => $"IgdbAccessToken(expires={ExpiresAt:O}, value redacted)";
}

/// <summary>
/// Supplies the Twitch client-credentials token IGDB requires (§4.4). Tokens
/// live ~60 days: the provider caches one in memory, persists it so a restart
/// does not re-mint, and only goes back to Twitch on expiry or on an explicit
/// refresh after a 401.
/// </summary>
public interface IIgdbTokenProvider
{
    /// <summary>
    /// A usable token, or null when no credentials are configured. Never throws
    /// for the unconfigured case.
    /// </summary>
    Task<IgdbAccessToken?> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Discards <paramref name="staleToken"/> and mints a replacement. Called by
    /// the auth handler on a 401. Passing the token that failed makes the call
    /// idempotent under concurrency: if another caller already replaced it, this
    /// returns the replacement instead of minting a second one.
    /// </summary>
    Task<IgdbAccessToken?> RefreshAsync(IgdbAccessToken? staleToken, CancellationToken ct = default);
}
