namespace Winnow.Core.Domain;

/// <summary>
/// What one ingest pass claims about an <see cref="Ownership"/>. Null fields
/// mean "this pass could not tell" and never overwrite stored values.
/// <see cref="Installed"/> is three-valued (unlike <see cref="Ownership.Installed"/>)
/// because some sources cannot inspect install state.
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
