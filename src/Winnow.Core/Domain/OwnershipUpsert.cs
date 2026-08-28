namespace Winnow.Core.Domain;

/// <summary>
/// What one ingest pass claims about an <see cref="Ownership"/> — the write
/// shape for <c>IOwnershipRepository.UpsertAsync</c>, deliberately not
/// <see cref="Ownership"/> itself.
///
/// <para>The difference is <see cref="Installed"/>. A stored
/// <see cref="Ownership"/> row always has a definite answer, so its
/// <see cref="Ownership.Installed"/> stays a plain <c>bool</c>: the question
/// "is this on disk" is either yes or no once a row exists, and a nullable
/// column would push a third state into every consumer — the filter, the tile,
/// the details pane — that none of them has any use for. A <i>candidate</i>,
/// on the other hand, comes from a source that may have no opinion at all, and
/// that state has to survive as far as the SQL or the write rule cannot express
/// it (§4.1 local files know install state; §4.2's Web API never does).</para>
///
/// <para><see cref="InstallPath"/> is read only when <see cref="Installed"/> is
/// non-null, and the two are written as a pair. "Installed = 0 with a path" and
/// "installed = 1 with no path" are both worse than either honest answer, so
/// the path is never COALESCEd independently of the flag.</para>
/// </summary>
/// <param name="ReleaseId">Release this licence belongs to.</param>
/// <param name="Store">Store key — the other half of the (release_id, store) conflict target.</param>
/// <param name="AccountRef">Source account, or null when this pass cannot name one (refresh, never erase).</param>
/// <param name="AcquiredAt">Acquisition timestamp (UTC), or null when the source does not expose one.</param>
/// <param name="InstallPath">Install directory when <paramref name="Installed"/> is true; null otherwise.</param>
/// <param name="Installed">
/// True/false when the source inspected install state, null when it cannot know.
/// Null leaves both stored install columns untouched; non-null writes both.
/// </param>
public sealed record OwnershipUpsert(
    long ReleaseId,
    string Store,
    string? AccountRef,
    DateTime? AcquiredAt,
    string? InstallPath,
    bool? Installed);
