using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

/// <summary>
/// The <c>manual_entries</c> table (migration 0025). A hand-added game is
/// an ordinary work + release + ownership whose ownership store is
/// <c>manual</c> and whose <c>manual_entries</c> row exists. No ingest pass
/// writes this table, and the work, release and ownership rows it created
/// are never deleted or overwritten by sync.
/// </summary>
public interface IManualEntryRepository
{
    /// <summary>
    /// Creates a work, a release, an ownership with store <c>manual</c>,
    /// and a <c>manual_entries</c> row from the draft. Throws
    /// <see cref="ManualEntryConflictException"/> when the IGDB id or Steam
    /// appid already belongs to another work.
    /// </summary>
    Task<ManualEntry> CreateAsync(ManualGameDraft draft, CancellationToken ct = default);

    /// <summary>Returns the hand-added entry for this ownership id, or null.</summary>
    Task<ManualEntry?> GetAsync(long ownershipId, CancellationToken ct = default);

    /// <summary>Every hand-added entry, ordered by title.</summary>
    Task<IReadOnlyList<ManualEntry>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Updates the work, release, ownership and <c>manual_entries</c> row from
    /// the draft. Returns false when the ownership id is not a hand-added entry.
    /// </summary>
    Task<bool> UpdateAsync(long ownershipId, ManualGameDraft draft, CancellationToken ct = default);

    /// <summary>
    /// Deletes the ownership (and the <c>manual_entries</c> row by cascade),
    /// then the release only when no other ownership hangs off it, then the
    /// work only when it has no releases left. Returns false when the
    /// ownership id is not a hand-added entry.
    /// </summary>
    Task<bool> DeleteAsync(long ownershipId, CancellationToken ct = default);
}
