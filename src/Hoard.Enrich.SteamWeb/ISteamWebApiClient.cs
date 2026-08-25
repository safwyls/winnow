using Hoard.Core.Ingest;
using Hoard.Enrich.SteamWeb.Model;

namespace Hoard.Enrich.SteamWeb;

/// <summary>
/// The Steam Web API surface Hoard uses: <c>IPlayerService/GetOwnedGames</c>
/// (§4.2), and nothing else.
///
/// <para><b>Never call this from a user-facing path</b> (§5.1, §9 pitfall 3).
/// One call is normally one request, but §4.2's 429 + <c>Retry-After</c> of
/// 60–120 s means a throttled call can legitimately spend minutes inside the
/// retry policy. Background sync only.</para>
///
/// <para><b>Nothing here throws for an unconfigured or unreachable Steam.</b>
/// No key, a bad key, a dead network and a shape change all produce a result
/// with <see cref="SteamOwnedLibrary.Succeeded"/> false, and the app runs exactly
/// as it does today.</para>
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
    ///
    /// <para>The request always carries the three parameters §4.2 is emphatic
    /// about: <c>include_appinfo=1</c> (without which there are no names),
    /// <c>include_played_free_games=1</c>, and <c>skip_unvetted_apps=false</c>
    /// (without which apps flagged "Profile Features Limited" are silently
    /// omitted — measured live on 2026-08-24 as 7 of 841 titles on the user's own
    /// account).</para>
    ///
    /// <para>Check <see cref="SteamOwnedLibrary.Succeeded"/> before drawing any
    /// conclusion from an empty <see cref="SteamOwnedLibrary.Games"/>.</para>
    /// </summary>
    /// <param name="steamId">The account to query. See <see cref="SteamId"/> for deriving one
    /// from the steam3 <c>userdata</c> folder name the local scan already enumerates.</param>
    /// <param name="cacheTtl">Overrides <see cref="SteamWebOptions.CacheTtl"/>. Pass
    /// <see cref="TimeSpan.Zero"/> or less to force a refetch.</param>
    /// <param name="ct">Cancellation. A cancelled call propagates rather than soft-failing.</param>
    Task<SteamOwnedLibrary> GetOwnedGamesAsync(
        SteamId steamId, TimeSpan? cacheTtl = null, CancellationToken ct = default);

    /// <summary>
    /// The same call, projected onto the §5.1 ingest contract — one
    /// <see cref="CandidateOwnership"/> per owned appid, <c>Source</c>
    /// <see cref="SteamWebApiClient.SourceName"/>.
    ///
    /// <para>An unanswered result yields an empty list. That is safe here
    /// precisely because a candidate feed is additive: emitting nothing means
    /// "this source contributed nothing this pass", never "the library is
    /// empty". Callers that need to tell the two apart should use
    /// <see cref="GetOwnedGamesAsync"/> and read
    /// <see cref="SteamOwnedLibrary.Succeeded"/>.</para>
    ///
    /// <para>See <see cref="SteamOwnedGame.ToCandidate"/> for what the Web API
    /// does and does not know: it never reports install state or install path,
    /// and §4.1 keeps local files authoritative for playtime.</para>
    /// </summary>
    Task<IReadOnlyList<CandidateOwnership>> GetOwnershipCandidatesAsync(
        SteamId steamId, TimeSpan? cacheTtl = null, CancellationToken ct = default);
}
