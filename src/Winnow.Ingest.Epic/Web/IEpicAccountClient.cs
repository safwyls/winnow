using Winnow.Core.Ingest;
using Winnow.Ingest.Epic.Web.Auth;
using Winnow.Ingest.Epic.Web.Model;

namespace Winnow.Ingest.Epic.Web;

/// <summary>
/// The authenticated Epic surface Winnow uses: the account's owned library, and
/// per-artifact playtime. Nothing else.
///
/// <para><b>This is an alternative source for the same facts the local readers
/// already produce, not a replacement for them</b> (§4.2's union rule, applied
/// to Epic). Neither side is authoritative for the SET. The local files see
/// install state, install paths, third-party-managed titles and the whole
/// catalog without a network call; the API sees the true entitlement list,
/// acquisition dates, and playtime. Games appear in the union; a field one
/// source cannot speak to arrives null and leaves the other's answer alone;
/// <see cref="CandidateOwnershipMerge"/> collapses the overlap.</para>
///
/// <para><b>Never call this from a user-facing path</b> (§5.1). It is an
/// authenticated network call with a retry policy that can legitimately spend a
/// minute inside a backoff. Background sync only.</para>
///
/// <para><b>Nothing here throws for an unconfigured, unauthenticated or
/// unreachable Epic.</b> No credentials, no session, a lapsed refresh token, a
/// dead network and a shape change all produce a result with
/// <see cref="EpicOwnedLibrary.Succeeded"/> false, and the app runs exactly as it
/// does with this module switched off.</para>
/// </summary>
public interface IEpicAccountClient
{
    /// <summary>
    /// Whether a user-supplied OAuth client pair was found. False is the
    /// ordinary state for a user who has not opted in, not an error.
    ///
    /// <para>Deliberately the same shape and the same semantics as
    /// <c>ISteamWebApiClient.IsConfiguredAsync</c>: false means the caller skips
    /// this source entirely and the local ingest runs untouched.</para>
    /// </summary>
    ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>
    /// Whether a stored session exists that is still worth trying. False with
    /// <see cref="IsConfiguredAsync"/> true is the "configured but signed out"
    /// state — the user has entered credentials and either never signed in or
    /// their refresh token lapsed. Also a clean no-op.
    /// </summary>
    ValueTask<bool> IsSignedInAsync(CancellationToken ct = default);

    /// <summary>
    /// Completes the one interactive step: exchanges an authorization code the
    /// user pasted for a session, stored encrypted.
    ///
    /// <para>See <see cref="AuthorizationCodeUrl"/> for where the user gets the
    /// code. Never throws — failures come back as an
    /// <see cref="EpicSignInResult"/> naming a reason the UI can act on.</para>
    /// </summary>
    Task<EpicSignInResult> SignInAsync(string authorizationCode, CancellationToken ct = default);

    /// <summary>Forgets the session, in memory and in encrypted storage.</summary>
    Task SignOutAsync(CancellationToken ct = default);

    /// <summary>
    /// The Epic-hosted page the user visits to obtain an authorization code, or
    /// null when no client pair is configured (there is no URL without a client
    /// id to put in it).
    ///
    /// <para>Winnow never fetches this page and never sees the user's Epic
    /// credentials. The user signs in to Epic directly, in their own browser, and
    /// copies back a single short-lived code.</para>
    /// </summary>
    ValueTask<string?> AuthorizationCodeUrl(CancellationToken ct = default);

    /// <summary>
    /// The account's owned Epic library, from cache when a fresh entry exists and
    /// from the library service otherwise.
    ///
    /// <para>Check <see cref="EpicOwnedLibrary.Succeeded"/> before drawing any
    /// conclusion from an empty <see cref="EpicOwnedLibrary.Items"/>.</para>
    /// </summary>
    /// <param name="cacheTtl">
    /// Overrides <see cref="EpicWebOptions.CacheTtl"/>. Pass
    /// <see cref="TimeSpan.Zero"/> or less to force a refetch.
    /// </param>
    /// <param name="ct">Cancellation. A cancelled call propagates rather than soft-failing.</param>
    Task<EpicOwnedLibrary> GetOwnedLibraryAsync(TimeSpan? cacheTtl = null, CancellationToken ct = default);

    /// <summary>
    /// The same call, projected onto the §5.1 ingest contract — one
    /// <see cref="CandidateOwnership"/> per owned catalog item, <c>Source</c>
    /// <see cref="EpicAccountClient.SourceName"/>.
    ///
    /// <para>An unanswered result yields an empty list. That is safe precisely
    /// because a candidate feed is additive: emitting nothing means "this source
    /// contributed nothing this pass", never "the library is empty". Callers that
    /// need to tell the two apart should use
    /// <see cref="GetOwnedLibraryAsync"/> and read
    /// <see cref="EpicOwnedLibrary.Succeeded"/>.</para>
    /// </summary>
    Task<IReadOnlyList<CandidateOwnership>> GetOwnershipCandidatesAsync(
        TimeSpan? cacheTtl = null, CancellationToken ct = default);
}
