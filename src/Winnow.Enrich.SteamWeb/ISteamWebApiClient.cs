using Winnow.Core.Ingest;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Model;

namespace Winnow.Enrich.SteamWeb;

/// <summary>
/// The Steam Web API surface Winnow uses: <c>IPlayerService/GetOwnedGames</c>
/// (§4.2). Background sync only; never throws for an unconfigured or
/// unreachable Steam.
/// </summary>
public interface ISteamWebApiClient
{
    /// <summary>
    /// Whether a user-supplied API key was found. False is the ordinary state
    /// for a user who has not entered one, not an error.
    /// </summary>
    ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>
    /// The account's owned library, from cache when a fresh entry exists and from
    /// one <c>GetOwnedGames</c> request otherwise.
    /// </summary>
    /// <param name="steamId">The account to query. See <see cref="SteamId"/> for deriving one
    /// from the steam3 <c>userdata</c> folder name the local scan already enumerates.</param>
    /// <param name="purpose">Which credential kind this call should prefer. Defaults to
    /// <see cref="SteamCredentialPurpose.Unattended"/>, which is what every caller today
    /// is (background sync). A caller a person is waiting on passes
    /// <see cref="SteamCredentialPurpose.UserInitiated"/>.</param>
    /// <param name="cacheTtl">Overrides <see cref="SteamWebOptions.CacheTtl"/>. Pass
    /// <see cref="TimeSpan.Zero"/> or less to force a refetch.</param>
    /// <param name="ct">Cancellation. A cancelled call propagates rather than soft-failing.</param>
    Task<SteamOwnedLibrary> GetOwnedGamesAsync(
        SteamId steamId,
        SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended,
        TimeSpan? cacheTtl = null,
        CancellationToken ct = default);

    /// <summary>
    /// The same call, projected onto the §5.1 ingest contract — one
    /// <see cref="CandidateOwnership"/> per owned appid.
    /// </summary>
    /// <param name="steamId">The account to query.</param>
    /// <param name="purpose">Which credential kind this call should prefer. Defaults to
    /// <see cref="SteamCredentialPurpose.Unattended"/>.</param>
    /// <param name="cacheTtl">Overrides <see cref="SteamWebOptions.CacheTtl"/>.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<CandidateOwnership>> GetOwnershipCandidatesAsync(
        SteamId steamId,
        SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended,
        TimeSpan? cacheTtl = null,
        CancellationToken ct = default);
}
