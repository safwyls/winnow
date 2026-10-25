namespace Winnow.Ingest.Epic.Web.Auth;

/// <summary>Nonsecret account and sign-in generation captured by an ownership operation.</summary>
public sealed record EpicSessionIdentity(string AccountId, string ClientId, long Generation)
{
    public override string ToString() => "EpicSessionIdentity(account redacted)";
}

/// <summary>
/// Owns the Epic OAuth session: exchange, refresh, persistence, and graceful
/// degradation. Returns null from <see cref="GetAsync"/> on any expected failure.
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

    /// <summary>Exchanges an authorization code for a session and stores it encrypted.</summary>
    Task<EpicSignInResult> SignInWithAuthorizationCodeAsync(string authorizationCode, CancellationToken ct = default);

    /// <summary>Exchanges a launcher exchange code for a session and stores it encrypted.</summary>
    Task<EpicSignInResult> SignInWithExchangeCodeAsync(string exchangeCode, CancellationToken ct = default);

    /// <summary>
    /// A usable access token, refreshing first if the current one is spent, or
    /// null when there is no session to be had.
    /// </summary>
    Task<EpicOAuthToken?> GetAsync(CancellationToken ct = default);

    /// <summary>Reads account identity without minting or refreshing a token. Sign-in changes its generation; renewal does not.</summary>
    ValueTask<EpicSessionIdentity?> GetIdentityAsync(CancellationToken ct = default);

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
