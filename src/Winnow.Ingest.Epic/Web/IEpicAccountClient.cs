using Winnow.Core.Ingest;
using Winnow.Ingest.Epic.Web.Auth;
using Winnow.Ingest.Epic.Web.Model;

namespace Winnow.Ingest.Epic.Web;

/// <summary>
/// Authenticated Epic client: owned library and per-artifact playtime.
/// Complements the local readers; never throws on failure.
/// </summary>
public interface IEpicAccountClient
{
    /// <summary>Whether an OAuth client pair is available. False is normal for unconfigured installs.</summary>
    ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>
    /// Whether a stored session exists that is still worth trying. False with
    /// <see cref="IsConfiguredAsync"/> true is the "configured but signed out"
    /// state — the user has entered credentials and either never signed in or
    /// their refresh token lapsed. Also a clean no-op.
    /// </summary>
    ValueTask<bool> IsSignedInAsync(CancellationToken ct = default);

    /// <summary>Exchanges an authorization code for an encrypted session. Never throws.</summary>
    Task<EpicSignInResult> SignInAsync(string authorizationCode, CancellationToken ct = default);

    /// <summary>Forgets the session, in memory and in encrypted storage.</summary>
    Task SignOutAsync(CancellationToken ct = default);

    /// <summary>
    /// The Epic-hosted page the user visits to obtain an authorization code, or
    /// null when no client pair is configured.
    /// </summary>
    ValueTask<string?> AuthorizationCodeUrl(CancellationToken ct = default);

    /// <summary>The account's owned Epic library, from cache or the library service.</summary>
    /// <param name="cacheTtl">
    /// Overrides <see cref="EpicWebOptions.CacheTtl"/>. Pass
    /// <see cref="TimeSpan.Zero"/> or less to force a refetch.
    /// </param>
    /// <param name="ct">Cancellation. A cancelled call propagates rather than soft-failing.</param>
    Task<EpicOwnedLibrary> GetOwnedLibraryAsync(TimeSpan? cacheTtl = null, CancellationToken ct = default);

    /// <summary>
    /// The owned library projected onto the ingest contract. An unanswered result
    /// yields an empty list, not a claim that the library is empty.
    /// </summary>
    Task<IReadOnlyList<CandidateOwnership>> GetOwnershipCandidatesAsync(
        TimeSpan? cacheTtl = null, CancellationToken ct = default);
}
