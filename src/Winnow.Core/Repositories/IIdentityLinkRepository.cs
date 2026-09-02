using Winnow.Core.Identity;

namespace Winnow.Core.Repositories;

/// <summary>
/// Reads and writes identity links (migration 0018). The library grid
/// collapses linked works into one tile; the details modal lists members
/// under ALSO COVERS.
/// </summary>
public interface IIdentityLinkRepository
{
    /// <summary>The live link map as one immutable snapshot, from one query.</summary>
    Task<IdentityResolution> GetResolutionAsync(CancellationToken ct = default);

    /// <summary>
    /// Links a set of children to one parent under one act, in one transaction.
    /// Re-parents any existing children of a work that is itself becoming a child,
    /// inside the same act. Returns the act id, which is the handle for undo.
    /// </summary>
    Task<long> LinkAsync(IdentityLinkRequest request, CancellationToken ct = default);

    /// <summary>
    /// Retracts a whole act. Returns false when the act has no live links left
    /// (already retracted), which is a no-op rather than an error: retracting
    /// twice is safe because idempotent undo is the fix for the user's complaint
    /// that undo made a pair permanently unmergeable.
    /// </summary>
    Task<bool> RetractActAsync(long actId, string? note = null, CancellationToken ct = default);

    /// <summary>
    /// Retracts ONE child's live link, leaving the rest of its act standing.
    /// This is what "Separate" on the details modal calls, so the user can
    /// undo the one link they noticed from the place they noticed it. It
    /// restores the link that this child's link displaced — the same promise
    /// <see cref="RetractActAsync"/> makes, narrowed to one child. Returns
    /// false when the child has no live link, which is a no-op rather than
    /// an error.
    /// </summary>
    Task<bool> RetractLinkAsync(
        long childWorkId, string? note = null, CancellationToken ct = default);

    /// <summary>
    /// Every row for a work, live and retracted, because the table is the
    /// history. Pass null for all works.
    /// </summary>
    Task<IReadOnlyList<IdentityLink>> GetHistoryAsync(
        long? workId = null, CancellationToken ct = default);

    /// <summary>Every act, ordered by id.</summary>
    Task<IReadOnlyList<IdentityAct>> GetActsAsync(CancellationToken ct = default);
}
