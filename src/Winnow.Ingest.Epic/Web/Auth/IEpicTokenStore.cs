namespace Winnow.Ingest.Epic.Web.Auth;

/// <summary>
/// Where a signed-in Epic session survives a restart.
///
/// <para>Nothing here throws. A store that cannot read, cannot write, or holds
/// something it does not understand answers null and accepts the write silently;
/// the session then lives only in memory and the user signs in again. Losing a
/// remembered login is an inconvenience, and it is the correct outcome for every
/// failure this store can have — none of which the user could act on if it were
/// raised as an error.</para>
/// </summary>
public interface IEpicTokenStore
{
    /// <summary>Whether this store can actually persist. False means sign-in works but is not remembered.</summary>
    bool CanPersist { get; }

    /// <summary>The stored session, or null when there is none or it is unreadable.</summary>
    Task<EpicOAuthToken?> LoadAsync(CancellationToken ct = default);

    /// <summary>Replaces the stored session.</summary>
    Task SaveAsync(EpicOAuthToken token, CancellationToken ct = default);

    /// <summary>
    /// Forgets the stored session. Called when the refresh token has lapsed and
    /// when the user signs out — in both cases keeping the dead value would only
    /// produce a failed refresh on every subsequent sync.
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);
}
