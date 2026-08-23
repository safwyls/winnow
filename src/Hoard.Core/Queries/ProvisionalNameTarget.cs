namespace Hoard.Core.Queries;

/// <summary>
/// A work still carrying a placeholder name (<c>works.name_is_provisional = 1</c>),
/// paired with the external id enrichment can look it up by.
///
/// <para>These arise when the Steam scan finds an appid in localconfig playtime
/// with no installed manifest to name it: the game is owned and played, but
/// nothing local knows what it is called. Enrichment resolves the real title
/// and promotes it (see the resolver's one-way promotion rule — a real title is
/// never overwritten by a placeholder).</para>
/// </summary>
/// <param name="WorkId">The work to rename.</param>
/// <param name="ReleaseId">
/// Its release. M0/M1 are 1:1, and <c>releases.name</c> is NOT NULL and carries
/// the same placeholder, so both are promoted together — otherwise the release
/// keeps <c>App 1203620</c> forever with nothing to find it by.
/// </param>
/// <param name="Provider">External id provider, e.g. <c>steam</c>.</param>
/// <param name="ProviderId">The provider's id, e.g. the Steam appid.</param>
public sealed record ProvisionalNameTarget(
    long WorkId,
    long ReleaseId,
    string Provider,
    string ProviderId);
