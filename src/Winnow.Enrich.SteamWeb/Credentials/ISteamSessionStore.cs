namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// Where the Steam session lives between runs. One blob, encrypted, or nothing
/// at all; <see cref="CanPersist"/> is false rather than the store quietly
/// writing something readable.
/// </summary>
public interface ISteamSessionStore
{
    /// <summary>
    /// Whether a saved session would actually survive a restart. False on a host
    /// with no settings table and on one that cannot encrypt. It never means the
    /// session will be written in the clear; there is no such path.
    /// </summary>
    bool CanPersist { get; }

    /// <summary>The stored session, or null when there is none, it cannot be decrypted, or it does not parse.</summary>
    Task<SteamSession?> LoadAsync(CancellationToken ct = default);

    /// <summary>Writes the session, encrypted. A no-op when it cannot be encrypted; the caller keeps working in memory.</summary>
    Task SaveAsync(SteamSession session, CancellationToken ct = default);

    /// <summary>Forgets the stored session. Sign-out, and the only path that discards a refresh token.</summary>
    Task ClearAsync(CancellationToken ct = default);
}
