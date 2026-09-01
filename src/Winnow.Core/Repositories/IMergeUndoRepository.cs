using Winnow.Core.Merging;

namespace Winnow.Core.Repositories;

/// <summary>
/// Reverses an applied merge from the row-level journal migration 0017
/// introduced. Two gates. Gate one is cheap, identity-scoped and read-only: it
/// drives the history screen's enabled state and is recomputed on every load.
/// Gate two runs inside the undo's own transaction and proves, statement by
/// statement, that every journalled row is still where the merge left it, that
/// every restore key is free, and that the counts <c>merge_applications</c>
/// recorded still hold. Any drift throws and the transaction rolls back, so the
/// database is untouched, the same shape as the executor's
/// <c>AssertDrainedAsync</c> tripwire.
/// </summary>
public interface IMergeUndoRepository
{
    /// <summary>
    /// Gate one. Writes nothing. Returns the application row, whether it can be
    /// reversed, and every reason it cannot.
    /// </summary>
    Task<MergeUndoPlan> PlanUndoAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// Every applied merge, newest first, each with its gate-one verdict already
    /// computed. One pass, because reversibility is a question about the whole
    /// log and answering it per row would be quadratic.
    /// </summary>
    Task<IReadOnlyList<MergeUndoPlan>> ListUndoPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// Gate two, then the reversal, in one transaction. Restores parents before
    /// children. Throws <see cref="MergeUndoRefusedException"/> when gate one
    /// refuses and <see cref="InvalidOperationException"/> when gate two finds
    /// drift; in both cases nothing is written. On success the
    /// <c>merge_applications</c> row is stamped <c>undone_at</c> and the
    /// <c>merge_candidates</c> pair is set to status <c>undone</c>.
    /// </summary>
    Task<MergeUndoResult> UndoAsync(long applicationId, CancellationToken ct = default);
}
